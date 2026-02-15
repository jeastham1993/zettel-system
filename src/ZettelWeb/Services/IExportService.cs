namespace ZettelWeb.Services;

public interface IExportService
{
    Task<byte[]> ExportAllAsZipAsync();
}
