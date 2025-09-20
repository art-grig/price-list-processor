using MailKit;
using MailKit.Net.Pop3;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;

namespace PriceListProcessor.Infrastructure.Email;

public class Pop3EmailService : IEmailService
{
    private readonly ILogger<Pop3EmailService> _logger;
    private readonly EmailConfiguration _config;
    private readonly HashSet<string> _processedEmails = new();

    public Pop3EmailService(ILogger<Pop3EmailService> logger, IOptions<EmailConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
    }

    public async Task<List<EmailMessage>> GetNewEmailsAsync(CancellationToken cancellationToken = default)
    {
        var emails = new List<EmailMessage>();

        try
        {
            using var client = new Pop3Client();
            
            await client.ConnectAsync(_config.Pop3Host, _config.Pop3Port, _config.UseSsl, cancellationToken);
            await client.AuthenticateAsync(_config.Username, _config.Password, cancellationToken);

            var messageCount = client.Count;
            _logger.LogInformation("Found {Count} messages in mailbox", messageCount);

            for (int i = 0; i < messageCount; i++)
            {
                var message = await client.GetMessageAsync(i, cancellationToken);
                var emailId = message.MessageId ?? $"pop3_{i}_{DateTime.UtcNow.Ticks}";

                if (_processedEmails.Contains(emailId))
                {
                    continue;
                }

                var csvAttachments = GetCsvAttachments(message);
                if (csvAttachments.Count == 0)
                {
                    continue;
                }

                var emailMessage = new EmailMessage
                {
                    Id = emailId,
                    From = message.From.ToString(),
                    Subject = message.Subject ?? string.Empty,
                    ReceivedAt = message.Date.DateTime,
                    Attachments = csvAttachments
                };

                emails.Add(emailMessage);
                _logger.LogInformation("Found email with CSV attachments: {Subject} from {From}", 
                    emailMessage.Subject, emailMessage.From);
            }

            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving emails via POP3");
            throw;
        }

        return emails;
    }

    public async Task SendReplyAsync(string originalEmailId, string replyContent, CancellationToken cancellationToken = default)
    {
        // POP3 doesn't support sending emails directly
        // In a real implementation, you would use SMTP for sending replies
        _logger.LogWarning("POP3 service cannot send replies directly. Original email ID: {EmailId}", originalEmailId);
        await Task.CompletedTask;
    }

    public async Task MarkAsProcessedAsync(string emailId, CancellationToken cancellationToken = default)
    {
        _processedEmails.Add(emailId);
        _logger.LogDebug("Marked email {EmailId} as processed", emailId);
        await Task.CompletedTask;
    }

    private static List<EmailAttachment> GetCsvAttachments(MimeMessage message)
    {
        var attachments = new List<EmailAttachment>();

        foreach (var attachment in message.Attachments)
        {
            if (attachment is MimePart mimePart)
            {
                var fileName = mimePart.FileName;
                if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var memory = new MemoryStream();
                mimePart.Content.DecodeTo(memory);

                attachments.Add(new EmailAttachment
                {
                    FileName = fileName,
                    ContentType = mimePart.ContentType.MimeType,
                    Content = memory.ToArray(),
                    Size = memory.Length
                });
            }
        }

        return attachments;
    }
}

public class EmailConfiguration
{
    public string Provider { get; set; } = "mock"; // pop3, imap, mock
    public string Pop3Host { get; set; } = string.Empty;
    public int Pop3Port { get; set; } = 995;
    public string ImapHost { get; set; } = string.Empty;
    public int ImapPort { get; set; } = 993;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = true;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
}
