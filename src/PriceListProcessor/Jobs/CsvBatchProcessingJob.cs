using Hangfire;
using Microsoft.Extensions.Logging;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;

namespace PriceListProcessor.Jobs;

public class CsvBatchProcessingJob
{
    private readonly ILogger<CsvBatchProcessingJob> _logger;
    private readonly IApiClient _apiClient;
    private readonly IEmailService _emailService;

    public CsvBatchProcessingJob(
        ILogger<CsvBatchProcessingJob> logger,
        IApiClient apiClient,
        IEmailService emailService)
    {
        _logger = logger;
        _apiClient = apiClient;
        _emailService = emailService;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 300, 600, 900 })] // 5, 10, 15 minutes
    [DisableConcurrentExecution(300)] // Prevent concurrent execution for 5 minutes
    public async Task ProcessBatchAsync(Core.Models.CsvBatchProcessingJob batchJob)
    {
        try
        {
            _logger.LogInformation("Processing batch {BatchNumber}/{TotalBatches} for CSV file {FileName} with {RowCount} rows", 
                batchJob.BatchNumber, batchJob.TotalBatches, batchJob.FileName, batchJob.Rows.Count);

            // Create API request
            var apiRequest = new ApiRequest
            {
                FileName = batchJob.FileName,
                SenderEmail = batchJob.SenderEmail,
                Subject = batchJob.Subject,
                ReceivedAt = batchJob.ReceivedAt,
                Data = batchJob.Rows,
                IsLast = batchJob.IsLast
            };

            // Send data to API
            var response = await _apiClient.SendDataAsync(apiRequest);

            if (!response.Success)
            {
                _logger.LogError("API request failed for batch {BatchNumber}/{TotalBatches} of file {FileName}: {Message}", 
                    batchJob.BatchNumber, batchJob.TotalBatches, batchJob.FileName, response.Message);
                throw new InvalidOperationException($"API request failed: {response.Message}");
            }

            _logger.LogInformation("Successfully processed batch {BatchNumber}/{TotalBatches} for CSV file {FileName}", 
                batchJob.BatchNumber, batchJob.TotalBatches, batchJob.FileName);

            // If this is the last batch, send reply email
            if (batchJob.IsLast)
            {
                await SendReplyEmailAsync(batchJob, response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batch {BatchNumber}/{TotalBatches} for CSV file {FileName}", 
                batchJob.BatchNumber, batchJob.TotalBatches, batchJob.FileName);
            throw;
        }
    }

    private async Task SendReplyEmailAsync(Core.Models.CsvBatchProcessingJob batchJob, ApiResponse apiResponse)
    {
        try
        {
            var replyContent = CreateReplyContent(batchJob, apiResponse);
            await _emailService.SendReplyAsync(batchJob.EmailId, replyContent);
            
            _logger.LogInformation("Reply email sent for completed processing of CSV file {FileName}", batchJob.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending reply email for CSV file {FileName}", batchJob.FileName);
            // Don't throw here - the batch processing was successful, only the reply failed
        }
    }

    private static string CreateReplyContent(Core.Models.CsvBatchProcessingJob batchJob, ApiResponse apiResponse)
    {
        var content = $@"Dear Supplier,

Your price list file '{batchJob.FileName}' has been successfully processed.

Processing Details:
- File: {batchJob.FileName}
- Processed: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
- Total Batches: {batchJob.TotalBatches}
- Status: Completed

";

        if (apiResponse.Data != null)
        {
            content += $"API Response: {apiResponse.Data}\n\n";
        }

        content += @"Thank you for using Tekara's automated price list processing system.

Best regards,
Tekara Price List Processor";

        return content;
    }
}
