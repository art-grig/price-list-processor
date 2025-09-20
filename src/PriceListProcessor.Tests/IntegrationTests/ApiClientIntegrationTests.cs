using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;
using PriceListProcessor.Tests.Fixtures;
using PriceListProcessor.Tests.Mocks;
using Xunit;
using Xunit.Abstractions;

namespace PriceListProcessor.Tests.IntegrationTests;

public class ApiClientIntegrationTests : IClassFixture<TestWebApplicationFactory>, IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public ApiClientIntegrationTests(TestWebApplicationFactory factory, ITestOutputHelper output)
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
    public async Task SendDataAsync_WithValidRequest_ShouldReturnSuccess()
    {
        // Setup test context
        SetupTestContext();
        
        // Arrange
        
        using var scope = _factory.Services.CreateScope();
        var apiClient = scope.ServiceProvider.GetRequiredService<IApiClient>();

        var request = new ApiRequest
        {
            FileName = $"test-file-{_factory.TestId}.csv",
            SenderEmail = "test@example.com",
            Subject = $"Test Subject [TEST:{_factory.TestId}]",
            ReceivedAt = DateTime.UtcNow,
            Data = new List<Dictionary<string, object>>
            {
                new() { ["Product"] = "Test Product", ["Price"] = 99.99m },
                new() { ["Product"] = "Another Product", ["Price"] = 149.99m }
            },
            IsLast = true
        };

        _factory.SetupMockApiClient(req => new ApiResponse
        {
            Success = true,
            Message = "Test response",
            Data = new { testId = _factory.TestId, receivedRows = req.Data.Count }
        });

        // Act
        var response = await apiClient.SendDataAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Message.Should().Be("Test response");
        
        var apiCalls = _factory.GetApiCalls();
        apiCalls.Should().HaveCount(1);
        apiCalls[0].Request.FileName.Should().Be($"test-file-{_factory.TestId}.csv");
        apiCalls[0].Request.IsLast.Should().BeTrue();

        _output.WriteLine($"API response: {response.Message}");
        _output.WriteLine($"API calls tracked: {apiCalls.Count}");
    }

    [Fact]
    public async Task SendDataAsync_WithFailureSetup_ShouldReturnFailure()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_factory.TestId}");
        
        using var scope = _factory.Services.CreateScope();
        var apiClient = scope.ServiceProvider.GetRequiredService<IApiClient>();

        var request = new ApiRequest
        {
            FileName = $"failing-file-{_factory.TestId}.csv",
            SenderEmail = "test@example.com",
            Subject = $"Failing Subject [TEST:{_factory.TestId}]",
            ReceivedAt = DateTime.UtcNow,
            Data = new List<Dictionary<string, object>>
            {
                new() { ["Product"] = "Failing Product", ["Price"] = 99.99m }
            },
            IsLast = false
        };

        _factory.SetupMockApiClient(shouldFail: true);

        // Act
        var response = await apiClient.SendDataAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.Message.Should().Contain(_factory.TestId);
        
        var apiCalls = _factory.GetApiCalls();
        apiCalls.Should().HaveCount(1);
        apiCalls[0].Request.FileName.Should().Be($"failing-file-{_factory.TestId}.csv");

        _output.WriteLine($"API response: {response.Message}");
        _output.WriteLine($"Expected failure occurred successfully");
    }

    [Fact]
    public async Task SendDataAsync_MultipleCallsInParallel_ShouldTrackAllCalls()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_factory.TestId}");
        
        using var scope = _factory.Services.CreateScope();
        var apiClient = scope.ServiceProvider.GetRequiredService<IApiClient>();

        _factory.SetupMockApiClient(req => new ApiResponse
        {
            Success = true,
            Message = $"Parallel response for {req.FileName}",
            Data = new { testId = _factory.TestId, fileName = req.FileName }
        });

        var requests = Enumerable.Range(1, 5).Select(i => new ApiRequest
        {
            FileName = $"parallel-file-{i}-{_factory.TestId}.csv",
            SenderEmail = "test@example.com",
            Subject = $"Parallel Subject {i} [TEST:{_factory.TestId}]",
            ReceivedAt = DateTime.UtcNow,
            Data = new List<Dictionary<string, object>>
            {
                new() { ["Product"] = $"Product {i}", ["Price"] = i * 10m }
            },
            IsLast = i == 5 // Only last one is marked as last
        }).ToList();

        // Act
        var tasks = requests.Select(req => apiClient.SendDataAsync(req)).ToArray();
        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().HaveCount(5);
        responses.Should().AllSatisfy(response => response.Success.Should().BeTrue());
        
        var apiCalls = _factory.GetApiCalls();
        apiCalls.Should().HaveCount(5);
        
        // Check that all file names are present
        var fileNames = apiCalls.Select(c => c.Request.FileName).ToHashSet();
        for (int i = 1; i <= 5; i++)
        {
            fileNames.Should().Contain($"parallel-file-{i}-{_factory.TestId}.csv");
        }
        
        // Check that only the last one is marked as last
        var lastCalls = apiCalls.Where(c => c.Request.IsLast).ToList();
        lastCalls.Should().HaveCount(1);
        lastCalls[0].Request.FileName.Should().Be($"parallel-file-5-{_factory.TestId}.csv");

        _output.WriteLine($"Parallel API calls completed: {apiCalls.Count}");
        _output.WriteLine($"Last batch calls: {lastCalls.Count}");
    }

    [Fact]
    public async Task SendDataAsync_WithCustomResponseProvider_ShouldUseCustomLogic()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_factory.TestId}");
        
        using var scope = _factory.Services.CreateScope();
        var apiClient = scope.ServiceProvider.GetRequiredService<IApiClient>();

        var callCount = 0;
        _factory.SetupMockApiClient(req =>
        {
            callCount++;
            return new ApiResponse
            {
                Success = true,
                Message = $"Custom response #{callCount}",
                Data = new 
                { 
                    testId = _factory.TestId,
                    callNumber = callCount,
                    fileName = req.FileName,
                    rowCount = req.Data.Count,
                    customFlag = req.IsLast ? "FINAL" : "INTERMEDIATE"
                }
            };
        });

        var requests = new[]
        {
            new ApiRequest
            {
                FileName = $"custom-1-{_factory.TestId}.csv",
                SenderEmail = "test@example.com",
                Subject = $"Custom Subject 1 [TEST:{_factory.TestId}]",
                ReceivedAt = DateTime.UtcNow,
                Data = new List<Dictionary<string, object>> { new() { ["Product"] = "Product 1", ["Price"] = 10m } },
                IsLast = false
            },
            new ApiRequest
            {
                FileName = $"custom-2-{_factory.TestId}.csv",
                SenderEmail = "test@example.com",
                Subject = $"Custom Subject 2 [TEST:{_factory.TestId}]",
                ReceivedAt = DateTime.UtcNow,
                Data = new List<Dictionary<string, object>> { new() { ["Product"] = "Product 2", ["Price"] = 20m } },
                IsLast = true
            }
        };

        // Act
        var responses = new List<ApiResponse>();
        foreach (var request in requests)
        {
            var response = await apiClient.SendDataAsync(request);
            responses.Add(response);
        }

        // Assert
        responses.Should().HaveCount(2);
        responses[0].Message.Should().Be("Custom response #1");
        responses[1].Message.Should().Be("Custom response #2");
        
        var apiCalls = _factory.GetApiCalls();
        apiCalls.Should().HaveCount(2);
        
        // Verify the custom response provider was called correctly
        callCount.Should().Be(2);

        _output.WriteLine($"Custom responses: {string.Join(", ", responses.Select(r => r.Message))}");
        _output.WriteLine($"Total calls made: {callCount}");
    }
}
