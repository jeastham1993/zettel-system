using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

public record ImportFileRequest(string FileName, string Content);

[ApiController]
[Route("api/[controller]")]
public class ImportController : ControllerBase
{
    private readonly IImportService _importService;

    public ImportController(IImportService importService)
    {
        _importService = importService;
    }

    [HttpPost]
    [RequestSizeLimit(10_485_760)] // 10 MB
    public async Task<IActionResult> Import([FromBody] ImportFileRequest[] files)
    {
        var importFiles = files
            .Select(f => new ImportFile(f.FileName, f.Content))
            .ToList();

        var result = await _importService.ImportMarkdownAsync(importFiles);

        return Ok(result);
    }
}
