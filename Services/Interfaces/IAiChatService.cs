namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IAiChatService
    {
        /// <summary>
        /// Gửi tin nhắn tới AI local (Ollama). Chỉ trả lời trong phạm vi vai trò.
        /// </summary>
        Task<(bool ok, string? traLoi, string? loi)> ChatAsync(
            string vaiTro,
            string hoTen,
            string cauHoi,
            IReadOnlyList<(string role, string content)>? lichSu,
            CancellationToken ct = default);

        /// <summary>Kiểm tra Ollama có online không.</summary>
        Task<(bool online, string? model, string? loi)> KiemTraAsync(CancellationToken ct = default);
    }
}
