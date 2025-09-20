using Hangfire;
using Microsoft.AspNetCore.Mvc;
using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;
using PriceListProcessor.Jobs;

namespace PriceListProcessor.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly ILogger<TestController> _logger;
    private readonly IEmailService _emailService;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public TestController(
        ILogger<TestController> logger,
        IEmailService emailService,
        IBackgroundJobClient backgroundJobClient)
    {
        _logger = logger;
        _emailService = emailService;
        _backgroundJobClient = backgroundJobClient;
    }

    [HttpPost("seed-test-emails")]
    public async Task<IActionResult> SeedTestEmails()
    {
        if (_emailService is not IMockEmailService mockEmailService)
        {
            return BadRequest("This endpoint is only available when using the mock email service");
        }

        await mockEmailService.ClearEmailsAsync();

        var testEmails = new List<EmailMessage>
        {
            new()
            {
                Id = $"test-email-{DateTime.UtcNow.Ticks}",
                From = "supplier@example.com",
                Subject = "Test Price List",
                ReceivedAt = DateTime.UtcNow,
                Attachments = new List<EmailAttachment>
                {
                    new()
                    {
                        FileName = "test-prices.csv",
                        ContentType = "text/csv",
                        Content = System.Text.Encoding.UTF8.GetBytes("""
                            Product,SKU,Price,Quantity
                            Test Product 1,TP001,99.99,10
                            Test Product 2,TP002,149.99,5
                            Test Product 3,TP003,199.99,3
                            """),
                        Size = 150
                    }
                }
            }
        };

        await mockEmailService.SeedTestEmailsAsync(testEmails);
        
        _logger.LogInformation("Seeded {Count} test emails", testEmails.Count);
        
        return Ok(new { message = $"Seeded {testEmails.Count} test emails", emails = testEmails.Count });
    }

    [HttpPost("trigger-email-processing")]
    public IActionResult TriggerEmailProcessing()
    {
        var jobId = _backgroundJobClient.Enqueue<EmailProcessingJob>(job => job.ProcessNewEmailsAsync());
        
        _logger.LogInformation("Triggered email processing job with ID: {JobId}", jobId);
        
        return Ok(new { message = "Email processing job triggered", jobId });
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new 
        { 
            status = "healthy", 
            timestamp = DateTime.UtcNow,
            service = "PriceListProcessor"
        });
    }

    [HttpGet("email-service-type")]
    public IActionResult GetEmailServiceType()
    {
        var serviceType = _emailService.GetType().Name;
        return Ok(new { emailServiceType = serviceType });
    }
}
