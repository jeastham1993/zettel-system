using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

/// <summary>Export all notes as a ZIP archive with YAML front matter.</summary>
[ApiController]
[Route("api/[controller]")]
public class ExportController : ControllerBase
{
    private readonly IExportService _exportService;

    public ExportController(IExportService exportService)
    {
        _exportService = exportService;
    }

    /// <summary>Export all notes as a ZIP file containing markdown with YAML front matter.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(FileResult), 200, "application/zip")]
    public async Task<IActionResult> Export()
    {
        var zipBytes = await _exportService.ExportAllAsZipAsync();

        return File(zipBytes, "application/zip", "zettel-export.zip");
    }
}
