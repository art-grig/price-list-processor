using System.Text.Json.Serialization;

namespace PriceListProcessor.Core.Models;

public class ApiRequest
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;
    
    [JsonPropertyName("senderEmail")]
    public string SenderEmail { get; set; } = string.Empty;
    
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;
    
    [JsonPropertyName("receivedAt")]
    public DateTime ReceivedAt { get; set; }
    
    [JsonPropertyName("data")]
    public List<Dictionary<string, object>> Data { get; set; } = new();
    
    [JsonPropertyName("isLast")]
    public bool IsLast { get; set; }
}

public class ApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("data")]
    public object? Data { get; set; }
}
