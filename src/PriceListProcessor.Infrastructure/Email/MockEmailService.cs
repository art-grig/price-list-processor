using Microsoft.Extensions.Logging;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;
using System.Collections.Concurrent;

namespace PriceListProcessor.Infrastructure.Email;

public class MockEmailService : IMockEmailService
{
    private readonly ILogger<MockEmailService> _logger;
    private readonly ConcurrentQueue<EmailMessage> _emailQueue = new();
    private readonly HashSet<string> _processedEmails = new();
    private readonly Dictionary<string, EmailMessage> _sentReplies = new();

    public MockEmailService(ILogger<MockEmailService> logger)
    {
        _logger = logger;
    }

    public async Task<List<EmailMessage>> GetNewEmailsAsync(CancellationToken cancellationToken = default)
    {
        var newEmails = new List<EmailMessage>();

        while (_emailQueue.TryDequeue(out var email))
        {
            if (!_processedEmails.Contains(email.Id))
            {
                newEmails.Add(email);
                _logger.LogInformation("Retrieved mock email: {Subject} from {From} with {AttachmentCount} CSV attachments", 
                    email.Subject, email.From, email.Attachments.Count);
            }
        }

        await Task.CompletedTask;
        return newEmails;
    }

    public async Task SendReplyAsync(string originalEmailId, string replyContent, CancellationToken cancellationToken = default)
    {
        var replyEmail = new EmailMessage
        {
            Id = $"reply_{originalEmailId}_{DateTime.UtcNow.Ticks}",
            From = "processor@tekara.com",
            Subject = $"Re: Processing Results",
            ReceivedAt = DateTime.UtcNow,
            Attachments = new List<EmailAttachment>()
        };

        _sentReplies[originalEmailId] = replyEmail;
        _logger.LogInformation("Mock reply sent for email {EmailId}: {Content}", originalEmailId, replyContent);
        
        await Task.CompletedTask;
    }

    public async Task MarkAsProcessedAsync(string emailId, CancellationToken cancellationToken = default)
    {
        _processedEmails.Add(emailId);
        _logger.LogDebug("Marked mock email {EmailId} as processed", emailId);
        await Task.CompletedTask;
    }

    public async Task SeedTestEmailsAsync(List<EmailMessage> testEmails, CancellationToken cancellationToken = default)
    {
        foreach (var email in testEmails)
        {
            _emailQueue.Enqueue(email);
            _logger.LogInformation("Seeded test email: {Subject} from {From}", email.Subject, email.From);
        }
        
        await Task.CompletedTask;
    }

    public async Task ClearEmailsAsync(CancellationToken cancellationToken = default)
    {
        while (_emailQueue.TryDequeue(out _)) { }
        _processedEmails.Clear();
        _sentReplies.Clear();
        
        _logger.LogInformation("Cleared all mock emails");
        await Task.CompletedTask;
    }

    public Dictionary<string, EmailMessage> GetSentReplies() => new(_sentReplies);
    
    public bool IsEmailProcessed(string emailId) => _processedEmails.Contains(emailId);
}
