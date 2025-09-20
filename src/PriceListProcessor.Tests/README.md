# Price List Processor Tests

This test project provides comprehensive testing for the Price List Processor application using WebApplicationFactory and isolated test environments.

## Test Architecture

### Test Isolation Strategy

Each test runs with a unique test identifier to ensure complete isolation:
- **Email Service**: `TestIsolatedMockEmailService` with test-specific queues
- **Storage Service**: `TestIsolatedStorageService` with test-prefixed MinIO buckets
- **API Client**: `MockApiClient` with test-specific call tracking
- **Hangfire**: Test-specific Redis databases and queue prefixes

### Test Types

#### Integration Tests
- **EmailProcessingIntegrationTests**: End-to-end email processing workflows
- **ApiClientIntegrationTests**: API client behavior with mocking
- **StorageIntegrationTests**: MinIO storage operations with test containers

#### Unit Tests
- **CsvProcessingServiceTests**: CSV validation and batch creation logic
- **MockEmailServiceTests**: Email service isolation and functionality

### Key Features

#### Parallel Test Execution
Tests can run in parallel without interference thanks to:
- Unique test identifiers for each test instance
- Isolated storage buckets with test prefixes
- Separate Redis databases for Hangfire
- Test-specific API call tracking

#### Mock Services
- **MockApiClient**: Tracks API calls by test ID, supports custom response providers
- **TestIsolatedMockEmailService**: Email service with per-test isolation
- **TestIsolatedStorageService**: MinIO storage with test-specific buckets

#### Test Containers
Uses Testcontainers for real infrastructure:
- Redis container for Hangfire storage
- MinIO container for file storage
- Automatic cleanup after tests

## Running Tests

### Prerequisites
- .NET 8 SDK
- Docker (for test containers)

### Commands

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "ClassName=EmailProcessingIntegrationTests"

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run tests in parallel
dotnet test --parallel
```

### Test Configuration

Tests use their own configuration in `appsettings.json`:
- Mock email provider by default
- Test-specific MinIO bucket names
- Disabled automatic email polling
- Appropriate logging levels

## Test Examples

### Basic Integration Test
```csharp
[Fact]
public async Task ProcessEmailWithSmallCsv_ShouldCreateSingleBatch_AndSendApiCall()
{
    // Arrange
    var emailService = _factory.GetEmailService();
    _factory.SetupMockApiClient(/* custom response */);
    
    // Act
    await emailService.SeedTestEmailsAsync(testEmails);
    await emailJob.ProcessNewEmailsAsync();
    
    // Assert
    var apiCalls = _factory.GetApiCalls();
    apiCalls.Should().HaveCount(1);
}
```

### Parallel Test Isolation
```csharp
[Fact]
public async Task ProcessMultipleEmailsInParallel_ShouldIsolateTestData()
{
    // Each test gets unique TestId automatically
    // All operations are isolated by this TestId
    // No cleanup needed between parallel tests
}
```

### Custom API Response Testing
```csharp
_factory.SetupMockApiClient(request => new ApiResponse
{
    Success = true,
    Data = new { customField = "test-specific-value" }
});
```

## Test Data Management

### Automatic Cleanup
- Test data is automatically cleaned up after each test
- MinIO buckets are cleared of test-specific files
- Redis keys with test prefixes are removed
- Mock service data is cleared

### Test Identifiers
Test identifiers are automatically generated and used for:
- Email subjects: `[TEST:abc12345]`
- File names: `filename-test-abc12345.csv`
- Storage keys: `test-abc12345/csv-files/...`
- Hangfire queues: `test-abc12345-default`
- Redis prefixes: `test-abc12345:`

### Manual Cleanup
```csharp
// In test setup/teardown
await _factory.CleanupTestDataAsync();
TestIsolatedMockEmailService.ClearTestData(testId);
MockApiClient.ClearTestData(testId);
```

## Debugging Tests

### Viewing Test Output
Tests include detailed output via `ITestOutputHelper`:
```csharp
_output.WriteLine($"Test ID: {_testId}");
_output.WriteLine($"API calls received: {apiCalls.Count}");
```

### Hangfire Dashboard
When running integration tests, you can access the Hangfire dashboard at:
`http://localhost:5000/hangfire` (when test app is running)

### MinIO Console
Access MinIO console during tests at:
`http://localhost:9001` (credentials: minioadmin/minioadmin)

## Best Practices

### Test Naming
- Use descriptive test names that explain the scenario
- Include expected outcome in the test name
- Use consistent naming patterns

### Test Structure
- Follow Arrange-Act-Assert pattern
- Use meaningful variable names
- Include test output for debugging

### Assertions
- Use FluentAssertions for readable assertions
- Assert on specific values, not just counts
- Verify both success and failure scenarios

### Resource Management
- Tests automatically handle resource cleanup
- Use `using` statements for disposable resources
- Don't manually manage test containers

## Troubleshooting

### Common Issues

1. **Docker not available**: Ensure Docker is running for test containers
2. **Port conflicts**: Test containers use random ports to avoid conflicts
3. **Test isolation failures**: Check that test IDs are being used correctly
4. **Timing issues**: Use appropriate delays for background job completion

### Debug Information
- Check test output for test IDs and operation details
- Verify test data isolation using static methods on mock services
- Use breakpoints in test code to inspect state
- Check container logs if infrastructure tests fail
