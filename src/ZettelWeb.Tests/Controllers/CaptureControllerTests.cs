using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZettelWeb.Controllers;
using ZettelWeb.Data;
using ZettelWeb.Services;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Controllers;

public class CaptureControllerTests : IDisposable
{
    private readonly FakeNoteService _noteService = new();
    private readonly FakeEnrichmentQueue _enrichmentQueue = new();
    private readonly ZettelDbContext _db;

    public CaptureControllerTests()
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

    private CaptureController CreateController(CaptureConfig? config = null)
    {
        config ??= new CaptureConfig
        {
            AllowedEmailSenders = ["james@example.com"],
            AllowedTelegramChatIds = [123456789],
            WebhookSecret = "test-email-secret",
            TelegramBotToken = "test-telegram-token",
        };

        var service = new CaptureService(
            _noteService,
            _enrichmentQueue,
            _db,
            Options.Create(config),
            NullLogger<CaptureService>.Instance);

        var controller = new CaptureController(
            service,
            Options.Create(config),
            NullLogger<CaptureController>.Instance);

        // Set up HttpContext so Request.Headers works
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static CaptureController WithHeaders(CaptureController controller, Dictionary<string, string> headers)
    {
        foreach (var (key, value) in headers)
        {
            controller.HttpContext.Request.Headers[key] = value;
        }
        return controller;
    }

    // --- Email webhook ---

    [Fact]
    public async Task EmailWebhook_ValidSender_ReturnsOkAndCreatesNote()
    {
        var controller = CreateController();
        WithHeaders(controller, new() { ["X-Webhook-Secret"] = "test-email-secret" });
        var payload = JsonDocument.Parse("""
        {
            "from": "james@example.com",
            "subject": "Quick note",
            "text": "Remember this"
        }
        """).RootElement;

        var result = await controller.EmailWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Single(_noteService.CreatedNotes);
        Assert.Equal("email", _noteService.LastFleetingSource);
    }

    [Fact]
    public async Task EmailWebhook_InvalidSender_ReturnsOkWithoutCreatingNote()
    {
        var controller = CreateController();
        WithHeaders(controller, new() { ["X-Webhook-Secret"] = "test-email-secret" });
        var payload = JsonDocument.Parse("""
        {
            "from": "spam@evil.com",
            "text": "Buy stuff"
        }
        """).RootElement;

        var result = await controller.EmailWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Empty(_noteService.CreatedNotes);
    }

    [Fact]
    public async Task EmailWebhook_EmptyBody_ReturnsOkWithoutCreatingNote()
    {
        var controller = CreateController();
        WithHeaders(controller, new() { ["X-Webhook-Secret"] = "test-email-secret" });
        var payload = JsonDocument.Parse("""
        {
            "from": "james@example.com",
            "text": ""
        }
        """).RootElement;

        var result = await controller.EmailWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Empty(_noteService.CreatedNotes);
    }

    // --- Telegram webhook ---

    [Fact]
    public async Task TelegramWebhook_ValidChat_ReturnsOkAndCreatesNote()
    {
        var controller = CreateController();
        WithHeaders(controller, new() { ["X-Telegram-Bot-Api-Secret-Token"] = "test-telegram-token" });
        var payload = JsonDocument.Parse("""
        {
            "update_id": 1,
            "message": {
                "message_id": 1,
                "chat": { "id": 123456789 },
                "text": "Save this thought"
            }
        }
        """).RootElement;

        var result = await controller.TelegramWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Single(_noteService.CreatedNotes);
        Assert.Equal("telegram", _noteService.LastFleetingSource);
    }

    [Fact]
    public async Task TelegramWebhook_UnknownChat_ReturnsOkWithoutCreatingNote()
    {
        var controller = CreateController();
        WithHeaders(controller, new() { ["X-Telegram-Bot-Api-Secret-Token"] = "test-telegram-token" });
        var payload = JsonDocument.Parse("""
        {
            "update_id": 1,
            "message": {
                "message_id": 1,
                "chat": { "id": 999 },
                "text": "Should be rejected"
            }
        }
        """).RootElement;

        var result = await controller.TelegramWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Empty(_noteService.CreatedNotes);
    }

    [Fact]
    public async Task TelegramWebhook_NoMessage_ReturnsOkWithoutCreatingNote()
    {
        var controller = CreateController();
        WithHeaders(controller, new() { ["X-Telegram-Bot-Api-Secret-Token"] = "test-telegram-token" });
        var payload = JsonDocument.Parse("""
        {
            "update_id": 1
        }
        """).RootElement;

        var result = await controller.TelegramWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Empty(_noteService.CreatedNotes);
    }

    [Fact]
    public async Task TelegramWebhook_AlwaysReturns200_EvenForInvalidPayload()
    {
        var controller = CreateController();
        WithHeaders(controller, new() { ["X-Telegram-Bot-Api-Secret-Token"] = "test-telegram-token" });
        var payload = JsonDocument.Parse("{}").RootElement;

        var result = await controller.TelegramWebhook(payload);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task EmailWebhook_AlwaysReturns200_EvenForInvalidPayload()
    {
        var controller = CreateController();
        WithHeaders(controller, new() { ["X-Webhook-Secret"] = "test-email-secret" });
        var payload = JsonDocument.Parse("{}").RootElement;

        var result = await controller.EmailWebhook(payload);

        Assert.IsType<OkResult>(result);
    }

    // --- C1: Webhook Authentication Tests ---

    [Fact]
    public async Task TelegramWebhook_WithToken_ValidHeader_CreatesNote()
    {
        var config = new CaptureConfig
        {
            AllowedTelegramChatIds = [123456789],
            TelegramBotToken = "my-secret-token",
        };
        var controller = CreateController(config);
        WithHeaders(controller, new() { ["X-Telegram-Bot-Api-Secret-Token"] = "my-secret-token" });

        var payload = JsonDocument.Parse("""
        {
            "update_id": 1,
            "message": { "message_id": 1, "chat": { "id": 123456789 }, "text": "Hello" }
        }
        """).RootElement;

        var result = await controller.TelegramWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Single(_noteService.CreatedNotes);
    }

    [Fact]
    public async Task TelegramWebhook_WithToken_InvalidHeader_RejectsWithOk()
    {
        var config = new CaptureConfig
        {
            AllowedTelegramChatIds = [123456789],
            TelegramBotToken = "my-secret-token",
        };
        var controller = CreateController(config);
        WithHeaders(controller, new() { ["X-Telegram-Bot-Api-Secret-Token"] = "wrong-token" });

        var payload = JsonDocument.Parse("""
        {
            "update_id": 1,
            "message": { "message_id": 1, "chat": { "id": 123456789 }, "text": "Hello" }
        }
        """).RootElement;

        var result = await controller.TelegramWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Empty(_noteService.CreatedNotes);
    }

    [Fact]
    public async Task TelegramWebhook_WithToken_MissingHeader_RejectsWithOk()
    {
        var config = new CaptureConfig
        {
            AllowedTelegramChatIds = [123456789],
            TelegramBotToken = "my-secret-token",
        };
        var controller = CreateController(config);
        // No header set

        var payload = JsonDocument.Parse("""
        {
            "update_id": 1,
            "message": { "message_id": 1, "chat": { "id": 123456789 }, "text": "Hello" }
        }
        """).RootElement;

        var result = await controller.TelegramWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Empty(_noteService.CreatedNotes);
    }

    [Fact]
    public async Task TelegramWebhook_EmptyToken_RejectsRequest()
    {
        var config = new CaptureConfig
        {
            AllowedTelegramChatIds = [123456789],
            TelegramBotToken = "",  // No token configured = reject
        };
        var controller = CreateController(config);

        var payload = JsonDocument.Parse("""
        {
            "update_id": 1,
            "message": { "message_id": 1, "chat": { "id": 123456789 }, "text": "Hello" }
        }
        """).RootElement;

        var result = await controller.TelegramWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Empty(_noteService.CreatedNotes);
    }

    [Fact]
    public async Task EmailWebhook_WithSecret_ValidHeader_CreatesNote()
    {
        var config = new CaptureConfig
        {
            AllowedEmailSenders = ["james@example.com"],
            WebhookSecret = "email-webhook-secret",
        };
        var controller = CreateController(config);
        WithHeaders(controller, new() { ["X-Webhook-Secret"] = "email-webhook-secret" });

        var payload = JsonDocument.Parse("""
        {
            "from": "james@example.com",
            "text": "A thought"
        }
        """).RootElement;

        var result = await controller.EmailWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Single(_noteService.CreatedNotes);
    }

    [Fact]
    public async Task EmailWebhook_WithSecret_InvalidHeader_RejectsWithOk()
    {
        var config = new CaptureConfig
        {
            AllowedEmailSenders = ["james@example.com"],
            WebhookSecret = "email-webhook-secret",
        };
        var controller = CreateController(config);
        WithHeaders(controller, new() { ["X-Webhook-Secret"] = "wrong-secret" });

        var payload = JsonDocument.Parse("""
        {
            "from": "james@example.com",
            "text": "A thought"
        }
        """).RootElement;

        var result = await controller.EmailWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Empty(_noteService.CreatedNotes);
    }

    [Fact]
    public async Task EmailWebhook_WithSecret_MissingHeader_RejectsWithOk()
    {
        var config = new CaptureConfig
        {
            AllowedEmailSenders = ["james@example.com"],
            WebhookSecret = "email-webhook-secret",
        };
        var controller = CreateController(config);
        // No header set

        var payload = JsonDocument.Parse("""
        {
            "from": "james@example.com",
            "text": "A thought"
        }
        """).RootElement;

        var result = await controller.EmailWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Empty(_noteService.CreatedNotes);
    }

    [Fact]
    public async Task EmailWebhook_EmptySecret_RejectsRequest()
    {
        var config = new CaptureConfig
        {
            AllowedEmailSenders = ["james@example.com"],
            WebhookSecret = "",  // No secret configured = reject
        };
        var controller = CreateController(config);

        var payload = JsonDocument.Parse("""
        {
            "from": "james@example.com",
            "text": "A thought"
        }
        """).RootElement;

        var result = await controller.EmailWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Empty(_noteService.CreatedNotes);
    }

    [Fact]
    public async Task TelegramWebhook_NullToken_RejectsRequest()
    {
        var config = new CaptureConfig
        {
            AllowedTelegramChatIds = [123456789],
            TelegramBotToken = null!,
        };
        var controller = CreateController(config);

        var payload = JsonDocument.Parse("""
        {
            "update_id": 1,
            "message": { "message_id": 1, "chat": { "id": 123456789 }, "text": "Hello" }
        }
        """).RootElement;

        var result = await controller.TelegramWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Empty(_noteService.CreatedNotes);
    }

    [Fact]
    public async Task EmailWebhook_NullSecret_RejectsRequest()
    {
        var config = new CaptureConfig
        {
            AllowedEmailSenders = ["james@example.com"],
            WebhookSecret = null!,
        };
        var controller = CreateController(config);

        var payload = JsonDocument.Parse("""
        {
            "from": "james@example.com",
            "text": "A thought"
        }
        """).RootElement;

        var result = await controller.EmailWebhook(payload);

        Assert.IsType<OkResult>(result);
        Assert.Empty(_noteService.CreatedNotes);
    }
}
