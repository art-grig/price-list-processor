namespace PriceListProcessor.Core.Models;

public class CsvProcessingJob
{
    public string EmailId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
    public string S3Key { get; set; } = string.Empty;
}

public class CsvBatchProcessingJob
{
    public string EmailId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
    public string S3Key { get; set; } = string.Empty;
    public int BatchNumber { get; set; }
    public int TotalBatches { get; set; }
    public List<Dictionary<string, object>> Rows { get; set; } = new();
    public bool IsLast => BatchNumber == TotalBatches;
}
