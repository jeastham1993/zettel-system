using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

/// <summary>A file to import as a note.</summary>
public record ImportFileRequest(string FileName, string Content);

/// <summary>Import notes from Notion-compatible markdown files.</summary>
[ApiController]
[Route("api/import")]
[Produces("application/json")]
public class ImportController : ControllerBase
{
    private readonly IImportService _importService;

    public ImportController(IImportService importService)
    {
        _importService = importService;
    }

    /// <summary>Import an array of markdown files as notes.</summary>
    /// <remarks>Request body is limited to 10 MB.</remarks>
    [HttpPost]
    [RequestSizeLimit(10_485_760)] // 10 MB
    [ProducesResponseType<ImportResult>(200)]
    public async Task<IActionResult> Import([FromBody] ImportFileRequest[] files)
    {
        var importFiles = files
            .Select(f => new ImportFile(f.FileName, f.Content))
            .ToList();

        var result = await _importService.ImportMarkdownAsync(importFiles);

        return Ok(result);
    }
}
