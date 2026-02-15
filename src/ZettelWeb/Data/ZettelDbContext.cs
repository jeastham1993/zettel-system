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
    }
}
