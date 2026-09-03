namespace OPC.MaintenanceAPI.DTOs.HoSoBaoTri
{
    public class HoSoBaoTriApproveDto
    {
        public string QuyetDinh { get; set; } = null!;   // "Duyệt" / "Từ chối"
        public string? LyDo { get; set; }
    }
}