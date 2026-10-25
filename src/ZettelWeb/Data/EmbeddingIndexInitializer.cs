using Microsoft.EntityFrameworkCore;
using ZettelWeb.Services;

namespace ZettelWeb.Data;

/// <summary>
/// Startup maintenance for the pgvector HNSW embedding index (Program.cs calls
/// <see cref="EnsureIndex"/> after migrations).
///
/// The HNSW index is dimension-bound. Stored embeddings whose dimension doesn't
/// match the configured one mean the embedding model changed — those vectors are
/// invalid for the new model, and the index cannot even be built over them.
/// In that case the stale index is dropped and every note with an embedding is
/// requeued (embedding nulled, status Pending) for the background service to
/// re-embed. This turns a provider switch into: change config, restart, wait
/// (see INFRA-004/INFRA-005).
/// </summary>
public static class EmbeddingIndexInitializer
{
    public const string IndexName = "idx_notes_embedding_hnsw";

    public static void EnsureIndex(this ZettelDbContext db, int dimensions, ILogger logger)
    {
        if (dimensions <= 0 || dimensions > 4096)
        {
            return; // HNSW supports at most 2000 dims; pgvector caps lower in practice
        }

#pragma warning disable EF1003 // Intentional raw SQL for DDL
        // The column is real[] (pgvector's implicit cast makes queries work);
        // vector_dims needs an explicit cast to vector.
        var staleCount = db.Database
            .SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"Notes\" WHERE \"Embedding\" IS NOT NULL AND vector_dims(\"Embedding\"::vector) <> {dimensions}")
            .First();

        if (staleCount > 0)
        {
            logger.LogWarning(
                "Embedding dimension changed: {Count} stored vectors don't match the configured {Dimensions} dims — dropping HNSW index and requeueing all notes for re-embedding",
                staleCount, dimensions);

            db.Database.ExecuteSqlRaw($"DROP INDEX IF EXISTS {IndexName};");
            db.Database.ExecuteSqlRaw(
                $"UPDATE \"Notes\" SET \"Embedding\" = NULL, \"EmbedStatus\" = 'Pending', \"EmbedError\" = NULL " +
                $"WHERE \"Embedding\" IS NOT NULL AND vector_dims(\"Embedding\"::vector) <> {dimensions}");
        }

        db.Database.ExecuteSqlRaw(
            $"CREATE INDEX IF NOT EXISTS {IndexName} " +
            $"ON \"Notes\" USING hnsw ((\"Embedding\"::vector({dimensions})) vector_cosine_ops) " +
            $"WHERE \"Embedding\" IS NOT NULL;");
#pragma warning restore EF1003
    }
}
