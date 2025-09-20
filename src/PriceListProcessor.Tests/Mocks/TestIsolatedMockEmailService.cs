using Microsoft.Extensions.Logging;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;
using System.Collections.Concurrent;

namespace PriceListProcessor.Tests.Mocks;

public class TestIsolatedMockEmailService : IMockEmailService
{
    private readonly ILogger<TestIsolatedMockEmailService> _logger;
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<EmailMessage>> _testEmailQueues = new();
    private static readonly ConcurrentDictionary<string, HashSet<string>> _testProcessedEmails = new();
    private static readonly ConcurrentDictionary<string, Dictionary<string, EmailMessage>> _testSentReplies = new();
    private readonly string _currentTestId;

    public TestIsolatedMockEmailService(ILogger<TestIsolatedMockEmailService> logger, string? testId = null)
    {
        _logger = logger;
        _currentTestId = testId ?? "default";
        
        // Initialize collections for this test if they don't exist
        _testEmailQueues.TryAdd(_currentTestId, new ConcurrentQueue<EmailMessage>());
        _testProcessedEmails.TryAdd(_currentTestId, new HashSet<string>());
        _testSentReplies.TryAdd(_currentTestId, new Dictionary<string, EmailMessage>());
    }

    public async Task<List<EmailMessage>> GetNewEmailsAsync(CancellationToken cancellationToken = default)
    {
        var newEmails = new List<EmailMessage>();
        var emailQueue = _testEmailQueues[_currentTestId];
        var processedEmails = _testProcessedEmails[_currentTestId];

        while (emailQueue.TryDequeue(out var email))
        {
            if (!processedEmails.Contains(email.Id))
            {
                newEmails.Add(email);
                _logger.LogInformation("[TEST:{TestId}] Retrieved mock email: {Subject} from {From} with {AttachmentCount} CSV attachments", 
                    _currentTestId, email.Subject, email.From, email.Attachments.Count);
            }
        }

        await Task.CompletedTask;
        return newEmails;
    }

    public async Task SendReplyAsync(string originalEmailId, string replyContent, CancellationToken cancellationToken = default)
    {
        var sentReplies = _testSentReplies[_currentTestId];
        
        var replyEmail = new EmailMessage
        {
            Id = $"reply_{originalEmailId}_{DateTime.UtcNow.Ticks}",
            From = "processor@tekara.com",
            Subject = $"Re: Processing Results [TEST:{_currentTestId}]",
            ReceivedAt = DateTime.UtcNow,
            Attachments = new List<EmailAttachment>()
        };

        sentReplies[originalEmailId] = replyEmail;
        _logger.LogInformation("[TEST:{TestId}] Mock reply sent for email {EmailId}: {Content}", 
            _currentTestId, originalEmailId, replyContent);
        
        await Task.CompletedTask;
    }

    public async Task MarkAsProcessedAsync(string emailId, CancellationToken cancellationToken = default)
    {
        var processedEmails = _testProcessedEmails[_currentTestId];
        processedEmails.Add(emailId);
        _logger.LogDebug("[TEST:{TestId}] Marked mock email {EmailId} as processed", _currentTestId, emailId);
        await Task.CompletedTask;
    }

    public async Task SeedTestEmailsAsync(List<EmailMessage> testEmails, CancellationToken cancellationToken = default)
    {
        var emailQueue = _testEmailQueues[_currentTestId];
        
        foreach (var email in testEmails)
        {
            // Add test identifier to the email subject if not already present
            if (!email.Subject.Contains($"[TEST:{_currentTestId}]"))
            {
                email.Subject = $"{email.Subject} [TEST:{_currentTestId}]";
            }
            
            // Add test identifier to filename
            foreach (var attachment in email.Attachments)
            {
                if (!attachment.FileName.Contains($"test-{_currentTestId}"))
                {
                    var extension = Path.GetExtension(attachment.FileName);
                    var nameWithoutExtension = Path.GetFileNameWithoutExtension(attachment.FileName);
                    attachment.FileName = $"{nameWithoutExtension}-test-{_currentTestId}{extension}";
                }
            }
            
            emailQueue.Enqueue(email);
            _logger.LogInformation("[TEST:{TestId}] Seeded test email: {Subject} from {From}", 
                _currentTestId, email.Subject, email.From);
        }
        
        await Task.CompletedTask;
    }

    public async Task ClearEmailsAsync(CancellationToken cancellationToken = default)
    {
        var emailQueue = _testEmailQueues[_currentTestId];
        var processedEmails = _testProcessedEmails[_currentTestId];
        var sentReplies = _testSentReplies[_currentTestId];
        
        while (emailQueue.TryDequeue(out _)) { }
        processedEmails.Clear();
        sentReplies.Clear();
        
        _logger.LogInformation("[TEST:{TestId}] Cleared all mock emails", _currentTestId);
        await Task.CompletedTask;
    }

    // Test-specific methods
    public Dictionary<string, EmailMessage> GetSentRepliesForTest()
    {
        var sentReplies = _testSentReplies[_currentTestId];
        return new Dictionary<string, EmailMessage>(sentReplies);
    }
    
    public bool IsEmailProcessedForTest(string emailId)
    {
        var processedEmails = _testProcessedEmails[_currentTestId];
        return processedEmails.Contains(emailId);
    }

    public int GetQueueCountForTest()
    {
        var emailQueue = _testEmailQueues[_currentTestId];
        return emailQueue.Count;
    }

    public int GetProcessedCountForTest()
    {
        var processedEmails = _testProcessedEmails[_currentTestId];
        return processedEmails.Count;
    }

    // Static methods for test management
    public static void ClearTestData(string testId)
    {
        if (_testEmailQueues.TryRemove(testId, out var queue))
        {
            while (queue.TryDequeue(out _)) { }
        }
        
        if (_testProcessedEmails.TryRemove(testId, out var processed))
        {
            processed.Clear();
        }
        
        if (_testSentReplies.TryRemove(testId, out var replies))
        {
            replies.Clear();
        }
    }

    public static void ClearAllTestData()
    {
        foreach (var testId in _testEmailQueues.Keys.ToList())
        {
            ClearTestData(testId);
        }
    }

    public static Dictionary<string, int> GetQueueCountsByTest()
    {
        return _testEmailQueues.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
    }

    public static Dictionary<string, int> GetProcessedCountsByTest()
    {
        return _testProcessedEmails.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
    }

    public static Dictionary<string, int> GetSentRepliesCountsByTest()
    {
        return _testSentReplies.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
    }

    public static TestIsolatedMockEmailService CreateForTest(string testId, ILogger<TestIsolatedMockEmailService> logger)
    {
        return new TestIsolatedMockEmailService(logger, testId);
    }
}
