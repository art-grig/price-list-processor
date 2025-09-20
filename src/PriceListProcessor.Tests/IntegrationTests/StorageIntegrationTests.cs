using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Tests.Fixtures;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace PriceListProcessor.Tests.IntegrationTests;

public class StorageIntegrationTests : IClassFixture<TestWebApplicationFactory>, IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public StorageIntegrationTests(TestWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await _factory.CleanupTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.CleanupTestDataAsync();
    }

    private void SetupTestContext([System.Runtime.CompilerServices.CallerMemberName] string testMethodName = "")
    {
        _factory.SetTestContext(testMethodName);
        _output.WriteLine($"Test ID: {_factory.TestId}");
    }

    [Fact]
    public async Task UploadAndDownloadFile_ShouldWorkCorrectly()
    {
        // Setup test context
        SetupTestContext();
        
        // Arrange
        
        using var scope = _factory.Services.CreateScope();
        var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

        var fileName = $"test-upload-{_factory.TestId}.csv";
        var content = "Product,Price\nTest Product,99.99\nAnother Product,149.99";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Act - Upload
        var key = await storageService.UploadFileAsync(fileName, contentBytes);

        // Assert - Upload
        key.Should().NotBeNullOrEmpty();
        key.Should().Contain(_factory.TestId, "key should contain test ID for isolation");
        key.Should().Contain(fileName, "key should contain original filename");

        // Act - Download
        var downloadedBytes = await storageService.DownloadFileAsync(key);
        var downloadedContent = Encoding.UTF8.GetString(downloadedBytes);

        // Assert - Download
        downloadedContent.Should().Be(content, "downloaded content should match uploaded content");

        _output.WriteLine($"Uploaded file with key: {key}");
        _output.WriteLine($"Downloaded content matches: {downloadedContent == content}");
    }

    [Fact]
    public async Task UploadAndDownloadStream_ShouldWorkCorrectly()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_factory.TestId}");
        
        using var scope = _factory.Services.CreateScope();
        var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

        var fileName = $"test-stream-{_factory.TestId}.csv";
        var content = "Product,Price,Quantity\nStream Product 1,199.99,10\nStream Product 2,299.99,5";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Act - Upload
        var key = await storageService.UploadFileAsync(fileName, contentBytes);

        // Act - Download as stream
        using var downloadStream = await storageService.DownloadFileStreamAsync(key);
        using var reader = new StreamReader(downloadStream);
        var downloadedContent = await reader.ReadToEndAsync();

        // Assert
        downloadedContent.Should().Be(content, "streamed content should match uploaded content");
        key.Should().Contain(_factory.TestId, "key should be isolated by test ID");

        _output.WriteLine($"Stream download successful for key: {key}");
    }

    [Fact]
    public async Task UploadMultipleFiles_ShouldIsolateByTestId()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_factory.TestId}");
        
        using var scope = _factory.Services.CreateScope();
        var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

        var files = new[]
        {
            (Name: $"file1-{_factory.TestId}.csv", Content: "Product,Price\nFile1 Product,10.00"),
            (Name: $"file2-{_factory.TestId}.csv", Content: "Product,Price\nFile2 Product,20.00"),
            (Name: $"file3-{_factory.TestId}.csv", Content: "Product,Price\nFile3 Product,30.00")
        };

        // Act - Upload all files
        var keys = new List<string>();
        foreach (var (name, content) in files)
        {
            var contentBytes = Encoding.UTF8.GetBytes(content);
            var key = await storageService.UploadFileAsync(name, contentBytes);
            keys.Add(key);
        }

        // Assert - All keys should be unique and contain test ID
        keys.Should().HaveCount(3);
        keys.Should().OnlyHaveUniqueItems("all keys should be unique");
        keys.Should().AllSatisfy(key => key.Should().Contain(_factory.TestId, "all keys should contain test ID"));

        // Act - Download and verify all files
        for (int i = 0; i < files.Length; i++)
        {
            var downloadedBytes = await storageService.DownloadFileAsync(keys[i]);
            var downloadedContent = Encoding.UTF8.GetString(downloadedBytes);
            downloadedContent.Should().Be(files[i].Content, $"file {i+1} content should match");
        }

        _output.WriteLine($"Successfully uploaded and verified {keys.Count} files");
        _output.WriteLine($"Keys: {string.Join(", ", keys.Select(k => k.Split('/').Last()))}");
    }

    [Fact]
    public async Task DeleteFile_ShouldRemoveFileSuccessfully()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_factory.TestId}");
        
        using var scope = _factory.Services.CreateScope();
        var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

        var fileName = $"test-delete-{_factory.TestId}.csv";
        var content = "Product,Price\nDelete Test Product,99.99";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Act - Upload file
        var key = await storageService.UploadFileAsync(fileName, contentBytes);
        
        // Verify file exists
        var downloadedBytes = await storageService.DownloadFileAsync(key);
        downloadedBytes.Should().NotBeEmpty("file should exist after upload");

        // Act - Delete file
        await storageService.DeleteFileAsync(key);

        // Assert - File should no longer exist
        var downloadAction = async () => await storageService.DownloadFileAsync(key);
        await downloadAction.Should().ThrowAsync<Exception>("file should not exist after deletion");

        _output.WriteLine($"Successfully deleted file with key: {key}");
    }

    [Fact]
    public async Task UploadLargeFile_ShouldHandleCorrectly()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_factory.TestId}");
        
        using var scope = _factory.Services.CreateScope();
        var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

        var fileName = $"large-file-{_factory.TestId}.csv";
        var contentBuilder = new StringBuilder("Product,SKU,Price,Quantity,Category\n");
        
        // Generate 10,000 rows
        for (int i = 1; i <= 10000; i++)
        {
            contentBuilder.AppendLine($"Product-{i},SKU-{i:D6},{i * 1.5m:F2},{i % 100 + 1},Category-{i % 5 + 1}");
        }
        
        var content = contentBuilder.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Act - Upload large file
        var key = await storageService.UploadFileAsync(fileName, contentBytes);

        // Act - Download and verify
        var downloadedBytes = await storageService.DownloadFileAsync(key);
        var downloadedContent = Encoding.UTF8.GetString(downloadedBytes);

        // Assert
        downloadedContent.Should().Be(content, "large file content should match");
        downloadedBytes.Length.Should().Be(contentBytes.Length, "file sizes should match");

        // Verify it's actually a large file
        contentBytes.Length.Should().BeGreaterThan(100000, "test file should be reasonably large");

        _output.WriteLine($"Large file uploaded and verified. Size: {contentBytes.Length} bytes");
        _output.WriteLine($"Key: {key}");
    }

    [Fact]
    public async Task UploadFileWithSpecialCharacters_ShouldHandleCorrectly()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_factory.TestId}");
        
        using var scope = _factory.Services.CreateScope();
        var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

        var fileName = $"special-chars-{_factory.TestId}-файл-名前.csv";
        var content = "Product,Price,Description\n" +
                     "Продукт,99.99,\"Description with, commas and \"\"quotes\"\"\"\n" +
                     "製品,149.99,\"Multi-line\ndescription\"\n" +
                     "Produkt,199.99,\"Special chars: àáâãäåæçèéêë\"";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Act - Upload
        var key = await storageService.UploadFileAsync(fileName, contentBytes);

        // Act - Download
        var downloadedBytes = await storageService.DownloadFileAsync(key);
        var downloadedContent = Encoding.UTF8.GetString(downloadedBytes);

        // Assert
        downloadedContent.Should().Be(content, "special characters should be preserved");
        key.Should().Contain(_factory.TestId, "key should contain test ID");

        _output.WriteLine($"Special characters file uploaded successfully");
        _output.WriteLine($"Original size: {contentBytes.Length}, Downloaded size: {downloadedBytes.Length}");
    }
}
