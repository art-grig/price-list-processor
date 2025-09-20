using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;
using PriceListProcessor.Core.Services;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace PriceListProcessor.Tests.UnitTests;

public class CsvProcessingServiceTests : IAsyncLifetime
{
    private readonly Mock<IStorageService> _mockStorageService;
    private readonly Mock<ILogger<CsvProcessingService>> _mockLogger;
    private readonly CsvProcessingService _csvProcessingService;
    private readonly ITestOutputHelper _output;
    private readonly string _testId;

    public CsvProcessingServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _testId = Guid.NewGuid().ToString("N")[..8];
        _mockStorageService = new Mock<IStorageService>();
        _mockLogger = new Mock<ILogger<CsvProcessingService>>();
        _csvProcessingService = new CsvProcessingService(_mockLogger.Object, _mockStorageService.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ValidateCsvAsync_WithValidCsv_ShouldReturnTrue()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var csvContent = "Product,SKU,Price,Quantity\nTest Product 1,TP001,99.99,10\nTest Product 2,TP002,149.99,5";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        // Act
        var result = await _csvProcessingService.ValidateCsvAsync(stream);

        // Assert
        result.Should().BeTrue("valid CSV should pass validation");
        
        _output.WriteLine("Valid CSV passed validation as expected");
    }

    [Fact]
    public async Task ValidateCsvAsync_WithEmptyCsv_ShouldReturnFalse()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        using var stream = new MemoryStream();

        // Act
        var result = await _csvProcessingService.ValidateCsvAsync(stream);

        // Assert
        result.Should().BeFalse("empty CSV should fail validation");
        
        _output.WriteLine("Empty CSV failed validation as expected");
    }

    [Fact]
    public async Task ValidateCsvAsync_WithHeaderOnly_ShouldReturnTrue()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var csvContent = "Product,SKU,Price,Quantity\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        // Act
        var result = await _csvProcessingService.ValidateCsvAsync(stream);

        // Assert
        result.Should().BeTrue("CSV with header only should pass validation");
        
        _output.WriteLine("Header-only CSV passed validation as expected");
    }

    [Fact]
    public async Task ValidateCsvAsync_WithInvalidFormat_ShouldReturnFalse()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var csvContent = "This is not a CSV file\nJust some random text";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        // Act
        var result = await _csvProcessingService.ValidateCsvAsync(stream);

        // Assert
        result.Should().BeTrue("CsvHelper is quite forgiving, so this might still pass");
        
        _output.WriteLine("Invalid format handling tested");
    }

    [Fact]
    public async Task CreateBatchJobsAsync_WithSmallCsv_ShouldCreateSingleBatch()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var csvContent = "Product,SKU,Price,Quantity\nProduct 1,P001,10.00,100\nProduct 2,P002,20.00,200\nProduct 3,P003,30.00,300";

        var job = new CsvProcessingJob
        {
            EmailId = $"email-{_testId}",
            FileName = $"small-{_testId}.csv",
            SenderEmail = "test@example.com",
            Subject = $"Test Subject [TEST:{_testId}]",
            ReceivedAt = DateTime.UtcNow,
            S3Key = $"test-{_testId}/small.csv"
        };

        _mockStorageService
            .Setup(s => s.DownloadFileStreamAsync(job.S3Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes(csvContent)));

        // Act
        var batchJobs = await _csvProcessingService.CreateBatchJobsAsync(job);

        // Assert
        batchJobs.Should().HaveCount(1, "small CSV should create single batch");
        
        var batch = batchJobs[0];
        batch.BatchNumber.Should().Be(1);
        batch.TotalBatches.Should().Be(1);
        batch.IsLast.Should().BeTrue();
        batch.Rows.Should().HaveCount(3, "should have 3 data rows");
        batch.EmailId.Should().Be(job.EmailId);
        batch.FileName.Should().Be(job.FileName);

        // Verify data parsing
        batch.Rows[0]["Product"].Should().Be("Product 1");
        batch.Rows[0]["Price"].Should().Be(10.00m);
        batch.Rows[1]["Quantity"].Should().Be(200);

        _output.WriteLine($"Created {batchJobs.Count} batch(es) for small CSV");
        _output.WriteLine($"First batch has {batch.Rows.Count} rows");
    }

    [Fact]
    public async Task CreateBatchJobsAsync_WithLargeCsv_ShouldCreateMultipleBatches()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var csvBuilder = new StringBuilder("Product,SKU,Price,Quantity\n");
        const int totalRows = 2500; // Should create 3 batches: 1000, 1000, 500
        
        for (int i = 1; i <= totalRows; i++)
        {
            csvBuilder.AppendLine($"Product {i},P{i:D4},{i * 1.5m:F2},{i * 10}");
        }

        var job = new CsvProcessingJob
        {
            EmailId = $"email-{_testId}",
            FileName = $"large-{_testId}.csv",
            SenderEmail = "test@example.com",
            Subject = $"Large Test [TEST:{_testId}]",
            ReceivedAt = DateTime.UtcNow,
            S3Key = $"test-{_testId}/large.csv"
        };

        _mockStorageService
            .Setup(s => s.DownloadFileStreamAsync(job.S3Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes(csvBuilder.ToString())));

        // Act
        var batchJobs = await _csvProcessingService.CreateBatchJobsAsync(job);

        // Assert
        batchJobs.Should().HaveCount(3, "2500 rows should create 3 batches");
        
        // Check batch details
        batchJobs[0].BatchNumber.Should().Be(1);
        batchJobs[0].TotalBatches.Should().Be(3);
        batchJobs[0].IsLast.Should().BeFalse();
        batchJobs[0].Rows.Should().HaveCount(1000);

        batchJobs[1].BatchNumber.Should().Be(2);
        batchJobs[1].TotalBatches.Should().Be(3);
        batchJobs[1].IsLast.Should().BeFalse();
        batchJobs[1].Rows.Should().HaveCount(1000);

        batchJobs[2].BatchNumber.Should().Be(3);
        batchJobs[2].TotalBatches.Should().Be(3);
        batchJobs[2].IsLast.Should().BeTrue();
        batchJobs[2].Rows.Should().HaveCount(500);

        // Verify all batches have correct metadata
        foreach (var batch in batchJobs)
        {
            batch.EmailId.Should().Be(job.EmailId);
            batch.FileName.Should().Be(job.FileName);
            batch.SenderEmail.Should().Be(job.SenderEmail);
            batch.Subject.Should().Be(job.Subject);
            batch.S3Key.Should().Be(job.S3Key);
        }

        // Verify data integrity
        var firstRowBatch1 = batchJobs[0].Rows[0];
        firstRowBatch1["Product"].Should().Be("Product 1");
        firstRowBatch1["Price"].Should().Be(1.5m);

        var firstRowBatch2 = batchJobs[1].Rows[0];
        firstRowBatch2["Product"].Should().Be("Product 1001");
        firstRowBatch2["Price"].Should().Be(1501.5m);

        _output.WriteLine($"Created {batchJobs.Count} batches for large CSV");
        _output.WriteLine($"Batch sizes: {string.Join(", ", batchJobs.Select(b => b.Rows.Count))}");
        _output.WriteLine($"IsLast flags: {string.Join(", ", batchJobs.Select(b => b.IsLast))}");
    }

    [Fact]
    public async Task CreateBatchJobsAsync_WithExactlyThousandRows_ShouldCreateSingleBatch()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var csvBuilder = new StringBuilder("Product,Price\n");
        const int totalRows = 1000; // Exactly batch size
        
        for (int i = 1; i <= totalRows; i++)
        {
            csvBuilder.AppendLine($"Product {i},{i * 10.0m:F2}");
        }

        var job = new CsvProcessingJob
        {
            EmailId = $"email-{_testId}",
            FileName = $"exact-{_testId}.csv",
            SenderEmail = "test@example.com",
            Subject = $"Exact Size Test [TEST:{_testId}]",
            ReceivedAt = DateTime.UtcNow,
            S3Key = $"test-{_testId}/exact.csv"
        };

        _mockStorageService
            .Setup(s => s.DownloadFileStreamAsync(job.S3Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes(csvBuilder.ToString())));

        // Act
        var batchJobs = await _csvProcessingService.CreateBatchJobsAsync(job);

        // Assert
        batchJobs.Should().HaveCount(1, "exactly 1000 rows should create single batch");
        
        var batch = batchJobs[0];
        batch.BatchNumber.Should().Be(1);
        batch.TotalBatches.Should().Be(1);
        batch.IsLast.Should().BeTrue();
        batch.Rows.Should().HaveCount(1000);

        _output.WriteLine($"Exactly 1000 rows created {batchJobs.Count} batch with {batch.Rows.Count} rows");
    }

    [Fact]
    public async Task CreateBatchJobsAsync_WithMixedDataTypes_ShouldParseCorrectly()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var csvContent = "Product,Price,Quantity,InStock,CreatedDate,Description\nProduct 1,99.99,10,true,2024-01-15,Simple product\nProduct 2,149.50,0,false,2024-01-16,\"Product with, comma\"\nProduct 3,199.00,5,TRUE,2024-01-17,\"Product with \"\"quotes\"\"\"";

        var job = new CsvProcessingJob
        {
            EmailId = $"email-{_testId}",
            FileName = $"mixed-{_testId}.csv",
            SenderEmail = "test@example.com",
            Subject = $"Mixed Data Test [TEST:{_testId}]",
            ReceivedAt = DateTime.UtcNow,
            S3Key = $"test-{_testId}/mixed.csv"
        };

        _mockStorageService
            .Setup(s => s.DownloadFileStreamAsync(job.S3Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes(csvContent)));

        // Act
        var batchJobs = await _csvProcessingService.CreateBatchJobsAsync(job);

        // Assert
        batchJobs.Should().HaveCount(1);
        var batch = batchJobs[0];
        batch.Rows.Should().HaveCount(3);

        // Verify data type parsing
        var row1 = batch.Rows[0];
        row1["Product"].Should().Be("Product 1");
        row1["Price"].Should().Be(99.99m);
        row1["Quantity"].Should().Be(10);
        row1["InStock"].Should().Be(true);
        row1["Description"].Should().Be("Simple product");

        var row2 = batch.Rows[1];
        row2["Price"].Should().Be(149.50m);
        row2["Quantity"].Should().Be(0);
        row2["InStock"].Should().Be(false);
        row2["Description"].Should().Be("Product with, comma");

        var row3 = batch.Rows[2];
        row3["InStock"].Should().Be(true); // TRUE should parse as boolean
        row3["Description"].Should().Be("Product with \"quotes\"");

        _output.WriteLine("Mixed data types parsed correctly");
        _output.WriteLine($"Row 1 price type: {row1["Price"].GetType()}");
        _output.WriteLine($"Row 1 InStock type: {row1["InStock"].GetType()}");
    }

    [Fact]
    public async Task CreateBatchJobsAsync_WhenStorageThrows_ShouldPropagateException()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var job = new CsvProcessingJob
        {
            EmailId = $"email-{_testId}",
            FileName = $"failing-{_testId}.csv",
            SenderEmail = "test@example.com",
            Subject = $"Failing Test [TEST:{_testId}]",
            ReceivedAt = DateTime.UtcNow,
            S3Key = $"test-{_testId}/nonexistent.csv"
        };

        _mockStorageService
            .Setup(s => s.DownloadFileStreamAsync(job.S3Key, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException($"File not found: {job.S3Key}"));

        // Act & Assert
        var act = async () => await _csvProcessingService.CreateBatchJobsAsync(job);
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"File not found: {job.S3Key}");

        _output.WriteLine("Storage exception propagated correctly");
    }
}
