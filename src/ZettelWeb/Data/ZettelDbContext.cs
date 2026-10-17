using Microsoft.EntityFrameworkCore;
using ZettelWeb.Models;

namespace ZettelWeb.Data;

public class ZettelDbContext : DbContext
{
    public ZettelDbContext(DbContextOptions<ZettelDbContext> options)
        : base(options)
    {
    }

    public DbSet<Note> Notes => Set<Note>();
    public DbSet<NoteTag> NoteTags => Set<NoteTag>();
    public DbSet<NoteVersion> NoteVersions => Set<NoteVersion>();
    public DbSet<ResearchAgenda> ResearchAgendas => Set<ResearchAgenda>();
    public DbSet<ResearchTask> ResearchTasks => Set<ResearchTask>();
    public DbSet<ResearchFinding> ResearchFindings => Set<ResearchFinding>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Note>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(21);
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.Content).IsRequired();

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(NoteStatus.Permanent);

            entity.Property(e => e.Source).HasMaxLength(20);

            entity.Property(e => e.EnrichStatus)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(EnrichStatus.None);

            entity.Property(e => e.EmbedStatus)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(EmbedStatus.Pending);

            entity.Property(e => e.NoteType)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(NoteType.Regular);

            entity.Property(e => e.SourceType).HasMaxLength(20);

            entity.Property(e => e.EmbeddingModel).HasMaxLength(100);

            entity.HasMany(e => e.Tags)
                .WithOne()
                .HasForeignKey(t => t.NoteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Versions)
                .WithOne()
                .HasForeignKey(v => v.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NoteTag>(entity =>
        {
            entity.HasKey(e => new { e.NoteId, e.Tag });
            entity.Property(e => e.NoteId).HasMaxLength(21);
        });

        modelBuilder.Entity<NoteVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.NoteId).HasMaxLength(21).IsRequired();
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.Content).IsRequired();
        });

        modelBuilder.Entity<ResearchAgenda>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(21);
            entity.Property(e => e.TriggeredFromNoteId).HasMaxLength(21);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(ResearchAgendaStatus.Pending);
            entity.HasMany(e => e.Tasks)
                .WithOne()
                .HasForeignKey(t => t.AgendaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ResearchTask>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(21);
            entity.Property(e => e.AgendaId).HasMaxLength(21).IsRequired();
            entity.Property(e => e.Query).IsRequired();
            entity.Property(e => e.Motivation).IsRequired();
            entity.Property(e => e.MotivationNoteId).HasMaxLength(21);
            entity.Property(e => e.SourceType)
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(ResearchTaskStatus.Pending);
            entity.HasIndex(e => e.AgendaId);
            // I2: Restrict delete — findings must be explicitly handled before a task can be deleted
            entity.HasMany(e => e.Findings)
                .WithOne()
                .HasForeignKey(f => f.TaskId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ResearchFinding>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(21);
            entity.Property(e => e.TaskId).HasMaxLength(21).IsRequired();
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.Synthesis).IsRequired();
            entity.Property(e => e.SourceUrl).IsRequired();
            entity.Property(e => e.AcceptedFleetingNoteId).HasMaxLength(21);
            entity.Property(e => e.SourceType)
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(ResearchFindingStatus.Pending);
            entity.Property(e => e.SimilarNoteIds)
                .HasColumnType("jsonb");
            entity.HasIndex(e => e.TaskId);
            // I9: composite index covers WHERE Status = 'Pending' ORDER BY CreatedAt DESC
            entity.HasIndex(e => new { e.Status, e.CreatedAt });
        });

        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(21);
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.ContextNoteIds)
                .HasColumnType("jsonb");
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(ChatSessionStatus.Active);
            entity.HasMany(e => e.Messages)
                .WithOne()
                .HasForeignKey(m => m.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(21);
            entity.Property(e => e.SessionId).HasMaxLength(21).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.ReferenceNoteIds)
                .HasColumnType("jsonb");
            entity.Property(e => e.Role)
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
