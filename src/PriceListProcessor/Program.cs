using Hangfire;
using Hangfire.Dashboard;
using Hangfire.Redis.StackExchange;
using Minio;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Services;
using PriceListProcessor.Infrastructure.Api;
using PriceListProcessor.Infrastructure.Email;
using PriceListProcessor.Infrastructure.Storage;
using PriceListProcessor.Middleware;
using PriceListProcessor.Jobs;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/price-list-processor-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configuration
builder.Services.Configure<EmailConfiguration>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<MinioConfiguration>(builder.Configuration.GetSection("Minio"));
builder.Services.Configure<ApiConfiguration>(builder.Configuration.GetSection("Api"));
builder.Services.Configure<EmailPollingConfiguration>(builder.Configuration.GetSection("EmailPolling"));

// Redis connection for Hangfire
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
var redis = ConnectionMultiplexer.Connect(redisConnectionString);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// Hangfire
builder.Services.AddHangfire(config =>
{
    config.UseRedisStorage(redis);
    config.UseSimpleAssemblyNameTypeSerializer();
    config.UseRecommendedSerializerSettings();
});
builder.Services.AddHangfireServer(options =>
{
    options.Queues = new[] { "default", "failed" };
    options.WorkerCount = Environment.ProcessorCount;
});

// MinIO
builder.Services.AddSingleton<IMinioClient>(provider =>
{
    var config = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MinioConfiguration>>().Value;
    return new MinioClient()
        .WithEndpoint(config.Endpoint)
        .WithCredentials(config.AccessKey, config.SecretKey)
        .WithSSL(config.UseSSL)
        .Build();
});

// HTTP Client for API
builder.Services.AddHttpClient<IApiClient, ApiClient>();

// Services
builder.Services.AddScoped<IStorageService, MinioStorageService>();
builder.Services.AddScoped<ICsvProcessingService, CsvProcessingService>();

// Email Service - Configure based on configuration
builder.Services.AddScoped<IEmailService>(provider =>
{
    var emailConfig = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailConfiguration>>().Value;
    var logger = provider.GetRequiredService<ILoggerFactory>();
    
    return emailConfig.Provider.ToLowerInvariant() switch
    {
        "pop3" => new Pop3EmailService(logger.CreateLogger<Pop3EmailService>(), 
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailConfiguration>>()),
        "imap" => new ImapEmailService(logger.CreateLogger<ImapEmailService>(), 
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailConfiguration>>()),
        "mock" => new MockEmailService(logger.CreateLogger<MockEmailService>()),
        _ => throw new InvalidOperationException($"Unknown email provider: {emailConfig.Provider}")
    };
});

// Remove EmailPollingService - we'll use Hangfire recurring job directly

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Hangfire Dashboard
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireDashboardAuthorizationFilter() }
});

// Setup recurring email processing job
var emailPollingConfig = app.Configuration.GetSection("EmailPolling").Get<EmailPollingConfiguration>() ?? new EmailPollingConfiguration();
RecurringJob.AddOrUpdate<PriceListProcessor.Jobs.EmailProcessingJob>(
    "email-processing",
    job => job.ProcessNewEmailsAsync(),
    emailPollingConfig.CronExpression);

app.Logger.LogInformation("Recurring email processing job configured with cron expression: {CronExpression}", emailPollingConfig.CronExpression);

// Seed test data if using mock email service
if (app.Configuration["Email:Provider"]?.ToLowerInvariant() == "mock")
{
    using var scope = app.Services.CreateScope();
    var mockEmailService = scope.ServiceProvider.GetRequiredService<IEmailService>() as IMockEmailService;
    if (mockEmailService != null)
    {
        await SeedTestEmailsAsync(mockEmailService);
    }
}

app.Run();

static async Task SeedTestEmailsAsync(IMockEmailService mockEmailService)
{
    var testEmails = new List<PriceListProcessor.Core.Models.EmailMessage>
    {
        new()
        {
            Id = "test-email-1",
            From = "supplier1@example.com",
            Subject = "Price List Update - Electronics",
            ReceivedAt = DateTime.UtcNow.AddMinutes(-10),
            Attachments = new List<PriceListProcessor.Core.Models.EmailAttachment>
            {
                new()
                {
                    FileName = "electronics-prices.csv",
                    ContentType = "text/csv",
                    Content = System.Text.Encoding.UTF8.GetBytes("""
                        Product,SKU,Price,Quantity
                        iPhone 15,IPH15,999.99,50
                        Samsung Galaxy S24,SGS24,899.99,30
                        MacBook Pro,MBP16,2499.99,10
                        Dell XPS 13,DXPS13,1299.99,25
                        """),
                    Size = 150
                }
            }
        },
        new()
        {
            Id = "test-email-2",
            From = "supplier2@example.com",
            Subject = "Monthly Price Update - Clothing",
            ReceivedAt = DateTime.UtcNow.AddMinutes(-5),
            Attachments = new List<PriceListProcessor.Core.Models.EmailAttachment>
            {
                new()
                {
                    FileName = "clothing-prices.csv",
                    ContentType = "text/csv",
                    Content = System.Text.Encoding.UTF8.GetBytes(GenerateLargeCsvContent()),
                    Size = 50000
                }
            }
        }
    };

    await mockEmailService.SeedTestEmailsAsync(testEmails);
    Log.Information("Seeded {Count} test emails", testEmails.Count);
}

static string GenerateLargeCsvContent()
{
    var csv = "Product,SKU,Price,Quantity,Category\n";
    var categories = new[] { "Shirts", "Pants", "Dresses", "Shoes", "Accessories" };
    var random = new Random();
    
    for (int i = 1; i <= 2500; i++) // Generate 2500 rows to test batching
    {
        var category = categories[random.Next(categories.Length)];
        var price = Math.Round(random.NextDouble() * 200 + 10, 2);
        var quantity = random.Next(1, 100);
        
        csv += $"Product-{i},{category}-{i:D4},{price},{quantity},{category}\n";
    }
    
    return csv;
}

// Simple authorization filter for Hangfire dashboard
public class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true; // In production, implement proper authorization
}

// Email polling configuration
public class EmailPollingConfiguration
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(5);
    public string CronExpression { get; set; } = "*/5 * * * *"; // Every 5 minutes
}

// Make Program class accessible for testing
public partial class Program { }
