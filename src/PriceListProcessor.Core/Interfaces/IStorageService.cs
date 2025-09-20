namespace PriceListProcessor.Core.Interfaces;

public interface IStorageService
{
    Task<string> UploadFileAsync(string fileName, byte[] content, CancellationToken cancellationToken = default);
    Task<byte[]> DownloadFileAsync(string key, CancellationToken cancellationToken = default);
    Task<Stream> DownloadFileStreamAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string key, CancellationToken cancellationToken = default);
}
