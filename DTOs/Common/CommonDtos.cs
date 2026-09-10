namespace OPC.MaintenanceAPI.DTOs.Common
{
    public class DuyetHoSoDto
    {
        public string QuyetDinh { get; set; } = null!;  // "Duyệt" hoặc "Từ chối"
        public string? LyDo { get; set; }               // bắt buộc khi Từ chối
        public string RowVersion { get; set; } = null!; // base64 - dùng cho Optimistic Concurrency Control
    }
}