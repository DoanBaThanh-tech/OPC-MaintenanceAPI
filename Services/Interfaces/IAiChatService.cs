namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IAiChatService
    {
        Task<(bool ok, string? traLoi, string? loi)> ChatAsync(
            int maNguoiDung,
            string vaiTro,
            string hoTen,
            string cauHoi,
            IReadOnlyList<(string role, string content)>? lichSu,
            CancellationToken ct = default);

        Task<(bool online, string? model, string? loi)> KiemTraAsync(CancellationToken ct = default);
    }
}
