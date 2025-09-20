using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;
using PriceListProcessor.Core.Services;
using PriceListProcessor.Jobs;
using PriceListProcessor.Tests.Fixtures;
using PriceListProcessor.Tests.Mocks;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace PriceListProcessor.Tests.IntegrationTests;

public class EndToEndEmailProcessingTests : IClassFixture<TestWebApplicationFactory>, IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public EndToEndEmailProcessingTests(TestWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        // Setup test context with method name
        SetupTestContext();
        await _factory.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.CleanupTestDataAsync();
    }

    private void SetupTestContext([System.Runtime.CompilerServices.CallerMemberName] string testMethodName = "")
    {
        _factory.SetTestContext(testMethodName);
        _output.WriteLine($"Test ID: {_factory.TestId}");
    }

    [Fact]
    public async Task ProcessEmailsEndToEnd_ShouldCompleteFullWorkflow()
    {
        // Setup test context
        SetupTestContext();
        
        // Arrange - Create test emails with different scenarios
        var emails = CreateTestEmails();
        var emailService = _factory.GetEmailService();
        var jobClient = _factory.GetJobClient();

        // Setup mock API client to track all calls
        var apiCalls = new List<ApiCallRecord>();
        _factory.SetupMockApiClient(request =>
        {
            var callRecord = new ApiCallRecord
            {
                Request = request,
                Timestamp = DateTime.UtcNow,
                TestId = _factory.TestId
            };
            apiCalls.Add(callRecord);
            
            _output.WriteLine($"API Call received: {request.FileName} with {request.Data.Count} rows, IsLast: {request.IsLast}");
            
            return new ApiResponse
            {
                Success = true,
                Message = "E2E test API call successful",
                Data = new { 
                    fileName = request.FileName,
                    rowCount = request.Data.Count,
                    isLast = request.IsLast,
                    testId = _factory.TestId,
                    timestamp = DateTime.UtcNow
                }
            };
        });

        // Act - Seed emails and wait for recurring job to process them automatically
        // Clear any existing emails first to ensure clean test
        await emailService.ClearEmailsAsync();
        await emailService.SeedTestEmailsAsync(emails);
        _output.WriteLine($"Seeded {emails.Count} test emails");
        _output.WriteLine("Waiting for recurring job to process emails automatically (every 5 seconds)...");

        // Wait for the recurring job to trigger and process all emails
        await WaitForEmailProcessingCompletionAsync(emails, TimeSpan.FromMinutes(2));
        _output.WriteLine("Recurring job completed email processing");

        // Wait for all background jobs to complete with timeout
        await WaitForJobCompletionAsync(TimeSpan.FromMinutes(2));
        _output.WriteLine("All background jobs completed");

        // Assert - Verify complete workflow
        await VerifyCompleteWorkflow(emails, apiCalls);
    }

    private List<EmailMessage> CreateTestEmails()
    {
        var testId = _factory.TestId;
        
        return new List<EmailMessage>
        {
            // Small CSV - should create 1 batch
            new()
            {
                Id = $"email-{testId}-small",
                From = "supplier1@example.com",
                Subject = $"Small Price List [TEST:{testId}]",
                ReceivedAt = DateTime.UtcNow.AddMinutes(-5),
                Attachments = new List<EmailAttachment>
                {
                    new()
                    {
                        FileName = $"small-prices-test-{testId}.csv",
                        ContentType = "text/csv",
                        Content = Encoding.UTF8.GetBytes(CreateSmallCsv()),
                        Size = 150
                    }
                }
            },
            
            // Large CSV - should create 3 batches (1000, 1000, 500 rows)
            new()
            {
                Id = $"email-{testId}-large",
                From = "supplier2@example.com",
                Subject = $"Large Price List [TEST:{testId}]",
                ReceivedAt = DateTime.UtcNow.AddMinutes(-3),
                Attachments = new List<EmailAttachment>
                {
                    new()
                    {
                        FileName = $"large-prices-test-{testId}.csv",
                        ContentType = "text/csv",
                        Content = Encoding.UTF8.GetBytes(CreateLargeCsv()),
                        Size = 50000
                    }
                }
            },
            
            // Multiple attachments - should process each CSV
            new()
            {
                Id = $"email-{testId}-multiple",
                From = "supplier3@example.com",
                Subject = $"Multiple Files [TEST:{testId}]",
                ReceivedAt = DateTime.UtcNow.AddMinutes(-1),
                Attachments = new List<EmailAttachment>
                {
                    new()
                    {
                        FileName = $"file1-test-{testId}.csv",
                        ContentType = "text/csv",
                        Content = Encoding.UTF8.GetBytes(CreateMediumCsv()),
                        Size = 5000
                    },
                    new()
                    {
                        FileName = $"file2-test-{testId}.csv",
                        ContentType = "text/csv",
                        Content = Encoding.UTF8.GetBytes(CreateMediumCsv()),
                        Size = 5000
                    }
                }
            }
        };
    }

    private string CreateSmallCsv()
    {
        return "Product,SKU,Price,Category\n" +
               "Widget A,WA001,19.99,Electronics\n" +
               "Widget B,WB002,29.99,Electronics\n" +
               "Widget C,WC003,39.99,Electronics";
    }

    private string CreateMediumCsv()
    {
        var csv = new StringBuilder("Product,SKU,Price,Category\n");
        for (int i = 1; i <= 500; i++)
        {
            csv.AppendLine($"Product-{i},SKU-{i:D3},{19.99 + i * 0.01},Category-{i % 5}");
        }
        return csv.ToString();
    }

    private string CreateLargeCsv()
    {
        var csv = new StringBuilder("Product,SKU,Price,Category,Description\n");
        for (int i = 1; i <= 2500; i++)
        {
            csv.AppendLine($"Product-{i},SKU-{i:D4},{19.99 + i * 0.01},Category-{i % 10},Description for product {i}");
        }
        return csv.ToString();
    }

    private async Task WaitForEmailProcessingCompletionAsync(List<EmailMessage> emails, TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        var emailService = _factory.GetEmailService();
        var expectedEmailCount = emails.Count;
        
        _output.WriteLine($"Waiting for {expectedEmailCount} emails to be processed by recurring job (timeout: {timeout.TotalSeconds}s)");
        
        while (DateTime.UtcNow - startTime < timeout)
        {
            var processedCount = 0;
            foreach (var email in emails)
            {
                if (emailService.IsEmailProcessedForTest(email.Id))
                {
                    processedCount++;
                }
            }
            
            if (processedCount >= expectedEmailCount)
            {
                _output.WriteLine($"All {expectedEmailCount} emails processed after {(DateTime.UtcNow - startTime).TotalSeconds:F1}s");
                return;
            }
            
            _output.WriteLine($"Emails processed: {processedCount}/{expectedEmailCount}");
            
            await Task.Delay(1000); // Check every second
        }
        
        throw new TimeoutException($"Email processing did not complete within {timeout.TotalSeconds}s");
    }

    private async Task WaitForJobCompletionAsync(TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        var hangfireApi = JobStorage.Current.GetMonitoringApi();
        
        _output.WriteLine($"Waiting for job completion (timeout: {timeout.TotalSeconds}s)");
        
        while (DateTime.UtcNow - startTime < timeout)
        {
            var processingJobs = hangfireApi.ProcessingJobs(0, 100);
            var scheduledJobs = hangfireApi.ScheduledJobs(0, 100);
            var enqueuedJobs = hangfireApi.EnqueuedJobs(_factory.TestId + "-default", 0, 100);
            
            var totalJobs = processingJobs.Count + scheduledJobs.Count + enqueuedJobs.Count;
            
            if (totalJobs == 0)
            {
                _output.WriteLine($"All jobs completed after {(DateTime.UtcNow - startTime).TotalSeconds:F1}s");
                return;
            }
            
            _output.WriteLine($"Jobs remaining: {totalJobs} (Processing: {processingJobs.Count}, Scheduled: {scheduledJobs.Count}, Enqueued: {enqueuedJobs.Count})");
            
            await Task.Delay(1000); // Check every second
        }
        
        throw new TimeoutException($"Jobs did not complete within {timeout.TotalSeconds}s");
    }

    private async Task VerifyCompleteWorkflow(List<EmailMessage> emails, List<ApiCallRecord> apiCalls)
    {
        var emailService = _factory.GetEmailService();
        
        // Verify all emails were processed
        foreach (var email in emails)
        {
            emailService.IsEmailProcessedForTest(email.Id).Should().BeTrue($"Email {email.Id} should be marked as processed");
        }
        
        // Verify reply emails were sent
        var sentReplies = emailService.GetSentRepliesForTest();
        sentReplies.Should().HaveCount(emails.Count, "One reply should be sent for each processed email");
        
        foreach (var email in emails)
        {
            sentReplies.Should().ContainKey(email.Id, $"Reply should be sent for email {email.Id}");
        }
        
        // Verify API calls were made
        apiCalls.Should().NotBeEmpty("API calls should have been made");
        
        // Group API calls by filename to analyze batches
        var apiCallsByFile = apiCalls.GroupBy(c => c.Request.FileName).ToList();
        
        // Verify small CSV (1 batch)
        var smallCsvCalls = apiCallsByFile.FirstOrDefault(g => g.Key.Contains("small-prices"));
        smallCsvCalls.Should().NotBeNull("Small CSV should have API calls");
        smallCsvCalls!.Should().HaveCount(1, "Small CSV should create exactly 1 batch");
        smallCsvCalls.First().Request.IsLast.Should().BeTrue("Single batch should be marked as last");
        smallCsvCalls.First().Request.Data.Should().HaveCount(3, "Small CSV should have 3 data rows");
        
        // Verify large CSV (3 batches)
        var largeCsvCalls = apiCallsByFile.FirstOrDefault(g => g.Key.Contains("large-prices"));
        largeCsvCalls.Should().NotBeNull("Large CSV should have API calls");
        largeCsvCalls!.Should().HaveCount(3, "Large CSV should create exactly 3 batches");
        
        var orderedLargeCalls = largeCsvCalls.OrderBy(c => c.Timestamp).ToList();
        orderedLargeCalls[0].Request.Data.Should().HaveCount(1000, "First batch should have 1000 rows");
        orderedLargeCalls[1].Request.Data.Should().HaveCount(1000, "Second batch should have 1000 rows");
        orderedLargeCalls[2].Request.Data.Should().HaveCount(500, "Third batch should have 500 rows");
        
        orderedLargeCalls[0].Request.IsLast.Should().BeFalse("First batch should not be last");
        orderedLargeCalls[1].Request.IsLast.Should().BeFalse("Second batch should not be last");
        orderedLargeCalls[2].Request.IsLast.Should().BeTrue("Third batch should be last");
        
        // Verify multiple files CSV (2 files, each 1 batch)
        var file1Calls = apiCallsByFile.FirstOrDefault(g => g.Key.Contains("file1-test"));
        var file2Calls = apiCallsByFile.FirstOrDefault(g => g.Key.Contains("file2-test"));
        
        file1Calls.Should().NotBeNull("File1 should have API calls");
        file2Calls.Should().NotBeNull("File2 should have API calls");
        
        file1Calls!.Should().HaveCount(1, "File1 should create exactly 1 batch");
        file2Calls!.Should().HaveCount(1, "File2 should create exactly 1 batch");
        
        file1Calls.First().Request.IsLast.Should().BeTrue("File1 single batch should be marked as last");
        file2Calls.First().Request.IsLast.Should().BeTrue("File2 single batch should be marked as last");
        
        // Verify all API calls contain the test ID
        apiCalls.Should().AllSatisfy(call => 
            call.Request.FileName.Should().Contain(_factory.TestId, "All API calls should contain the test ID"));
        
        // Verify sequential processing for large CSV
        for (int i = 1; i < orderedLargeCalls.Count; i++)
        {
            orderedLargeCalls[i].Timestamp.Should().BeAfter(orderedLargeCalls[i-1].Timestamp, 
                "Batches should be processed sequentially");
        }
        
        _output.WriteLine($"E2E Test Results:");
        _output.WriteLine($"- Processed {emails.Count} emails");
        _output.WriteLine($"- Made {apiCalls.Count} API calls");
        _output.WriteLine($"- Created {apiCallsByFile.Count} file processing jobs");
        _output.WriteLine($"- Sent {sentReplies.Count} reply emails");
        
        _output.WriteLine($"API Call Summary:");
        foreach (var fileGroup in apiCallsByFile)
        {
            var totalRows = fileGroup.Sum(c => c.Request.Data.Count);
            _output.WriteLine($"  {fileGroup.Key}: {fileGroup.Count()} batches, {totalRows} total rows");
        }
    }
}
