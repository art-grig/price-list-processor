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
