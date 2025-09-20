using PriceListProcessor.Core.Models;

namespace PriceListProcessor.Core.Interfaces;

public interface IApiClient
{
    Task<ApiResponse> SendDataAsync(ApiRequest request, CancellationToken cancellationToken = default);
}
