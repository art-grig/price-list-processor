using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PriceListProcessor.Core.Models;
using PriceListProcessor.Tests.Mocks;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace PriceListProcessor.Tests.UnitTests;

public class MockEmailServiceTests : IAsyncLifetime
{
    private readonly Mock<ILogger<TestIsolatedMockEmailService>> _mockLogger;
    private readonly ITestOutputHelper _output;
    private readonly string _testId;

    public MockEmailServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _testId = Guid.NewGuid().ToString("N")[..8];
        _mockLogger = new Mock<ILogger<TestIsolatedMockEmailService>>();
    }

    public async Task InitializeAsync()
    {
        // Clean up any existing test data
        TestIsolatedMockEmailService.ClearTestData(_testId);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Clean up test data
        TestIsolatedMockEmailService.ClearTestData(_testId);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SeedTestEmailsAsync_ShouldAddTestIdentifierToSubjectAndFileName()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var emailService = new TestIsolatedMockEmailService(_mockLogger.Object, _testId);
        
        var testEmails = new List<EmailMessage>
        {
            new()
            {
                Id = $"email-{_testId}-1",
                From = "supplier@example.com",
                Subject = "Original Subject",
                ReceivedAt = DateTime.UtcNow,
                Attachments = new List<EmailAttachment>
                {
                    new()
                    {
                        FileName = "original.csv",
                        ContentType = "text/csv",
                        Content = Encoding.UTF8.GetBytes("Product,Price\nTest,10.00"),
                        Size = 100
                    }
                }
            }
        };

        // Act
        await emailService.SeedTestEmailsAsync(testEmails);
        var retrievedEmails = await emailService.GetNewEmailsAsync();

        // Assert
        retrievedEmails.Should().HaveCount(1);
        var email = retrievedEmails[0];
        
        email.Subject.Should().Contain($"[TEST:{_testId}]", "subject should contain test identifier");
        email.Attachments[0].FileName.Should().Contain($"test-{_testId}", "filename should contain test identifier");

        _output.WriteLine($"Subject: {email.Subject}");
        _output.WriteLine($"Filename: {email.Attachments[0].FileName}");
    }

    [Fact]
    public async Task GetNewEmailsAsync_ShouldReturnOnlyUnprocessedEmails()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var emailService = new TestIsolatedMockEmailService(_mockLogger.Object, _testId);
        
        var testEmails = new List<EmailMessage>
        {
            new()
            {
                Id = $"email-{_testId}-1",
                From = "supplier1@example.com",
                Subject = "Email 1",
                ReceivedAt = DateTime.UtcNow,
                Attachments = new List<EmailAttachment>
                {
                    new() { FileName = "file1.csv", ContentType = "text/csv", Content = new byte[10], Size = 10 }
                }
            },
            new()
            {
                Id = $"email-{_testId}-2",
                From = "supplier2@example.com",
                Subject = "Email 2",
                ReceivedAt = DateTime.UtcNow,
                Attachments = new List<EmailAttachment>
                {
                    new() { FileName = "file2.csv", ContentType = "text/csv", Content = new byte[20], Size = 20 }
                }
            }
        };

        await emailService.SeedTestEmailsAsync(testEmails);

        // Act - First retrieval should return all emails
        var firstRetrieval = await emailService.GetNewEmailsAsync();
        
        // Mark first email as processed
        await emailService.MarkAsProcessedAsync(firstRetrieval[0].Id);
        
        // Seed the same emails again
        await emailService.SeedTestEmailsAsync(testEmails);
        
        // Act - Second retrieval should only return unprocessed email
        var secondRetrieval = await emailService.GetNewEmailsAsync();

        // Assert
        firstRetrieval.Should().HaveCount(2, "first retrieval should return all emails");
        secondRetrieval.Should().HaveCount(1, "second retrieval should only return unprocessed email");
        
        secondRetrieval[0].From.Should().Be("supplier2@example.com", "should return the unprocessed email");

        _output.WriteLine($"First retrieval: {firstRetrieval.Count} emails");
        _output.WriteLine($"Second retrieval: {secondRetrieval.Count} emails");
    }

    [Fact]
    public async Task MarkAsProcessedAsync_ShouldPreventEmailFromBeingReturnedAgain()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var emailService = new TestIsolatedMockEmailService(_mockLogger.Object, _testId);
        
        var testEmail = new EmailMessage
        {
            Id = $"email-{_testId}-processed",
            From = "supplier@example.com",
            Subject = "Test Email",
            ReceivedAt = DateTime.UtcNow,
            Attachments = new List<EmailAttachment>
            {
                new() { FileName = "test.csv", ContentType = "text/csv", Content = new byte[10], Size = 10 }
            }
        };

        await emailService.SeedTestEmailsAsync(new List<EmailMessage> { testEmail });

        // Act
        var emailsBeforeProcessing = await emailService.GetNewEmailsAsync();
        await emailService.MarkAsProcessedAsync(testEmail.Id);
        
        // Seed the same email again
        await emailService.SeedTestEmailsAsync(new List<EmailMessage> { testEmail });
        var emailsAfterProcessing = await emailService.GetNewEmailsAsync();

        // Assert
        emailsBeforeProcessing.Should().HaveCount(1, "should return email before processing");
        emailsAfterProcessing.Should().HaveCount(0, "should not return processed email");
        
        emailService.IsEmailProcessedForTest(testEmail.Id).Should().BeTrue("email should be marked as processed");

        _output.WriteLine($"Before processing: {emailsBeforeProcessing.Count} emails");
        _output.WriteLine($"After processing: {emailsAfterProcessing.Count} emails");
    }

    [Fact]
    public async Task SendReplyAsync_ShouldStoreReplyWithTestIdentifier()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var emailService = new TestIsolatedMockEmailService(_mockLogger.Object, _testId);
        var originalEmailId = $"email-{_testId}-original";
        var replyContent = "Processing completed successfully";

        // Act
        await emailService.SendReplyAsync(originalEmailId, replyContent);

        // Assert
        var sentReplies = emailService.GetSentRepliesForTest();
        sentReplies.Should().ContainKey(originalEmailId, "reply should be stored for original email");
        
        var reply = sentReplies[originalEmailId];
        reply.Subject.Should().Contain($"[TEST:{_testId}]", "reply subject should contain test identifier");
        reply.From.Should().Be("processor@tekara.com");

        _output.WriteLine($"Reply stored for email: {originalEmailId}");
        _output.WriteLine($"Reply subject: {reply.Subject}");
    }

    [Fact]
    public async Task ClearEmailsAsync_ShouldRemoveAllTestData()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var emailService = new TestIsolatedMockEmailService(_mockLogger.Object, _testId);
        
        var testEmails = new List<EmailMessage>
        {
            new()
            {
                Id = $"email-{_testId}-1",
                From = "supplier@example.com",
                Subject = "Test Email",
                ReceivedAt = DateTime.UtcNow,
                Attachments = new List<EmailAttachment>
                {
                    new() { FileName = "test.csv", ContentType = "text/csv", Content = new byte[10], Size = 10 }
                }
            }
        };

        await emailService.SeedTestEmailsAsync(testEmails);
        await emailService.SendReplyAsync("some-email-id", "test reply");
        await emailService.MarkAsProcessedAsync("some-email-id");

        // Verify data exists
        emailService.GetQueueCountForTest().Should().BeGreaterThan(0);
        emailService.GetProcessedCountForTest().Should().BeGreaterThan(0);
        emailService.GetSentRepliesForTest().Should().NotBeEmpty();

        // Act
        await emailService.ClearEmailsAsync();

        // Assert
        emailService.GetQueueCountForTest().Should().Be(0, "queue should be empty");
        emailService.GetProcessedCountForTest().Should().Be(0, "processed emails should be cleared");
        emailService.GetSentRepliesForTest().Should().BeEmpty("sent replies should be cleared");

        _output.WriteLine("All test data cleared successfully");
    }

    [Fact]
    public void MultipleTestInstances_ShouldIsolateDataCorrectly()
    {
        // Arrange
        _output.WriteLine($"Primary Test ID: {_testId}");
        
        var testId1 = $"{_testId}-1";
        var testId2 = $"{_testId}-2";
        
        var emailService1 = new TestIsolatedMockEmailService(_mockLogger.Object, testId1);
        var emailService2 = new TestIsolatedMockEmailService(_mockLogger.Object, testId2);

        var email1 = new EmailMessage
        {
            Id = $"email-{testId1}",
            From = "supplier1@example.com",
            Subject = "Email for Service 1",
            ReceivedAt = DateTime.UtcNow,
            Attachments = new List<EmailAttachment>
            {
                new() { FileName = "service1.csv", ContentType = "text/csv", Content = new byte[10], Size = 10 }
            }
        };

        var email2 = new EmailMessage
        {
            Id = $"email-{testId2}",
            From = "supplier2@example.com",
            Subject = "Email for Service 2",
            ReceivedAt = DateTime.UtcNow,
            Attachments = new List<EmailAttachment>
            {
                new() { FileName = "service2.csv", ContentType = "text/csv", Content = new byte[20], Size = 20 }
            }
        };

        // Act
        emailService1.SeedTestEmailsAsync(new List<EmailMessage> { email1 }).Wait();
        emailService2.SeedTestEmailsAsync(new List<EmailMessage> { email2 }).Wait();

        var emails1 = emailService1.GetNewEmailsAsync().Result;
        var emails2 = emailService2.GetNewEmailsAsync().Result;

        // Assert
        emails1.Should().HaveCount(1, "service 1 should have its own email");
        emails2.Should().HaveCount(1, "service 2 should have its own email");
        
        emails1[0].From.Should().Be("supplier1@example.com");
        emails2[0].From.Should().Be("supplier2@example.com");
        
        emails1[0].Subject.Should().Contain(testId1);
        emails2[0].Subject.Should().Contain(testId2);

        // Clean up
        TestIsolatedMockEmailService.ClearTestData(testId1);
        TestIsolatedMockEmailService.ClearTestData(testId2);

        _output.WriteLine($"Service 1 ({testId1}): {emails1.Count} emails");
        _output.WriteLine($"Service 2 ({testId2}): {emails2.Count} emails");
        _output.WriteLine("Data isolation verified successfully");
    }

    [Fact]
    public void StaticMethods_ShouldProvideCorrectStatistics()
    {
        // Arrange
        _output.WriteLine($"Test ID: {_testId}");
        
        var testId1 = $"{_testId}-stats-1";
        var testId2 = $"{_testId}-stats-2";
        
        var emailService1 = new TestIsolatedMockEmailService(_mockLogger.Object, testId1);
        var emailService2 = new TestIsolatedMockEmailService(_mockLogger.Object, testId2);

        var email1 = new EmailMessage
        {
            Id = $"email-{testId1}",
            From = "supplier@example.com",
            Subject = "Test",
            ReceivedAt = DateTime.UtcNow,
            Attachments = new List<EmailAttachment>
            {
                new() { FileName = "test.csv", ContentType = "text/csv", Content = new byte[10], Size = 10 }
            }
        };

        // Act
        emailService1.SeedTestEmailsAsync(new List<EmailMessage> { email1 }).Wait();
        emailService1.SeedTestEmailsAsync(new List<EmailMessage> { email1 }).Wait(); // Seed twice
        emailService2.SeedTestEmailsAsync(new List<EmailMessage> { email1 }).Wait();

        emailService1.SendReplyAsync("reply-1", "content").Wait();
        emailService2.SendReplyAsync("reply-2", "content").Wait();
        emailService2.SendReplyAsync("reply-3", "content").Wait(); // Two replies for service 2

        // Assert
        var queueCounts = TestIsolatedMockEmailService.GetQueueCountsByTest();
        var replyCounts = TestIsolatedMockEmailService.GetSentRepliesCountsByTest();

        queueCounts.Should().ContainKey(testId1);
        queueCounts.Should().ContainKey(testId2);
        queueCounts[testId1].Should().Be(2, "service 1 should have 2 emails in queue");
        queueCounts[testId2].Should().Be(1, "service 2 should have 1 email in queue");

        replyCounts[testId1].Should().Be(1, "service 1 should have 1 reply");
        replyCounts[testId2].Should().Be(2, "service 2 should have 2 replies");

        // Clean up
        TestIsolatedMockEmailService.ClearTestData(testId1);
        TestIsolatedMockEmailService.ClearTestData(testId2);

        _output.WriteLine($"Queue counts: {string.Join(", ", queueCounts.Select(kv => $"{kv.Key}={kv.Value}"))}");
        _output.WriteLine($"Reply counts: {string.Join(", ", replyCounts.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }
}
