using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;
using System.Globalization;
using System.Text;

namespace PriceListProcessor.Core.Services;

public interface ICsvProcessingService
{
    Task<bool> ValidateCsvAsync(Stream csvStream, CancellationToken cancellationToken = default);
    Task<List<CsvBatchProcessingJob>> CreateBatchJobsAsync(CsvProcessingJob job, CancellationToken cancellationToken = default);
}

public class CsvProcessingService : ICsvProcessingService
{
    private readonly ILogger<CsvProcessingService> _logger;
    private readonly IStorageService _storageService;
    private const int BatchSize = 1000;

    public CsvProcessingService(ILogger<CsvProcessingService> logger, IStorageService storageService)
    {
        _logger = logger;
        _storageService = storageService;
    }

    public async Task<bool> ValidateCsvAsync(Stream csvStream, CancellationToken cancellationToken = default)
    {
        try
        {
            csvStream.Position = 0;
            using var reader = new StreamReader(csvStream, Encoding.UTF8, leaveOpen: true);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null, // Don't throw on missing fields
                BadDataFound = null, // Don't throw on bad data
                HeaderValidated = null, // Don't validate headers
                IgnoreBlankLines = true
            });

            // Try to read the header
            if (!await csv.ReadAsync())
            {
                _logger.LogWarning("CSV file is empty or has no header");
                return false;
            }

            csv.ReadHeader();
            var headers = csv.HeaderRecord;
            
            if (headers == null || headers.Length == 0)
            {
                _logger.LogWarning("CSV file has no valid headers");
                return false;
            }

            _logger.LogInformation("CSV validation passed. Found {HeaderCount} columns", headers.Length);

            // Try to read at least one data row to ensure the format is valid
            if (await csv.ReadAsync())
            {
                var record = csv.GetRecord<dynamic>();
                _logger.LogDebug("Successfully read first data row");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CSV validation failed");
            return false;
        }
    }

    public async Task<List<CsvBatchProcessingJob>> CreateBatchJobsAsync(CsvProcessingJob job, CancellationToken cancellationToken = default)
    {
        var batchJobs = new List<CsvBatchProcessingJob>();

        try
        {
            using var csvStream = await _storageService.DownloadFileStreamAsync(job.S3Key, cancellationToken);
            using var reader = new StreamReader(csvStream, Encoding.UTF8);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                BadDataFound = null,
                HeaderValidated = null,
                IgnoreBlankLines = true
            });

            // Read header
            await csv.ReadAsync();
            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? Array.Empty<string>();

            var allRows = new List<Dictionary<string, object>>();

            // Read all rows
            while (await csv.ReadAsync())
            {
                var record = new Dictionary<string, object>();
                
                for (int i = 0; i < headers.Length; i++)
                {
                    var header = headers[i];
                    var value = csv.GetField(i) ?? string.Empty;
                    
                    // Try to parse numeric values
                    if (decimal.TryParse(value, out var decimalValue))
                    {
                        record[header] = decimalValue;
                    }
                    else if (DateTime.TryParse(value, out var dateValue))
                    {
                        record[header] = dateValue;
                    }
                    else if (bool.TryParse(value, out var boolValue))
                    {
                        record[header] = boolValue;
                    }
                    else
                    {
                        record[header] = value;
                    }
                }
                
                allRows.Add(record);
            }

            // Create batches
            var totalBatches = (int)Math.Ceiling((double)allRows.Count / BatchSize);
            
            for (int batchNumber = 1; batchNumber <= totalBatches; batchNumber++)
            {
                var startIndex = (batchNumber - 1) * BatchSize;
                var batchRows = allRows.Skip(startIndex).Take(BatchSize).ToList();

                var batchJob = new CsvBatchProcessingJob
                {
                    EmailId = job.EmailId,
                    FileName = job.FileName,
                    SenderEmail = job.SenderEmail,
                    Subject = job.Subject,
                    ReceivedAt = job.ReceivedAt,
                    S3Key = job.S3Key,
                    BatchNumber = batchNumber,
                    TotalBatches = totalBatches,
                    Rows = batchRows
                };

                batchJobs.Add(batchJob);
            }

            _logger.LogInformation("Created {BatchCount} batch jobs for CSV file {FileName} with {TotalRows} rows", 
                totalBatches, job.FileName, allRows.Count);

            return batchJobs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating batch jobs for CSV file {FileName}", job.FileName);
            throw;
        }
    }
}
