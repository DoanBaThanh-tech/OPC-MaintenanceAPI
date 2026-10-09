namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IEmailSender
    {
        /// <summary>Gửi email HTML/text. Trả về null nếu OK, hoặc message lỗi.</summary>
        Task<string?> SendAsync(string toEmail, string subject, string bodyHtml, string? bodyText = null);
        bool IsConfigured { get; }
    }
}
