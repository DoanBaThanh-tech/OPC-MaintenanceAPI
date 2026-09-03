namespace OPC.MaintenanceAPI.DTOs.ThietBi
{
    public class ThietBiCreateDto
    {
        public int MaChuKy { get; set; }
        public string TenThietBi { get; set; } = null!;
        public string? LoaiThietBi { get; set; }
        public string? ViTriLapDat { get; set; }
        public DateOnly NgayLapDat { get; set; }
        public string? TinhTrangHienTai { get; set; }
        public string? GhiChu { get; set; }
    }
}