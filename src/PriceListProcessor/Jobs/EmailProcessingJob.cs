using Hangfire;
using Microsoft.Extensions.Logging;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;

namespace PriceListProcessor.Jobs;

public class EmailProcessingJob
{
    private readonly ILogger<EmailProcessingJob> _logger;
    private readonly IEmailService _emailService;
    private readonly IStorageService _storageService;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public EmailProcessingJob(
        ILogger<EmailProcessingJob> logger,
        IEmailService emailService,
        IStorageService storageService,
        IBackgroundJobClient backgroundJobClient)
    {
        _logger = logger;
        _emailService = emailService;
        _storageService = storageService;
        _backgroundJobClient = backgroundJobClient;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 300, 600, 900 })] // 5, 10, 15 minutes
    [DisableConcurrentExecution(300)] // Prevent concurrent execution for 5 minutes
    public async Task ProcessNewEmailsAsync()
    {
        try
        {
            _logger.LogInformation("Starting email processing job");
            
            var emails = await _emailService.GetNewEmailsAsync();
            
            if (emails.Count == 0)
            {
                _logger.LogDebug("No new emails with CSV attachments found");
                return;
            }

            foreach (var email in emails)
            {
                try
                {
                    await ProcessEmailAsync(email);
                    await _emailService.MarkAsProcessedAsync(email.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing email {EmailId}", email.Id);
                    throw; // Let Hangfire handle the retry
                }
            }

            _logger.LogInformation("Completed processing {EmailCount} emails", emails.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in email processing job");
            throw;
        }
    }

    private async Task ProcessEmailAsync(EmailMessage email)
    {
        _logger.LogInformation("Processing email {EmailId} with {AttachmentCount} CSV attachments", 
            email.Id, email.Attachments.Count);

        foreach (var attachment in email.Attachments)
        {
            try
            {
                // Upload CSV to storage
                var s3Key = await _storageService.UploadFileAsync(attachment.FileName, attachment.Content);

                // Create CSV processing job
                var csvJob = new Core.Models.CsvProcessingJob
                {
                    EmailId = email.Id,
                    FileName = attachment.FileName,
                    SenderEmail = email.From,
                    Subject = email.Subject,
                    ReceivedAt = email.ReceivedAt,
                    S3Key = s3Key
                };

                // Enqueue CSV processing job
                _backgroundJobClient.Enqueue<Jobs.CsvProcessingJob>(job => job.ProcessCsvFileAsync(csvJob));
                
                _logger.LogInformation("Enqueued CSV processing job for file {FileName}", attachment.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing attachment {FileName} from email {EmailId}", 
                    attachment.FileName, email.Id);
                throw;
            }
        }
    }
}
