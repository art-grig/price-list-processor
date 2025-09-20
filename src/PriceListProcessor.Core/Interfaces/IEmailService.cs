using PriceListProcessor.Core.Models;

namespace PriceListProcessor.Core.Interfaces;

public interface IEmailService
{
    Task<List<EmailMessage>> GetNewEmailsAsync(CancellationToken cancellationToken = default);
    Task SendReplyAsync(string originalEmailId, string replyContent, CancellationToken cancellationToken = default);
    Task MarkAsProcessedAsync(string emailId, CancellationToken cancellationToken = default);
}

public interface IMockEmailService : IEmailService
{
    Task SeedTestEmailsAsync(List<EmailMessage> testEmails, CancellationToken cancellationToken = default);
    Task ClearEmailsAsync(CancellationToken cancellationToken = default);
}
