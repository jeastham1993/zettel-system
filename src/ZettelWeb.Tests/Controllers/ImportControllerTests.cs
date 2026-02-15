using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Controllers;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Controllers;

public class ImportControllerTests
{
    [Fact]
    public async Task Import_ReturnsOkWithResult()
    {
        var fakeService = new FakeImportService(
            new ImportResult(1, 1, 0, new List<string> { "20260213120000" }));
        var controller = new ImportController(fakeService);

        var result = await controller.Import(new[]
        {
            new ImportFileRequest("note.md", "Content"),
        });

        var okResult = Assert.IsType<OkObjectResult>(result);
        var importResult = Assert.IsType<ImportResult>(okResult.Value);
        Assert.Equal(1, importResult.Imported);
        Assert.Single(importResult.NoteIds);
    }

    [Fact]
    public async Task Import_PassesFilesToService()
    {
        var fakeService = new FakeImportService(
            new ImportResult(2, 2, 0, new List<string> { "id1", "id2" }));
        var controller = new ImportController(fakeService);

        await controller.Import(new[]
        {
            new ImportFileRequest("a.md", "Content A"),
            new ImportFileRequest("b.md", "Content B"),
        });

        Assert.Equal(2, fakeService.LastFiles!.Count);
        Assert.Equal("a.md", fakeService.LastFiles[0].FileName);
        Assert.Equal("Content B", fakeService.LastFiles[1].Content);
    }

    [Fact]
    public async Task Import_EmptyArray_ReturnsOk()
    {
        var fakeService = new FakeImportService(
            new ImportResult(0, 0, 0, new List<string>()));
        var controller = new ImportController(fakeService);

        var result = await controller.Import(Array.Empty<ImportFileRequest>());

        var okResult = Assert.IsType<OkObjectResult>(result);
        var importResult = Assert.IsType<ImportResult>(okResult.Value);
        Assert.Equal(0, importResult.Imported);
    }

    private class FakeImportService : IImportService
    {
        private readonly ImportResult _result;
        public IReadOnlyList<ImportFile>? LastFiles { get; private set; }

        public FakeImportService(ImportResult result) => _result = result;

        public Task<ImportResult> ImportMarkdownAsync(IReadOnlyList<ImportFile> files)
        {
            LastFiles = files;
            return Task.FromResult(_result);
        }
    }
}
