using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Controllers;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Controllers;

public class ExportControllerTests
{
    [Fact]
    public async Task Export_ReturnsFileResult()
    {
        var fakeService = new FakeExportService(new byte[] { 0x50, 0x4B });
        var controller = new ExportController(fakeService);

        var result = await controller.Export();

        Assert.IsType<FileContentResult>(result);
    }

    [Fact]
    public async Task Export_ReturnsZipContentType()
    {
        var fakeService = new FakeExportService(new byte[] { 0x50, 0x4B });
        var controller = new ExportController(fakeService);

        var result = await controller.Export();

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/zip", fileResult.ContentType);
    }

    [Fact]
    public async Task Export_ReturnsCorrectFilename()
    {
        var fakeService = new FakeExportService(new byte[] { 0x50, 0x4B });
        var controller = new ExportController(fakeService);

        var result = await controller.Export();

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("zettel-export.zip", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task Export_ReturnsServiceBytes()
    {
        var expectedBytes = new byte[] { 1, 2, 3, 4, 5 };
        var fakeService = new FakeExportService(expectedBytes);
        var controller = new ExportController(fakeService);

        var result = await controller.Export();

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal(expectedBytes, fileResult.FileContents);
    }

    private class FakeExportService : IExportService
    {
        private readonly byte[] _bytes;

        public FakeExportService(byte[] bytes) => _bytes = bytes;

        public Task<byte[]> ExportAllAsZipAsync() => Task.FromResult(_bytes);
    }
}
