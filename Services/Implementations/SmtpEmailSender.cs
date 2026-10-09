using System.Net;
using System.Net.Mail;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    /// <summary>Gửi email qua SMTP (Gmail App Password / SMTP công ty).</summary>
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
        {
            _config = config;
            _logger = logger;
        }

        public bool IsConfigured
        {
            get
            {
                var host = _config["Email:SmtpHost"];
                var user = _config["Email:UserName"];
                var pass = _config["Email:Password"];
                return !string.IsNullOrWhiteSpace(host)
                       && !string.IsNullOrWhiteSpace(user)
                       && !string.IsNullOrWhiteSpace(pass)
                       && !pass.Contains("THAY-", StringComparison.OrdinalIgnoreCase)
                       && !pass.Contains("your-", StringComparison.OrdinalIgnoreCase);
            }
        }

        public async Task<string?> SendAsync(string toEmail, string subject, string bodyHtml, string? bodyText = null)
        {
            if (!IsConfigured)
                return "Chưa cấu hình SMTP (Email:SmtpHost / UserName / Password).";

            try
            {
                var host = _config["Email:SmtpHost"]!;
                var port = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
                var user = _config["Email:UserName"]!;
                var pass = _config["Email:Password"]!;
                var from = _config["Email:From"] ?? user;
                var fromName = _config["Email:FromName"] ?? "OPC Maintenance";
                var enableSsl = !string.Equals(_config["Email:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase);

                using var msg = new MailMessage
                {
                    From = new MailAddress(from, fromName),
                    Subject = subject,
                    Body = bodyHtml,
                    IsBodyHtml = true
                };
                msg.To.Add(new MailAddress(toEmail.Trim()));
                if (!string.IsNullOrWhiteSpace(bodyText))
                    msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(bodyText, null, "text/plain"));

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = enableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(user, pass),
                    Timeout = 30000
                };

                await client.SendMailAsync(msg);
                _logger.LogInformation("Đã gửi email tới {To} — {Subject}", toEmail, subject);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gửi email tới {To} thất bại", toEmail);
                return "Không gửi được email. Kiểm tra SMTP / App Password Gmail hoặc hộp thư người nhận.";
            }
        }
    }
}
