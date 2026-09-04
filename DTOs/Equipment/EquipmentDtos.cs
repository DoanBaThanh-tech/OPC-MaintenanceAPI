namespace OPC.MaintenanceAPI.DTOs.Equipment
{
    public class TaoThietBiDto
    {
        public string TenThietBi { get; set; } = null!;
        public string? LoaiThietBi { get; set; }
        public string ViTriLapDat { get; set; } = null!;
        public DateOnly? NgayLapDat { get; set; }
        public string? GhiChu { get; set; }
    }

    public class CapNhatThietBiDto
    {
        public string? TenThietBi { get; set; }
        public string? LoaiThietBi { get; set; }
        public string? ViTriLapDat { get; set; }
        public DateOnly? NgayLapDat { get; set; }
        public string? GhiChu { get; set; }
    }

    public class ThietBiResponseDto
    {
        public int MaThietBi { get; set; }
        public string TenThietBi { get; set; } = null!;
        public string? LoaiThietBi { get; set; }
        public string? ViTriLapDat { get; set; }
        public string? TinhTrangHienTai { get; set; }
        public DateOnly? NgayBaoTriGanNhat { get; set; }
        public DateOnly? NgayBaoTriTiepTheo { get; set; }
    }

    public class LichSuThietBiDto
    {
        public int MaLichSu { get; set; }
        public DateTime? NgayHoanThanh { get; set; }
        public string? KetQua { get; set; }
    }
}