using Amazon.Lambda.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZettelWeb.Data;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ZettelWeb.Lambda;

/// <summary>
/// Runs EF Core migrations and creates the HNSW vector index.
/// Invoked once by Terraform (aws_lambda_invocation) before the API Lambda image is updated.
/// This replaces the db.Database.Migrate() call that previously ran at startup.
/// </summary>
public class MigrationHandler
{
    public async Task<MigrationResult> HandleAsync(MigrationRequest request, ILambdaContext context)
    {
        context.Logger.LogInformation("Starting database migration");

        var services = LambdaServiceProvider.Build();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

        try
        {
            var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
            var migrationList = pendingMigrations.ToList();

            context.Logger.LogInformation(
                "Found {Count} pending migrations: {Migrations}",
                migrationList.Count,
                string.Join(", ", migrationList));

            await db.Database.MigrateAsync();

            context.Logger.LogInformation("Migrations applied successfully");

            // HNSW index creation — dimension-dependent, so cannot live in a static migration.
            // SAFETY: ExecuteSqlRaw with string interpolation is intentional.
            // `dimensions` is validated to the range 1–4096 from configuration, not user input.
            var dimensions = configuration.GetValue<int>("Embedding:Dimensions");
            if (dimensions > 0 && dimensions <= 4096)
            {
#pragma warning disable EF1003
                await db.Database.ExecuteSqlRawAsync(
                    $"CREATE INDEX IF NOT EXISTS idx_notes_embedding_hnsw " +
                    $"ON \"Notes\" USING hnsw ((\"Embedding\"::vector({dimensions})) vector_cosine_ops) " +
                    $"WHERE \"Embedding\" IS NOT NULL;");
#pragma warning restore EF1003

                context.Logger.LogInformation(
                    "HNSW index ensured for {Dimensions} dimensions", dimensions);
            }

            return new MigrationResult(Success: true, AppliedMigrations: migrationList, Error: null);
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Migration failed: {Message}", ex.Message);
            return new MigrationResult(Success: false, AppliedMigrations: [], Error: ex.Message);
        }
    }
}

public record MigrationRequest(string Action = "migrate");
public record MigrationResult(bool Success, List<string> AppliedMigrations, string? Error);
