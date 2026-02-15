using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExportController : ControllerBase
{
    private readonly IExportService _exportService;

    public ExportController(IExportService exportService)
    {
        _exportService = exportService;
    }

    [HttpGet]
    public async Task<IActionResult> Export()
    {
        var zipBytes = await _exportService.ExportAllAsZipAsync();

        return File(zipBytes, "application/zip", "zettel-export.zip");
    }
}
