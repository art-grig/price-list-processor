using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace PriceListProcessor.Infrastructure.Api;

public class ApiClient : IApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ApiClient> _logger;
    private readonly ApiConfiguration _config;
    private readonly JsonSerializerOptions _jsonOptions;

    public ApiClient(HttpClient httpClient, ILogger<ApiClient> logger, IOptions<ApiConfiguration> config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config.Value;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // Configure HttpClient
        _httpClient.BaseAddress = new Uri(_config.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
        
        // Add authentication if configured
        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _config.ApiKey);
        }
        
        if (!string.IsNullOrEmpty(_config.BearerToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.BearerToken);
        }
    }

    public async Task<ApiResponse> SendDataAsync(ApiRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Sending API request for file {FileName}, batch {BatchNumber}/{TotalBatches}, isLast: {IsLast}", 
                request.FileName, 
                request.Data.Count > 0 ? "batch" : "unknown", 
                "unknown", 
                request.IsLast);

            var response = await _httpClient.PostAsJsonAsync(_config.Endpoint, request, _jsonOptions, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                
                try
                {
                    var apiResponse = JsonSerializer.Deserialize<ApiResponse>(responseContent, _jsonOptions);
                    if (apiResponse != null)
                    {
                        _logger.LogInformation("API request successful for file {FileName}", request.FileName);
                        return apiResponse;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize API response, treating as successful with raw content");
                }

                // If deserialization fails, return a success response with raw content
                return new ApiResponse
                {
                    Success = true,
                    Message = "Request processed successfully",
                    Data = responseContent
                };
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("API request failed with status {StatusCode}: {ErrorContent}", 
                    response.StatusCode, errorContent);
                
                return new ApiResponse
                {
                    Success = false,
                    Message = $"API request failed with status {response.StatusCode}: {errorContent}"
                };
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error occurred while sending API request for file {FileName}", request.FileName);
            return new ApiResponse
            {
                Success = false,
                Message = $"HTTP error: {ex.Message}"
            };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "API request timed out for file {FileName}", request.FileName);
            return new ApiResponse
            {
                Success = false,
                Message = "Request timed out"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred while sending API request for file {FileName}", request.FileName);
            return new ApiResponse
            {
                Success = false,
                Message = $"Unexpected error: {ex.Message}"
            };
        }
    }
}

public class ApiConfiguration
{
    public string BaseUrl { get; set; } = "https://api.example.com";
    public string Endpoint { get; set; } = "/api/process-data";
    public string? ApiKey { get; set; }
    public string? BearerToken { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
}
