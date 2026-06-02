using CrestApps.Core.AI.Documents;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.Tests.Framework.AI.Documents;

public sealed class UploadedFileScannerTests
{
    [Fact]
    public async Task NoOpScanner_AlwaysReturnsClean()
    {
        var scanner = new NoOpUploadedFileScanner();
        var file = CreateFakeFormFile("test.pdf", 1024);

        var result = await scanner.ScanAsync(file, TestContext.Current.CancellationToken);

        Assert.True(result.IsSafe);
        Assert.Equal(FileScanStatus.Clean, result.Status);
    }

    [Fact]
    public async Task NoOpScanner_WithNullFile_ReturnsClean()
    {
        var scanner = new NoOpUploadedFileScanner();

        var result = await scanner.ScanAsync(null!, TestContext.Current.CancellationToken);

        Assert.True(result.IsSafe);
        Assert.Equal(FileScanStatus.Clean, result.Status);
    }

    [Fact]
    public void FileScanResult_Clean_IsSafe()
    {
        var result = FileScanResult.Clean;

        Assert.True(result.IsSafe);
        Assert.Equal(FileScanStatus.Clean, result.Status);
        Assert.Null(result.ThreatName);
    }

    [Fact]
    public void FileScanResult_Infected_IsNotSafe()
    {
        var result = FileScanResult.Infected("Win32.Eicar.Test", "Test virus detected");

        Assert.False(result.IsSafe);
        Assert.Equal(FileScanStatus.Infected, result.Status);
        Assert.Equal("Win32.Eicar.Test", result.ThreatName);
        Assert.Equal("Test virus detected", result.Message);
    }

    [Fact]
    public void FileScanResult_Infected_WithDefaultMessage()
    {
        var result = FileScanResult.Infected("Trojan.Generic");

        Assert.False(result.IsSafe);
        Assert.Equal("Malicious content detected: Trojan.Generic", result.Message);
    }

    [Fact]
    public void FileScanResult_Error_IsNotSafe()
    {
        var result = FileScanResult.Error("Scanner unavailable");

        Assert.False(result.IsSafe);
        Assert.Equal(FileScanStatus.Error, result.Status);
        Assert.Equal("Scanner unavailable", result.Message);
        Assert.Null(result.ThreatName);
    }

    [Fact]
    public async Task CustomScanner_ReturnsInfected_BlocksUpload()
    {
        var scanner = new InfectedFileScanner();
        var file = CreateFakeFormFile("malware.exe", 2048);

        var result = await scanner.ScanAsync(file, TestContext.Current.CancellationToken);

        Assert.False(result.IsSafe);
        Assert.Equal(FileScanStatus.Infected, result.Status);
        Assert.Equal("Eicar.Test", result.ThreatName);
    }

    [Fact]
    public async Task CustomScanner_ReturnsError_BlocksUpload()
    {
        var scanner = new ErrorFileScanner();
        var file = CreateFakeFormFile("document.docx", 512);

        var result = await scanner.ScanAsync(file, TestContext.Current.CancellationToken);

        Assert.False(result.IsSafe);
        Assert.Equal(FileScanStatus.Error, result.Status);
    }

    [Fact]
    public async Task CustomScanner_WithCancellation_Throws()
    {
        var scanner = new SlowFileScanner();
        var file = CreateFakeFormFile("large.zip", 10_000_000);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scanner.ScanAsync(file, cts.Token));
    }

    [Fact]
    public void FileScanResult_Clean_IsSingleton()
    {
        var a = FileScanResult.Clean;
        var b = FileScanResult.Clean;

        Assert.Same(a, b);
    }

    private static FormFile CreateFakeFormFile(string fileName, long length)
    {
        var stream = new MemoryStream(new byte[length]);

        return new FormFile(stream, 0, length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream",
        };
    }

    private sealed class InfectedFileScanner : IUploadedFileScanner
    {
        public Task<FileScanResult> ScanAsync(IFormFile file, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(FileScanResult.Infected("Eicar.Test"));
        }
    }

    private sealed class ErrorFileScanner : IUploadedFileScanner
    {
        public Task<FileScanResult> ScanAsync(IFormFile file, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(FileScanResult.Error("Scanner timeout"));
        }
    }

    private sealed class SlowFileScanner : IUploadedFileScanner
    {
        public async Task<FileScanResult> ScanAsync(IFormFile file, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

            return FileScanResult.Clean;
        }
    }
}
