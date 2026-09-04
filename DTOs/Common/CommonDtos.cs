namespace OPC.MaintenanceAPI.DTOs.Common
{
    // Dùng chung cho: duyệt hồ sơ bảo trì, duyệt hồ sơ sửa chữa, duyệt hồ sơ yêu cầu vật tư
    public class DuyetHoSoDto
    {
        public bool Duyet { get; set; }
        public string? LyDoTuChoi { get; set; }
        public int MaNhanVienDuyet { get; set; }
    }
}