namespace OPC.MaintenanceAPI.DTOs.MaintenancePlan
{
    // Gộp 2 bước cũ (TaoYeuCauNgayBaoTri + LapKeHoach) thành 1 request duy nhất.
    // Khớp đúng với body mà Flutter (MaintenancePlanService.taoKeHoach) đang gửi lên.
    public class TaoKeHoachDto
    {
        public int MaChuKy { get; set; }
        public int Nam { get; set; }
        public List<ChiTietKeHoachInputDto> ThietBiDuocChon { get; set; } = new();
    }

    public class ChiTietKeHoachInputDto
    {
        public int MaThietBi { get; set; }
        public DateOnly NgayDuKienBaoTri { get; set; }
    }

    public class ChiTietKeHoachDto
    {
        public int MaChiTietKeHoach { get; set; }
        public int MaThietBi { get; set; }
        public string? TenThietBi { get; set; }
        public DateOnly NgayDuKienBaoTri { get; set; }
        public int? MaHoSoBaoTri { get; set; }
    }

    public class KeHoachResponseDto
    {
        public int MaKeHoach { get; set; }
        public int MaChuKy { get; set; }
        public string? TenChuKy { get; set; }
        public string? TenThietBi { get; set; }
        public int Nam { get; set; }
        public string? TenNhanVienLap { get; set; }
        public DateOnly NgayLapKeHoach { get; set; }
        public string TrangThai { get; set; } = null!;
        public int SoThietBi { get; set; }
    }

    public class ChuKyResponseDto
    {
        public int MaChuKy { get; set; }
        public string? LoaiThietBi { get; set; }
        public int SoThangChuKyDeXuat { get; set; }
        public string? MoTa { get; set; }
    }

    public class ThietBiGoiYDto
    {
        public int MaThietBi { get; set; }
        public string TenThietBi { get; set; } = null!;
        public string? LoaiThietBi { get; set; }
        public DateOnly? NgayBaoTriGanNhat { get; set; }
        public DateOnly? NgayGoiY { get; set; }
        public int? SoThangChuKy { get; set; }
    }
}