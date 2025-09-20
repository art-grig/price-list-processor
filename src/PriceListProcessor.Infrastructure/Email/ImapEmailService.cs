using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;

namespace PriceListProcessor.Infrastructure.Email;

public class ImapEmailService : IEmailService
{
    private readonly ILogger<ImapEmailService> _logger;
    private readonly EmailConfiguration _config;
    private readonly HashSet<string> _processedEmails = new();

    public ImapEmailService(ILogger<ImapEmailService> logger, IOptions<EmailConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
    }

    public async Task<List<EmailMessage>> GetNewEmailsAsync(CancellationToken cancellationToken = default)
    {
        var emails = new List<EmailMessage>();

        try
        {
            using var client = new ImapClient();
            
            await client.ConnectAsync(_config.ImapHost, _config.ImapPort, _config.UseSsl, cancellationToken);
            await client.AuthenticateAsync(_config.Username, _config.Password, cancellationToken);

            await client.Inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            // Search for unread emails
            var uids = await client.Inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken);
            _logger.LogInformation("Found {Count} unread messages", uids.Count);

            foreach (var uid in uids)
            {
                var message = await client.Inbox.GetMessageAsync(uid, cancellationToken);
                var emailId = message.MessageId ?? $"imap_{uid}_{DateTime.UtcNow.Ticks}";

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

                // Mark as read
                await client.Inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken);
            }

            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving emails via IMAP");
            throw;
        }

        return emails;
    }

    public async Task SendReplyAsync(string originalEmailId, string replyContent, CancellationToken cancellationToken = default)
    {
        try
        {
            using var smtpClient = new SmtpClient();
            
            await smtpClient.ConnectAsync(_config.SmtpHost, _config.SmtpPort, SecureSocketOptions.StartTls, cancellationToken);
            await smtpClient.AuthenticateAsync(_config.Username, _config.Password, cancellationToken);

            // Find original message to get reply details
            using var imapClient = new ImapClient();
            await imapClient.ConnectAsync(_config.ImapHost, _config.ImapPort, _config.UseSsl, cancellationToken);
            await imapClient.AuthenticateAsync(_config.Username, _config.Password, cancellationToken);
            await imapClient.Inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var query = SearchQuery.HeaderContains("Message-ID", originalEmailId);
            var uids = await imapClient.Inbox.SearchAsync(query, cancellationToken);

            if (uids.Count > 0)
            {
                var originalMessage = await imapClient.Inbox.GetMessageAsync(uids[0], cancellationToken);
                
                var reply = new MimeMessage();
                reply.From.Add(new MailboxAddress("Price List Processor", _config.Username));
                reply.To.AddRange(originalMessage.From);
                reply.Subject = $"Re: {originalMessage.Subject}";
                reply.InReplyTo = originalMessage.MessageId;
                reply.References.Add(originalMessage.MessageId);
                
                var bodyBuilder = new BodyBuilder
                {
                    TextBody = replyContent
                };
                reply.Body = bodyBuilder.ToMessageBody();

                await smtpClient.SendAsync(reply, cancellationToken);
                _logger.LogInformation("Reply sent for email {EmailId}", originalEmailId);
            }
            else
            {
                _logger.LogWarning("Original email {EmailId} not found for reply", originalEmailId);
            }

            await imapClient.DisconnectAsync(true, cancellationToken);
            await smtpClient.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending reply for email {EmailId}", originalEmailId);
            throw;
        }
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
                    Size = memory.Length,
                    Content = memory.ToArray()
                });
            }
        }

        return attachments;
    }
}
