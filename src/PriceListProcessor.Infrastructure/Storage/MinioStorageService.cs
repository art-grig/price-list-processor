using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using PriceListProcessor.Core.Interfaces;

namespace PriceListProcessor.Infrastructure.Storage;

public class MinioStorageService : IStorageService
{
    private readonly ILogger<MinioStorageService> _logger;
    private readonly IMinioClient _minioClient;
    private readonly MinioConfiguration _config;

    public MinioStorageService(
        ILogger<MinioStorageService> logger, 
        IMinioClient minioClient,
        IOptions<MinioConfiguration> config)
    {
        _logger = logger;
        _minioClient = minioClient;
        _config = config.Value;
    }

    public async Task<string> UploadFileAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure bucket exists
            await EnsureBucketExistsAsync(cancellationToken);

            // Generate unique key
            var key = $"csv-files/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid()}_{fileName}";

            using var stream = new MemoryStream(content);
            
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(_config.BucketName)
                .WithObject(key)
                .WithStreamData(stream)
                .WithObjectSize(content.Length)
                .WithContentType("text/csv");

            await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
            
            _logger.LogInformation("Uploaded file {FileName} to MinIO with key {Key}", fileName, key);
            return key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {FileName} to MinIO", fileName);
            throw;
        }
    }

    public async Task<byte[]> DownloadFileAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            using var stream = new MemoryStream();
            
            var getObjectArgs = new GetObjectArgs()
                .WithBucket(_config.BucketName)
                .WithObject(key)
                .WithCallbackStream(s => s.CopyTo(stream));

            await _minioClient.GetObjectAsync(getObjectArgs, cancellationToken);
            
            _logger.LogDebug("Downloaded file with key {Key} from MinIO", key);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file with key {Key} from MinIO", key);
            throw;
        }
    }

    public async Task<Stream> DownloadFileStreamAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var stream = new MemoryStream();
            
            var getObjectArgs = new GetObjectArgs()
                .WithBucket(_config.BucketName)
                .WithObject(key)
                .WithCallbackStream(s => s.CopyTo(stream));

            await _minioClient.GetObjectAsync(getObjectArgs, cancellationToken);
            stream.Position = 0;
            
            _logger.LogDebug("Downloaded file stream with key {Key} from MinIO", key);
            return stream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file stream with key {Key} from MinIO", key);
            throw;
        }
    }

    public async Task DeleteFileAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var removeObjectArgs = new RemoveObjectArgs()
                .WithBucket(_config.BucketName)
                .WithObject(key);

            await _minioClient.RemoveObjectAsync(removeObjectArgs, cancellationToken);
            
            _logger.LogInformation("Deleted file with key {Key} from MinIO", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file with key {Key} from MinIO", key);
            throw;
        }
    }

    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var bucketExistsArgs = new BucketExistsArgs()
                .WithBucket(_config.BucketName);

            var bucketExists = await _minioClient.BucketExistsAsync(bucketExistsArgs, cancellationToken);
            
            if (!bucketExists)
            {
                var makeBucketArgs = new MakeBucketArgs()
                    .WithBucket(_config.BucketName);

                await _minioClient.MakeBucketAsync(makeBucketArgs, cancellationToken);
                _logger.LogInformation("Created MinIO bucket {BucketName}", _config.BucketName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring MinIO bucket {BucketName} exists", _config.BucketName);
            throw;
        }
    }
}

public class MinioConfiguration
{
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = "price-lists";
    public bool UseSSL { get; set; } = false;
}
