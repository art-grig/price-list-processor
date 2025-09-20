using Hangfire;
using Microsoft.Extensions.Logging;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;
using PriceListProcessor.Core.Services;

namespace PriceListProcessor.Jobs;

public class CsvProcessingJob
{
    private readonly ILogger<CsvProcessingJob> _logger;
    private readonly ICsvProcessingService _csvProcessingService;
    private readonly IStorageService _storageService;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public CsvProcessingJob(
        ILogger<CsvProcessingJob> logger,
        ICsvProcessingService csvProcessingService,
        IStorageService storageService,
        IBackgroundJobClient backgroundJobClient)
    {
        _logger = logger;
        _csvProcessingService = csvProcessingService;
        _storageService = storageService;
        _backgroundJobClient = backgroundJobClient;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 300, 600, 900 })] // 5, 10, 15 minutes
    [DisableConcurrentExecution(600)] // Prevent concurrent execution for 10 minutes
    public async Task ProcessCsvFileAsync(Core.Models.CsvProcessingJob job)
    {
        try
        {
            _logger.LogInformation("Processing CSV file {FileName} from email {EmailId}", job.FileName, job.EmailId);

            // Download and validate CSV file
            using var csvStream = await _storageService.DownloadFileStreamAsync(job.S3Key);
            
            var isValid = await _csvProcessingService.ValidateCsvAsync(csvStream);
            if (!isValid)
            {
                _logger.LogError("CSV file {FileName} failed validation", job.FileName);
                throw new InvalidOperationException($"CSV file {job.FileName} is not valid");
            }

            // Create batch jobs
            var batchJobs = await _csvProcessingService.CreateBatchJobsAsync(job);
            
            if (batchJobs.Count == 0)
            {
                _logger.LogWarning("No batch jobs created for CSV file {FileName}", job.FileName);
                return;
            }

            // Enqueue batch processing jobs sequentially
            string? previousJobId = null;
            
            for (int i = 0; i < batchJobs.Count; i++)
            {
                var batchJob = batchJobs[i];
                
                string jobId;
                if (previousJobId == null)
                {
                    // First job - enqueue immediately
                    jobId = _backgroundJobClient.Enqueue<CsvBatchProcessingJob>(
                        job => job.ProcessBatchAsync(batchJob));
                }
                else
                {
                    // Subsequent jobs - enqueue after previous job completes
                    jobId = _backgroundJobClient.ContinueJobWith<CsvBatchProcessingJob>(
                        previousJobId, 
                        job => job.ProcessBatchAsync(batchJob));
                }
                
                previousJobId = jobId;
                
                _logger.LogDebug("Enqueued batch job {BatchNumber}/{TotalBatches} for CSV file {FileName} with job ID {JobId}", 
                    batchJob.BatchNumber, batchJob.TotalBatches, batchJob.FileName, jobId);
            }

            _logger.LogInformation("Successfully created {BatchCount} sequential batch jobs for CSV file {FileName}", 
                batchJobs.Count, job.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing CSV file {FileName} from email {EmailId}", job.FileName, job.EmailId);
            throw;
        }
    }
}
