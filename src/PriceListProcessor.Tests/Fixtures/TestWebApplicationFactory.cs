using Hangfire;
using Hangfire.Redis.StackExchange;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minio;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;
using PriceListProcessor.Core.Services;
using PriceListProcessor.Infrastructure.Api;
using PriceListProcessor.Infrastructure.Email;
using PriceListProcessor.Infrastructure.Storage;
using PriceListProcessor.Tests.Mocks;
using PriceListProcessor.Tests.Services;
using StackExchange.Redis;
using Xunit;

namespace PriceListProcessor.Tests.Fixtures;

public class TestWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private IConnectionMultiplexer? _redis;
    private IMinioClient? _minioClient;

    public string TestId { get; private set; } = $"default-{Guid.NewGuid().ToString("N")[..8]}";
    
    public void SetTestContext(string testMethodName)
    {
        // Generate a deterministic but unique test ID based on the test method name
        var hash = testMethodName.GetHashCode();
        var positiveHash = Math.Abs(hash);
        TestId = $"{testMethodName.ToLowerInvariant().Replace("_", "-")}-{positiveHash % 100000}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove existing services that we want to replace
            RemoveService<IEmailService>(services);
            RemoveService<IStorageService>(services);
            RemoveService<IApiClient>(services);
            RemoveService<ICsvProcessingService>(services);
            RemoveService<IConnectionMultiplexer>(services);
            RemoveService<IMinioClient>(services);
            
            // EmailPollingService no longer exists - we use Hangfire recurring jobs directly

            // Add test-specific Redis connection
            services.AddSingleton(_redis!);

            // Configure Hangfire with test-specific database
            services.AddHangfire(config =>
            {
                var redisOptions = new RedisStorageOptions
                {
                    Prefix = $"test-{TestId}:"
                };
                
                config.UseRedisStorage(_redis, redisOptions);
                config.UseSimpleAssemblyNameTypeSerializer();
                config.UseRecommendedSerializerSettings();
            });

            services.AddHangfireServer(options =>
            {
                options.Queues = new[] { $"test-{TestId}-default", $"test-{TestId}-failed" };
                options.WorkerCount = 1; // Single worker for predictable test execution
                options.ServerName = $"test-server-{TestId}";
                options.SchedulePollingInterval = TimeSpan.FromSeconds(1); // Faster polling for tests
                options.HeartbeatInterval = TimeSpan.FromSeconds(1); // Faster heartbeat for tests
            });

            // Add test-specific MinIO client
            services.AddSingleton(_minioClient!);

            // Register test-isolated services
            services.AddScoped<IEmailService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<TestIsolatedMockEmailService>>();
                return new TestIsolatedMockEmailService(logger, TestId);
            });

            services.AddScoped<IStorageService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<TestIsolatedStorageService>>();
                var config = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MinioConfiguration>>();
                return new TestIsolatedStorageService(logger, _minioClient!, config, TestId);
            });

            services.AddScoped<IApiClient, MockApiClient>();
            services.AddScoped<ICsvProcessingService, CsvProcessingService>();
            
            // Register Hangfire job classes
            services.AddScoped<PriceListProcessor.Jobs.EmailProcessingJob>();
            services.AddScoped<PriceListProcessor.Jobs.CsvProcessingJob>();
            services.AddScoped<PriceListProcessor.Jobs.CsvBatchProcessingJob>();

            // Override configuration for testing
            services.PostConfigure<EmailConfiguration>(config =>
            {
                config.Provider = "mock";
            });

            services.PostConfigure<MinioConfiguration>(config =>
            {
                config.Endpoint = "localhost:9000";
                config.AccessKey = "minioadmin";
                config.SecretKey = "minioadmin";
                config.BucketName = "test-price-lists";
                config.UseSSL = false;
            });
        });

        builder.ConfigureAppConfiguration(configBuilder =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["Email:Provider"] = "mock",
                ["Minio:Endpoint"] = "localhost:9000",
                ["Minio:AccessKey"] = "minioadmin",
                ["Minio:SecretKey"] = "minioadmin",
                ["Minio:BucketName"] = "test-price-lists",
                ["Minio:UseSSL"] = "false",
                ["EmailPolling:CronExpression"] = "*/5 * * * * *", // Run every 5 seconds in tests for true E2E
                ["Logging:LogLevel:Default"] = "Information",
                ["Hangfire:WorkerCount"] = "1",
                ["Hangfire:SchedulePollingInterval"] = "00:00:01", // 1 second for faster tests
                ["Hangfire:HeartbeatInterval"] = "00:00:01" // 1 second for faster tests
            });
        });
    }

    public async Task InitializeAsync()
    {
        // Connect to existing Redis instance from docker-compose
        _redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");

        // Create MinIO client for existing MinIO instance from docker-compose
        _minioClient = new MinioClient()
            .WithEndpoint("localhost:9000")
            .WithCredentials("minioadmin", "minioadmin")
            .WithSSL(false)
            .Build();

        // Ensure test bucket exists
        await EnsureTestBucketExistsAsync();
    }

    public new async Task DisposeAsync()
    {
        try
        {
            // Cleanup test data first
            await CleanupTestDataAsync();
        }
        catch (Exception)
        {
            // Ignore cleanup errors during disposal
        }
        finally
        {
            // Don't dispose Redis connections - they're shared across parallel tests
            // Let them clean up naturally when the process ends
            
            // Call base disposal
            await base.DisposeAsync();
        }
    }

    public async Task CleanupTestDataAsync()
    {
        try
        {
            // Clear Hangfire data
            if (_redis != null)
            {
                var database = _redis.GetDatabase();
                var server = _redis.GetServer(_redis.GetEndPoints().First());
                
                var keys = server.Keys(pattern: $"test-{TestId}:*");
                foreach (var key in keys)
                {
                    await database.KeyDeleteAsync(key);
                }
            }

            // Clear MinIO test data
            if (_minioClient != null)
            {
                var testStorageService = new TestIsolatedStorageService(
                    Services.GetRequiredService<ILogger<TestIsolatedStorageService>>(),
                    _minioClient,
                    Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MinioConfiguration>>(),
                    TestId);

                await testStorageService.CleanupTestDataAsync();
            }

            // Clear mock service data
            TestIsolatedMockEmailService.ClearTestData(TestId);
            MockApiClient.ClearTestData(TestId);
        }
        catch (Exception ex)
        {
            // Log cleanup errors but don't fail the test
            var logger = Services.GetService<ILogger<TestWebApplicationFactory>>();
            logger?.LogWarning(ex, "Error during test cleanup for test {TestId}", TestId);
        }
    }

    public void SetupMockApiClient(Func<ApiRequest, ApiResponse>? responseProvider = null, bool shouldFail = false)
    {
        MockApiClient.SetupTest(TestId, responseProvider, shouldFail);
    }

    public List<ApiCallRecord> GetApiCalls()
    {
        return MockApiClient.GetCallsForTest(TestId);
    }

    public TestIsolatedMockEmailService GetEmailService()
    {
        using var scope = Services.CreateScope();
        return (TestIsolatedMockEmailService)scope.ServiceProvider.GetRequiredService<IEmailService>();
    }

    public IBackgroundJobClient GetJobClient()
    {
        return Services.GetRequiredService<IBackgroundJobClient>();
    }

    private static void RemoveService<T>(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }
    }

    private async Task EnsureTestBucketExistsAsync()
    {
        try
        {
            var bucketName = "test-price-lists";
            var bucketExistsArgs = new Minio.DataModel.Args.BucketExistsArgs()
                .WithBucket(bucketName);

            var bucketExists = await _minioClient!.BucketExistsAsync(bucketExistsArgs);
            
            if (!bucketExists)
            {
                var makeBucketArgs = new Minio.DataModel.Args.MakeBucketArgs()
                    .WithBucket(bucketName);

                await _minioClient.MakeBucketAsync(makeBucketArgs);
            }
        }
        catch (Exception)
        {
            // Ignore bucket creation errors - might already exist
        }
    }
}