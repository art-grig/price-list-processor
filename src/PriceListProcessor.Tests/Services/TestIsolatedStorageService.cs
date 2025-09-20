using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Infrastructure.Storage;

namespace PriceListProcessor.Tests.Services;

public class TestIsolatedStorageService : IStorageService
{
    private readonly ILogger<TestIsolatedStorageService> _logger;
    private readonly IMinioClient _minioClient;
    private readonly MinioConfiguration _config;
    private readonly string _testId;

    public TestIsolatedStorageService(
        ILogger<TestIsolatedStorageService> logger, 
        IMinioClient minioClient,
        IOptions<MinioConfiguration> config,
        string testId)
    {
        _logger = logger;
        _minioClient = minioClient;
        _config = config.Value;
        _testId = testId;
    }

    public async Task<string> UploadFileAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure test-specific bucket exists
            var testBucketName = GetTestBucketName();
            await EnsureBucketExistsAsync(testBucketName, cancellationToken);

            // Generate unique key with test prefix
            var key = $"test-{_testId}/csv-files/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid()}_{fileName}";

            using var stream = new MemoryStream(content);
            
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(testBucketName)
                .WithObject(key)
                .WithStreamData(stream)
                .WithObjectSize(content.Length)
                .WithContentType("text/csv");

            await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
            
            _logger.LogInformation("[TEST:{TestId}] Uploaded file {FileName} to MinIO with key {Key}", _testId, fileName, key);
            return key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TEST:{TestId}] Error uploading file {FileName} to MinIO", _testId, fileName);
            throw;
        }
    }

    public async Task<byte[]> DownloadFileAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            using var stream = new MemoryStream();
            var testBucketName = GetTestBucketName();
            
            var getObjectArgs = new GetObjectArgs()
                .WithBucket(testBucketName)
                .WithObject(key)
                .WithCallbackStream(s => s.CopyTo(stream));

            await _minioClient.GetObjectAsync(getObjectArgs, cancellationToken);
            
            _logger.LogDebug("[TEST:{TestId}] Downloaded file with key {Key} from MinIO", _testId, key);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TEST:{TestId}] Error downloading file with key {Key} from MinIO", _testId, key);
            throw;
        }
    }

    public async Task<Stream> DownloadFileStreamAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var stream = new MemoryStream();
            var testBucketName = GetTestBucketName();
            
            var getObjectArgs = new GetObjectArgs()
                .WithBucket(testBucketName)
                .WithObject(key)
                .WithCallbackStream(s => s.CopyTo(stream));

            await _minioClient.GetObjectAsync(getObjectArgs, cancellationToken);
            stream.Position = 0;
            
            _logger.LogDebug("[TEST:{TestId}] Downloaded file stream with key {Key} from MinIO", _testId, key);
            return stream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TEST:{TestId}] Error downloading file stream with key {Key} from MinIO", _testId, key);
            throw;
        }
    }

    public async Task DeleteFileAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var testBucketName = GetTestBucketName();
            var removeObjectArgs = new RemoveObjectArgs()
                .WithBucket(testBucketName)
                .WithObject(key);

            await _minioClient.RemoveObjectAsync(removeObjectArgs, cancellationToken);
            
            _logger.LogInformation("[TEST:{TestId}] Deleted file with key {Key} from MinIO", _testId, key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TEST:{TestId}] Error deleting file with key {Key} from MinIO", _testId, key);
            throw;
        }
    }

    public async Task CleanupTestDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var testBucketName = GetTestBucketName();
            
            var bucketExistsArgs = new BucketExistsArgs().WithBucket(testBucketName);
            var bucketExists = await _minioClient.BucketExistsAsync(bucketExistsArgs, cancellationToken);
            
            if (bucketExists)
            {
                // List and delete all objects in the test bucket
                var listObjectsArgs = new ListObjectsArgs()
                    .WithBucket(testBucketName)
                    .WithPrefix($"test-{_testId}/")
                    .WithRecursive(true);

                await foreach (var item in _minioClient.ListObjectsEnumAsync(listObjectsArgs, cancellationToken))
                {
                    var removeObjectArgs = new RemoveObjectArgs()
                        .WithBucket(testBucketName)
                        .WithObject(item.Key);
                    
                    await _minioClient.RemoveObjectAsync(removeObjectArgs, cancellationToken);
                }
                
                _logger.LogInformation("[TEST:{TestId}] Cleaned up test data from MinIO", _testId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TEST:{TestId}] Error cleaning up test data from MinIO", _testId);
        }
    }

    private string GetTestBucketName()
    {
        return $"{_config.BucketName}-test";
    }

    private async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        try
        {
            var bucketExistsArgs = new BucketExistsArgs()
                .WithBucket(bucketName);

            var bucketExists = await _minioClient.BucketExistsAsync(bucketExistsArgs, cancellationToken);
            
            if (!bucketExists)
            {
                var makeBucketArgs = new MakeBucketArgs()
                    .WithBucket(bucketName);

                await _minioClient.MakeBucketAsync(makeBucketArgs, cancellationToken);
                _logger.LogInformation("[TEST:{TestId}] Created MinIO bucket {BucketName}", _testId, bucketName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TEST:{TestId}] Error ensuring MinIO bucket {BucketName} exists", _testId, bucketName);
            throw;
        }
    }
}
