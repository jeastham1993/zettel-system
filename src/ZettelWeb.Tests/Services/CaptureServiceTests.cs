using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Services;

public class CaptureServiceTests : IDisposable
{
    private readonly FakeNoteService _noteService = new();
    private readonly FakeEnrichmentQueue _enrichmentQueue = new();
    private readonly ZettelDbContext _db;

    public CaptureServiceTests()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ZettelDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private CaptureService CreateService(CaptureConfig? config = null)
    {
        config ??= new CaptureConfig
        {
            AllowedEmailSenders = ["james@example.com", "test@example.com"],
            AllowedTelegramChatIds = [123456789, 987654321],
        };

        return new CaptureService(
            _noteService,
            _enrichmentQueue,
            _db,
            Options.Create(config),
            NullLogger<CaptureService>.Instance);
    }

    // --- CaptureAsync ---

    [Fact]
    public async Task CaptureAsync_CreatesFleetingNote()
    {
        var svc = CreateService();

        var note = await svc.CaptureAsync("A quick thought", "email");

        Assert.NotNull(note);
        Assert.Equal("A quick thought", _noteService.LastFleetingContent);
        Assert.Equal("email", _noteService.LastFleetingSource);
    }

    [Fact]
    public async Task CaptureAsync_AddsSourceTag()
    {
        var svc = CreateService();

        await svc.CaptureAsync("A thought", "telegram");

        Assert.Contains("source:telegram", _noteService.LastFleetingTags!);
    }

    [Fact]
    public async Task CaptureAsync_PreservesAdditionalTags()
    {
        var svc = CreateService();

        await svc.CaptureAsync("A thought", "email", ["reading", "ideas"]);

        Assert.Contains("source:email", _noteService.LastFleetingTags!);
        Assert.Contains("reading", _noteService.LastFleetingTags!);
        Assert.Contains("ideas", _noteService.LastFleetingTags!);
    }

    [Fact]
    public async Task CaptureAsync_WithUrl_EnqueuesForEnrichment()
    {
        var svc = CreateService();

        var note = await svc.CaptureAsync(
            "Check this out https://example.com/article", "email");

        Assert.Single(_enrichmentQueue.EnqueuedIds);
        Assert.Equal(note!.Id, _enrichmentQueue.EnqueuedIds[0]);
    }

    [Fact]
    public async Task CaptureAsync_WithoutUrl_DoesNotEnqueue()
    {
        var svc = CreateService();

        await svc.CaptureAsync("Just a plain thought, no links", "telegram");

        Assert.Empty(_enrichmentQueue.EnqueuedIds);
    }

    [Fact]
    public async Task CaptureAsync_WithHttpsUrl_EnqueuesForEnrichment()
    {
        var svc = CreateService();

        await svc.CaptureAsync("Visit https://secure.example.com/page", "email");

        Assert.Single(_enrichmentQueue.EnqueuedIds);
    }

    [Fact]
    public async Task CaptureAsync_WithHttpUrl_EnqueuesForEnrichment()
    {
        var svc = CreateService();

        await svc.CaptureAsync("Visit http://old.example.com/page", "email");

        Assert.Single(_enrichmentQueue.EnqueuedIds);
    }

    // --- ParseEmailPayload ---

    [Fact]
    public void ParseEmailPayload_ValidPayloadWithSubject_ReturnsContent()
    {
        var svc = CreateService();
        var payload = JsonDocument.Parse("""
        {
            "from": "james@example.com",
            "subject": "Quick Thought",
            "text": "Something I want to remember"
        }
        """).RootElement;

        var (content, isValid) = svc.ParseEmailPayload(payload);

        Assert.True(isValid);
        Assert.Equal("Quick Thought\n\nSomething I want to remember", content);
    }

    [Fact]
    public void ParseEmailPayload_ValidPayloadWithoutSubject_ReturnsBodyOnly()
    {
        var svc = CreateService();
        var payload = JsonDocument.Parse("""
        {
            "from": "james@example.com",
            "text": "Something I want to remember"
        }
        """).RootElement;

        var (content, isValid) = svc.ParseEmailPayload(payload);

        Assert.True(isValid);
        Assert.Equal("Something I want to remember", content);
    }

    [Fact]
    public void ParseEmailPayload_UnknownSender_ReturnsInvalid()
    {
        var svc = CreateService();
        var payload = JsonDocument.Parse("""
        {
            "from": "stranger@evil.com",
            "subject": "Spam",
            "text": "Buy my product"
        }
        """).RootElement;

        var (_, isValid) = svc.ParseEmailPayload(payload);

        Assert.False(isValid);
    }

    [Fact]
    public void ParseEmailPayload_EmptyBody_ReturnsInvalid()
    {
        var svc = CreateService();
        var payload = JsonDocument.Parse("""
        {
            "from": "james@example.com",
            "subject": "Empty",
            "text": ""
        }
        """).RootElement;

        var (_, isValid) = svc.ParseEmailPayload(payload);

        Assert.False(isValid);
    }

    [Fact]
    public void ParseEmailPayload_HtmlFallback_StripsHtmlTags()
    {
        var svc = CreateService();
        var payload = JsonDocument.Parse("""
        {
            "from": "james@example.com",
            "subject": "HTML Note",
            "html": "<p>Important <strong>thought</strong></p>"
        }
        """).RootElement;

        var (content, isValid) = svc.ParseEmailPayload(payload);

        Assert.True(isValid);
        Assert.Equal("HTML Note\n\nImportant thought", content);
    }

    [Fact]
    public void ParseEmailPayload_NameAndEmailFormat_ExtractsEmail()
    {
        var svc = CreateService();
        var payload = JsonDocument.Parse("""
        {
            "from": "James Eastham <james@example.com>",
            "text": "A thought"
        }
        """).RootElement;

        var (content, isValid) = svc.ParseEmailPayload(payload);

        Assert.True(isValid);
        Assert.Equal("A thought", content);
    }

    [Fact]
    public void ParseEmailPayload_MissingFromField_ReturnsInvalid()
    {
        var svc = CreateService();
        var payload = JsonDocument.Parse("""
        {
            "text": "No sender"
        }
        """).RootElement;

        var (_, isValid) = svc.ParseEmailPayload(payload);

        Assert.False(isValid);
    }

    // --- ParseTelegramUpdate ---

    [Fact]
    public void ParseTelegramUpdate_ValidMessage_ReturnsContent()
    {
        var svc = CreateService();
        var update = JsonDocument.Parse("""
        {
            "update_id": 100,
            "message": {
                "message_id": 200,
                "from": { "id": 789 },
                "chat": { "id": 123456789 },
                "text": "Check this out https://example.com interesting article"
            }
        }
        """).RootElement;

        var (content, isValid) = svc.ParseTelegramUpdate(update);

        Assert.True(isValid);
        Assert.Equal("Check this out https://example.com interesting article", content);
    }

    [Fact]
    public void ParseTelegramUpdate_UnknownChat_ReturnsInvalid()
    {
        var svc = CreateService();
        var update = JsonDocument.Parse("""
        {
            "update_id": 100,
            "message": {
                "message_id": 200,
                "chat": { "id": 999999 },
                "text": "Should be rejected"
            }
        }
        """).RootElement;

        var (_, isValid) = svc.ParseTelegramUpdate(update);

        Assert.False(isValid);
    }

    [Fact]
    public void ParseTelegramUpdate_NoMessage_ReturnsInvalid()
    {
        var svc = CreateService();
        var update = JsonDocument.Parse("""
        {
            "update_id": 100
        }
        """).RootElement;

        var (_, isValid) = svc.ParseTelegramUpdate(update);

        Assert.False(isValid);
    }

    [Fact]
    public void ParseTelegramUpdate_EmptyText_ReturnsInvalid()
    {
        var svc = CreateService();
        var update = JsonDocument.Parse("""
        {
            "update_id": 100,
            "message": {
                "message_id": 200,
                "chat": { "id": 123456789 },
                "text": ""
            }
        }
        """).RootElement;

        var (_, isValid) = svc.ParseTelegramUpdate(update);

        Assert.False(isValid);
    }

    [Fact]
    public void ParseTelegramUpdate_NoText_ReturnsInvalid()
    {
        var svc = CreateService();
        var update = JsonDocument.Parse("""
        {
            "update_id": 100,
            "message": {
                "message_id": 200,
                "chat": { "id": 123456789 }
            }
        }
        """).RootElement;

        var (_, isValid) = svc.ParseTelegramUpdate(update);

        Assert.False(isValid);
    }

    [Fact]
    public void ParseTelegramUpdate_ForwardedMessage_UsesMessageText()
    {
        var svc = CreateService();
        var update = JsonDocument.Parse("""
        {
            "update_id": 100,
            "message": {
                "message_id": 200,
                "from": { "id": 789 },
                "chat": { "id": 123456789 },
                "forward_from": { "id": 111 },
                "text": "Forwarded content to save"
            }
        }
        """).RootElement;

        var (content, isValid) = svc.ParseTelegramUpdate(update);

        Assert.True(isValid);
        Assert.Equal("Forwarded content to save", content);
    }

    // --- IsAllowedEmailSender ---

    [Fact]
    public void IsAllowedEmailSender_ExactMatch_ReturnsTrue()
    {
        var svc = CreateService();

        Assert.True(svc.IsAllowedEmailSender("james@example.com"));
    }

    [Fact]
    public void IsAllowedEmailSender_CaseInsensitive_ReturnsTrue()
    {
        var svc = CreateService();

        Assert.True(svc.IsAllowedEmailSender("James@Example.COM"));
    }

    [Fact]
    public void IsAllowedEmailSender_NameFormat_ReturnsTrue()
    {
        var svc = CreateService();

        Assert.True(svc.IsAllowedEmailSender("James Eastham <james@example.com>"));
    }

    [Fact]
    public void IsAllowedEmailSender_UnknownSender_ReturnsFalse()
    {
        var svc = CreateService();

        Assert.False(svc.IsAllowedEmailSender("nobody@evil.com"));
    }

    [Fact]
    public void IsAllowedEmailSender_EmptyList_ReturnsFalse()
    {
        var svc = CreateService(new CaptureConfig { AllowedEmailSenders = [] });

        Assert.False(svc.IsAllowedEmailSender("james@example.com"));
    }

    // --- IsAllowedTelegramChat ---

    [Fact]
    public void IsAllowedTelegramChat_KnownId_ReturnsTrue()
    {
        var svc = CreateService();

        Assert.True(svc.IsAllowedTelegramChat(123456789));
    }

    [Fact]
    public void IsAllowedTelegramChat_UnknownId_ReturnsFalse()
    {
        var svc = CreateService();

        Assert.False(svc.IsAllowedTelegramChat(999));
    }

    [Fact]
    public void IsAllowedTelegramChat_EmptyList_ReturnsFalse()
    {
        var svc = CreateService(new CaptureConfig { AllowedTelegramChatIds = [] });

        Assert.False(svc.IsAllowedTelegramChat(123456789));
    }

    // --- ParseSesNotification ---

    [Fact]
    public void ParseSesNotification_PlainTextEmail_ReturnsSubjectAndBody()
    {
        var svc = CreateService();
        var notification = JsonDocument.Parse("""
        {
            "notificationType": "Received",
            "mail": {
                "source": "james@example.com",
                "commonHeaders": {
                    "from": ["James Eastham <james@example.com>"],
                    "subject": "Quick Thought"
                }
            },
            "content": "From: james@example.com\r\nTo: capture@example.com\r\nSubject: Quick Thought\r\nContent-Type: text/plain; charset=UTF-8\r\n\r\nSomething I want to remember"
        }
        """).RootElement;

        var (content, isValid) = svc.ParseSesNotification(notification);

        Assert.True(isValid);
        Assert.Equal("Quick Thought\n\nSomething I want to remember", content);
    }

    [Fact]
    public void ParseSesNotification_UnknownSender_ReturnsInvalid()
    {
        var svc = CreateService();
        var notification = JsonDocument.Parse("""
        {
            "notificationType": "Received",
            "mail": {
                "source": "stranger@evil.com",
                "commonHeaders": {
                    "from": ["stranger@evil.com"],
                    "subject": "Spam"
                }
            },
            "content": "From: stranger@evil.com\r\nSubject: Spam\r\nContent-Type: text/plain\r\n\r\nBuy my product"
        }
        """).RootElement;

        var (_, isValid) = svc.ParseSesNotification(notification);

        Assert.False(isValid);
    }

    [Fact]
    public void ParseSesNotification_NoContent_ReturnsInvalid()
    {
        var svc = CreateService();
        var notification = JsonDocument.Parse("""
        {
            "notificationType": "Received",
            "mail": {
                "source": "james@example.com",
                "commonHeaders": {
                    "from": ["james@example.com"],
                    "subject": "No body"
                }
            }
        }
        """).RootElement;

        var (_, isValid) = svc.ParseSesNotification(notification);

        Assert.False(isValid);
    }

    [Fact]
    public void ParseSesNotification_MultipartEmail_ExtractsPlainTextPart()
    {
        var svc = CreateService();
        var mime = "From: james@example.com\r\n" +
                   "Subject: Multipart Test\r\n" +
                   "Content-Type: multipart/alternative; boundary=\"boundary123\"\r\n" +
                   "\r\n" +
                   "--boundary123\r\n" +
                   "Content-Type: text/plain; charset=UTF-8\r\n" +
                   "\r\n" +
                   "Plain text body here\r\n" +
                   "--boundary123\r\n" +
                   "Content-Type: text/html; charset=UTF-8\r\n" +
                   "\r\n" +
                   "<html><body>HTML body</body></html>\r\n" +
                   "--boundary123--";

        var json = $$"""
        {
            "notificationType": "Received",
            "mail": {
                "source": "james@example.com",
                "commonHeaders": {
                    "from": ["james@example.com"],
                    "subject": "Multipart Test"
                }
            },
            "content": {{JsonSerializer.Serialize(mime)}}
        }
        """;

        var notification = JsonDocument.Parse(json).RootElement;
        var (content, isValid) = svc.ParseSesNotification(notification);

        Assert.True(isValid);
        Assert.Equal("Multipart Test\n\nPlain text body here", content);
    }

    [Fact]
    public void ParseSesNotification_MissingMailField_ReturnsInvalid()
    {
        var svc = CreateService();
        var notification = JsonDocument.Parse("""
        {
            "notificationType": "Received"
        }
        """).RootElement;

        var (_, isValid) = svc.ParseSesNotification(notification);

        Assert.False(isValid);
    }

    [Fact]
    public void ParseSesNotification_NameEmailFormat_ExtractsEmail()
    {
        var svc = CreateService();
        var notification = JsonDocument.Parse("""
        {
            "notificationType": "Received",
            "mail": {
                "source": "james@example.com",
                "commonHeaders": {
                    "from": ["James Eastham <james@example.com>"],
                    "subject": "Named sender"
                }
            },
            "content": "From: James Eastham <james@example.com>\r\nSubject: Named sender\r\nContent-Type: text/plain\r\n\r\nBody from named sender"
        }
        """).RootElement;

        var (content, isValid) = svc.ParseSesNotification(notification);

        Assert.True(isValid);
        Assert.Equal("Named sender\n\nBody from named sender", content);
    }

    // --- End-to-end scenarios ---

    [Fact]
    public async Task EndToEnd_EmailWithUrl_CreatesNoteAndEnqueues()
    {
        var svc = CreateService();
        var payload = JsonDocument.Parse("""
        {
            "from": "james@example.com",
            "subject": "Read Later",
            "text": "Great article https://blog.example.com/post"
        }
        """).RootElement;

        var (content, isValid) = svc.ParseEmailPayload(payload);
        Assert.True(isValid);

        var note = await svc.CaptureAsync(content, "email");

        Assert.NotNull(note);
        Assert.Equal(NoteStatus.Fleeting, note.Status);
        Assert.Equal("email", note.Source);
        Assert.Contains("source:email", _noteService.LastFleetingTags!);
        Assert.Single(_enrichmentQueue.EnqueuedIds);
    }

    [Fact]
    public async Task EndToEnd_TelegramPlainText_CreatesNoteWithoutEnrichment()
    {
        var svc = CreateService();
        var update = JsonDocument.Parse("""
        {
            "update_id": 1,
            "message": {
                "message_id": 1,
                "chat": { "id": 123456789 },
                "text": "Remember to buy milk"
            }
        }
        """).RootElement;

        var (content, isValid) = svc.ParseTelegramUpdate(update);
        Assert.True(isValid);

        var note = await svc.CaptureAsync(content, "telegram");

        Assert.NotNull(note);
        Assert.Equal(NoteStatus.Fleeting, note.Status);
        Assert.Contains("source:telegram", _noteService.LastFleetingTags!);
        Assert.Empty(_enrichmentQueue.EnqueuedIds);
    }

    [Fact]
    public async Task EndToEnd_InvalidSender_NoNoteCreated()
    {
        var svc = CreateService();
        var payload = JsonDocument.Parse("""
        {
            "from": "attacker@evil.com",
            "text": "Malicious content"
        }
        """).RootElement;

        var (_, isValid) = svc.ParseEmailPayload(payload);
        Assert.False(isValid);
        // Controller would return 200 without creating a note
        Assert.Empty(_noteService.CreatedNotes);
    }
}
