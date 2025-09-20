# Price List Processor

A .NET 8 application that automatically processes CSV price lists received via email, using Hangfire for job management and MinIO for file storage.

## Features

- **Email Processing**: Supports POP3, IMAP, and Mock email providers
- **CSV Processing**: Validates and processes CSV files in batches of 1000 rows
- **Sequential Processing**: Ensures batches are processed in order with the last batch marked appropriately
- **Storage**: Uses MinIO (S3-compatible) for CSV file storage
- **Job Queue**: Hangfire with Redis for reliable job processing
- **Retry Logic**: Automatic retry with exponential backoff for failed jobs
- **API Integration**: Sends processed data to external API endpoints
- **Email Replies**: Sends processing results back to original sender

## Architecture

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   Email Source  │───▶│  Email Service   │───▶│  Storage (MinIO)│
│  (POP3/IMAP)    │    │  (Poll/Process)  │    │                 │
└─────────────────┘    └──────────────────┘    └─────────────────┘
                                │
                                ▼
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   Hangfire      │◀───│  CSV Processing  │───▶│  External API   │
│   Job Queue     │    │     Service      │    │                 │
└─────────────────┘    └──────────────────┘    └─────────────────┘
                                │
                                ▼
                       ┌──────────────────┐
                       │  Email Reply     │
                       │    Service       │
                       └──────────────────┘
```

## Getting Started

### Prerequisites

- .NET 8 SDK
- Docker and Docker Compose

### Quick Start

1. **Clone and build the project:**
   ```bash
   git clone <repository-url>
   cd price-list-processor
   dotnet restore
   dotnet build
   ```

2. **Start infrastructure services:**
   ```bash
   docker-compose up -d
   ```

   This starts:
   - Redis (port 6379) - for Hangfire job storage
   - MinIO (port 9000) - for CSV file storage
   - MinIO Console (port 9001) - for MinIO management

3. **Run the application:**
   ```bash
   cd src/PriceListProcessor
   dotnet run
   ```

4. **Access the application:**
   - API: http://localhost:5000
   - Swagger UI: http://localhost:5000/swagger
   - Hangfire Dashboard: http://localhost:5000/hangfire
   - MinIO Console: http://localhost:9001 (admin/minioadmin)

### Configuration

The application uses `appsettings.json` for configuration:

```json
{
  "Email": {
    "Provider": "mock", // "pop3", "imap", or "mock"
    "Pop3Host": "mail.example.com",
    "Pop3Port": 995,
    "ImapHost": "imap.example.com", 
    "ImapPort": 993,
    "SmtpHost": "smtp.example.com",
    "SmtpPort": 587,
    "Username": "your-email@example.com",
    "Password": "your-password",
    "UseSsl": true
  },
  "Minio": {
    "Endpoint": "localhost:9000",
    "AccessKey": "minioadmin",
    "SecretKey": "minioadmin",
    "BucketName": "price-lists",
    "UseSSL": false
  },
  "Api": {
    "BaseUrl": "https://your-api.com",
    "Endpoint": "/api/process-data",
    "ApiKey": "your-api-key",
    "TimeoutSeconds": 30
  }
}
```

## Unit Testing

This project includes comprehensive unit and integration tests to ensure code quality and reliability.

### Test Structure

The test suite is organized into several categories:

- **Unit Tests**: Fast, isolated tests for individual components
- **Integration Tests**: Tests that verify interactions between components
- **End-to-End Tests**: Full workflow tests that verify the complete system

### Running Tests Locally

#### Prerequisites

1. **Start test infrastructure:**
   ```bash
   # Start Redis and MinIO for integration tests
   docker-compose -f docker-compose.tests.yml up -d
   ```

2. **Verify services are running:**
   ```bash
   # Check Redis
   docker exec $(docker-compose -f docker-compose.tests.yml ps -q redis) redis-cli ping
   
   # Check MinIO
   curl http://localhost:9000/minio/health/live
   ```

#### Running All Tests

```bash
# Run all tests (27 tests total)
dotnet test

# Run with detailed output
dotnet test --verbosity normal

# Run with code coverage
dotnet test --collect:"XPlat Code Coverage"
```

#### Running Specific Test Categories

```bash
# Run only unit tests (16 tests)
dotnet test --filter "FullyQualifiedName~CsvProcessingServiceTests|FullyQualifiedName~MockEmailServiceTests"

# Run only integration tests (10 tests)
dotnet test --filter "FullyQualifiedName~ApiClientIntegrationTests|FullyQualifiedName~StorageIntegrationTests"

# Run only E2E test (1 test)
dotnet test --filter "FullyQualifiedName~EndToEndEmailProcessingTests"

# Run all tests except E2E (26 tests)
dotnet test --filter "FullyQualifiedName!~EndToEndEmailProcessingTests"
```

#### Running Individual Test Classes

```bash
# CSV Processing Service tests
dotnet test --filter "FullyQualifiedName~CsvProcessingServiceTests"

# Mock Email Service tests
dotnet test --filter "FullyQualifiedName~MockEmailServiceTests"

# API Client Integration tests
dotnet test --filter "FullyQualifiedName~ApiClientIntegrationTests"

# Storage Integration tests
dotnet test --filter "FullyQualifiedName~StorageIntegrationTests"

# End-to-End Email Processing test
dotnet test --filter "FullyQualifiedName~EndToEndEmailProcessingTests"
```

#### Test Configuration

Tests use isolated configurations to prevent interference:

- **Redis**: Each test uses a unique database prefix
- **MinIO**: Each test uses isolated bucket paths
- **Mock Services**: Test-specific instances with isolated data

### Test Categories

#### Unit Tests (16 tests)

**CsvProcessingServiceTests** - Tests CSV validation and batch creation:
- CSV format validation
- Batch size calculations
- Data type parsing
- Error handling

**MockEmailServiceTests** - Tests mock email service functionality:
- Email seeding and retrieval
- Processing state management
- Test isolation
- Reply email generation

#### Integration Tests (10 tests)

**ApiClientIntegrationTests** - Tests API client with mock responses:
- Successful API calls
- Error handling and retries
- Parallel request handling
- Custom response logic

**StorageIntegrationTests** - Tests MinIO storage operations:
- File upload and download
- Stream handling
- File deletion
- Special character handling
- Large file processing

#### End-to-End Tests (1 test)

**EndToEndEmailProcessingTests** - Tests complete workflow:
- Email processing job execution
- CSV file processing pipeline
- API data transmission
- Email reply generation

### Test Data Isolation

Each test runs in isolation using unique identifiers:

- **Test IDs**: Generated from test method names
- **Redis Keys**: Prefixed with test-specific identifiers
- **MinIO Buckets**: Isolated per test execution
- **Mock Data**: Clean state for each test

### Debugging Tests

#### Running Tests with Debug Output

```bash
# Run with detailed logging
dotnet test --logger "console;verbosity=detailed"

# Run specific test with debug output
dotnet test --filter "TestMethodName" --logger "console;verbosity=detailed"
```

#### Viewing Test Results

```bash
# Generate test results file
dotnet test --logger "trx;LogFileName=test-results.trx"

# View results in Visual Studio or VS Code
```

### Continuous Integration

Tests are automatically run in GitHub Actions with:

- **Unit Tests**: Fast execution without external dependencies
- **Integration Tests**: Full infrastructure setup with Redis and MinIO
- **E2E Tests**: Complete workflow verification
- **Artifact Collection**: Test results and coverage reports

### Known Issues

#### E2E Test Failures When Running All Tests

**Issue**: The End-to-End test (`EndToEndEmailProcessingTests`) may fail when running the complete test suite (`dotnet test`) due to resource contention.

**Root Cause**: 
- Multiple Hangfire servers running simultaneously during parallel test execution
- Shared Redis and MinIO instances being accessed by multiple test classes
- Background job processing conflicts between different test contexts

**Workarounds**:

1. **Run E2E test separately** (Recommended):
   ```bash
   # Run all tests except E2E first
   dotnet test --filter "FullyQualifiedName!~EndToEndEmailProcessingTests"
   
   # Then run E2E test separately
   dotnet test --filter "FullyQualifiedName~EndToEndEmailProcessingTests"
   ```

2. **Run tests sequentially**:
   ```bash
   # Disable parallel execution
   dotnet test --logger "console;verbosity=normal" -- --no-build
   ```

3. **Use GitHub Actions workflow** (Production approach):
   - The CI/CD pipeline runs tests in separate jobs to avoid conflicts
   - Unit/Integration tests run first, then E2E tests in isolation

**Why This Happens**:
- E2E tests require exclusive access to Hangfire job processing
- Other integration tests may interfere with background job execution
- Test isolation mechanisms work well for individual test classes but can conflict during full suite execution

**Future Improvements**:
- Consider implementing test ordering or dependency management
- Explore containerized test isolation per test class
- Investigate Hangfire test server isolation improvements

## Testing

### Using Mock Email Service

The application includes a mock email service for testing:

1. **Seed test emails:**
   ```bash
   curl -X POST http://localhost:5000/api/test/seed-test-emails
   ```

2. **Trigger email processing:**
   ```bash
   curl -X POST http://localhost:5000/api/test/trigger-email-processing
   ```

3. **Monitor progress:**
   - Visit http://localhost:5000/hangfire to see job progress
   - Check logs for detailed processing information

### Email Providers

#### POP3 Configuration
```json
{
  "Email": {
    "Provider": "pop3",
    "Pop3Host": "pop.gmail.com",
    "Pop3Port": 995,
    "Username": "your-email@gmail.com",
    "Password": "your-app-password",
    "UseSsl": true
  }
}
```

#### IMAP Configuration
```json
{
  "Email": {
    "Provider": "imap",
    "ImapHost": "imap.gmail.com",
    "ImapPort": 993,
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": 587,
    "Username": "your-email@gmail.com",
    "Password": "your-app-password",
    "UseSsl": true
  }
}
```

## Processing Flow

1. **Email Polling**: Background service polls for new emails every 5 minutes
2. **CSV Detection**: Identifies emails with CSV attachments
3. **File Upload**: Uploads CSV files to MinIO storage
4. **CSV Validation**: Validates CSV format and structure
5. **Batch Creation**: Splits CSV into batches of 1000 rows
6. **Sequential Processing**: Processes batches in order using Hangfire
7. **API Calls**: Sends each batch to external API
8. **Reply Email**: Sends processing results to original sender

## Error Handling

- **Automatic Retry**: Failed jobs retry 3 times with 5, 10, 15 minute delays
- **Failed Queue**: Permanently failed jobs move to "failed" queue
- **Logging**: Comprehensive logging with Serilog
- **Sequential Guarantee**: Last batch only processes after all previous batches succeed

## API Endpoints

### Test Endpoints
- `POST /api/test/seed-test-emails` - Add test emails to mock service
- `POST /api/test/trigger-email-processing` - Manually trigger email processing
- `GET /api/test/health` - Health check
- `GET /api/test/email-service-type` - Get current email service type

### Monitoring
- `/hangfire` - Hangfire dashboard for job monitoring

## Development

### Project Structure
```
src/
├── PriceListProcessor/          # Main web application
│   ├── Controllers/             # API controllers
│   ├── Jobs/                    # Hangfire job definitions
│   └── Services/                # Background services
├── PriceListProcessor.Core/     # Domain models and interfaces
│   ├── Interfaces/              # Service contracts
│   ├── Models/                  # Domain models
│   └── Services/                # Core business logic
└── PriceListProcessor.Infrastructure/ # External service implementations
    ├── Api/                     # API client
    ├── Email/                   # Email service implementations
    └── Storage/                 # MinIO storage service
```

### Adding New Email Providers

1. Implement `IEmailService` interface
2. Register in `Program.cs`
3. Add configuration section
4. Update provider selection logic

### Customizing CSV Processing

Modify `CsvProcessingService` to:
- Change batch size (default: 1000 rows)
- Add custom validation rules
- Transform data before API calls

## Troubleshooting

### Common Issues

1. **Redis Connection Failed**
   ```bash
   docker-compose up redis -d
   ```

2. **MinIO Access Denied**
   - Check credentials in appsettings.json
   - Verify bucket exists and permissions

3. **Email Authentication Failed**
   - Use app-specific passwords for Gmail
   - Check firewall/antivirus blocking IMAP/POP3

4. **Jobs Not Processing**
   - Check Hangfire dashboard for errors
   - Verify Redis connection
   - Check application logs

### Logs Location
- Console output during development
- File logs: `logs/price-list-processor-YYYYMMDD.txt`

## Production Considerations

1. **Security**: 
   - Use secure credential storage (Azure Key Vault, etc.)
   - Enable Hangfire dashboard authentication
   - Use HTTPS for all communications

2. **Scalability**:
   - Configure Hangfire worker count based on load
   - Consider Redis clustering for high availability
   - Monitor MinIO storage usage

3. **Monitoring**:
   - Set up application insights or similar
   - Configure alerts for failed jobs
   - Monitor email processing metrics

## License

This project is developed as a technical assessment for Tekara and demonstrates enterprise-level .NET development patterns.
