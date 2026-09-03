namespace OPC.MaintenanceAPI.DTOs.ChuKyBaoTri
{
    public class ChuKyBaoTriCreateDto
    {
        public string LoaiThietBi { get; set; } = null!;
        public int SoThangChuKyDeXuat { get; set; }
        public string? MoTa { get; set; }
    }
}