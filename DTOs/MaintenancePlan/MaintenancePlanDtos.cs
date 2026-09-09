namespace OPC.MaintenanceAPI.DTOs.MaintenancePlan
{
    public class LapKeHoachDto
    {
        public int MaYeuCauNgayBaoTri { get; set; }
        public int Nam { get; set; }
        // Không nhận MaNhanVienLap — server tự lấy từ JWT
        public List<ChiTietKeHoachInputDto> ThietBiDuocChon { get; set; } = new();
    }

    public class TaoYeuCauNgayBaoTriDto
    {
        public int MaThietBi { get; set; }
        public int Nam { get; set; }
        public DateOnly NgayBaoTri { get; set; }
    }

    public class YeuCauNgayBaoTriResponseDto
    {
        public int MaYeuCauNgayBaoTri { get; set; }
        public int MaThietBi { get; set; }
        public string TenThietBi { get; set; } = null!;
        public string? LoaiThietBi { get; set; }
        public int MaChuKy { get; set; }
        public int SoThangChuKy { get; set; }
        public int Nam { get; set; }
        public DateOnly NgayBaoTri { get; set; }
        public string TrangThai { get; set; } = null!;
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