using ZettelWeb.Models;

namespace ZettelWeb.Services;

public interface IImportService
{
    Task<ImportResult> ImportMarkdownAsync(IReadOnlyList<ImportFile> files);
}
