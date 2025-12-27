using MedExtractEval.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace MedExtractEval.Components.Account
{
    // Remove the "else if (EmailSender is IdentityNoOpEmailSender)" block from RegisterConfirmation.razor after updating with a real implementation.
    internal sealed class SmtpEmailSender(IOptions<EmailSettings> options) : IEmailSender<ApplicationUser>
    {
        private readonly EmailSettings smtpSettings = options.Value;
        //private readonly IEmailSender emailSender = new NoOpEmailSender();

        public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
        {
            await SendEmailAsync(user.UserName ?? string.Empty, email, "Confirm your email", $"Please confirm your account by <a href='{confirmationLink}'>clicking here</a>.");
        }

        public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
        {
            await SendEmailAsync(user.UserName ?? string.Empty, email, "Reset your password", $"Please reset your password by <a href='{resetLink}'>clicking here</a>.");
        }

        public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
        {
            await SendEmailAsync(user.UserName ?? string.Empty, email, "Reset your password", $"Please reset your password using the following code: {resetCode}");
        }

        private async Task SendEmailAsync(string username, string email, string subject, string body)
        {
            // 构造邮件消息
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(smtpSettings.SenderName, smtpSettings.SenderEmail));
            message.To.Add(new MailboxAddress(username, email));
            message.Subject = subject;

            var builder = new BodyBuilder
            {
                HtmlBody = body
            };
            message.Body = builder.ToMessageBody();

            // 使用 MailKit 的 SmtpClient 发送邮件
            using var client = new SmtpClient();
            await client.ConnectAsync(smtpSettings.SmtpServer, smtpSettings.SmtpPort, SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(smtpSettings.UserName, smtpSettings.Password);
            var x = await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

    }

    public class EmailSettings
    {
        public string ClientUrl { get; set; } = "https://localhost:5001";

        public string SmtpServer { get; set; } = "smtp.example.com";

        public int SmtpPort { get; set; } = 999;

        public string SenderEmail { get; set; } = "noreply@example.com";

        public string SenderName { get; set; } = "YourAppName";

        public string UserName { get; set; } = "smtp_username";

        public string Password { get; set; } = "smtp_password";
    }
}
