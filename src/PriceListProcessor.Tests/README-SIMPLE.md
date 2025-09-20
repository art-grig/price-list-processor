# Running Tests with Docker Compose

This test suite uses the existing Docker Compose infrastructure instead of test containers.

## Prerequisites

1. Docker and Docker Compose installed
2. .NET 8 SDK

## Running Tests

### 1. Start Infrastructure
```bash
# Start Redis and MinIO
docker-compose up -d
```

### 2. Run Tests
```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "ClassName=CsvProcessingServiceTests"

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"
```

### 3. Monitor Test Execution
- **Redis**: localhost:6379 (for Hangfire job storage)
- **MinIO**: localhost:9000 (for file storage)
- **MinIO Console**: localhost:9001 (admin/minioadmin)

## Test Isolation

Each test gets a unique Test ID (e.g., `abc12345`) and uses:
- **Hangfire**: Test-prefixed Redis keys (`test-abc12345:*`)
- **MinIO**: Test-prefixed storage paths (`test-abc12345/...`)
- **Email Service**: Test-isolated mock queues
- **API Client**: Test-specific call tracking

## Test Categories

### Unit Tests
- `CsvProcessingServiceTests` - CSV validation and processing logic
- `MockEmailServiceTests` - Email service isolation verification

### Integration Tests  
- `EmailProcessingIntegrationTests` - End-to-end email processing
- `ApiClientIntegrationTests` - API client behavior
- `StorageIntegrationTests` - MinIO storage operations

## Cleanup

Tests automatically clean up their data, but you can manually clean:

```bash
# Stop infrastructure
docker-compose down

# Clean volumes (removes all data)
docker-compose down -v
```

## Troubleshooting

### Redis Connection Issues
```bash
# Check if Redis is running
docker-compose ps
docker logs price-list-redis
```

### MinIO Connection Issues  
```bash
# Check if MinIO is running
docker-compose ps
docker logs price-list-minio

# Access MinIO console
open http://localhost:9001
# Login: minioadmin/minioadmin
```

### Test Failures
- Check that Docker Compose services are running
- Verify no port conflicts (6379, 9000, 9001)
- Check logs for connection errors
