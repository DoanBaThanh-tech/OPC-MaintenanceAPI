namespace OPC.MaintenanceAPI.DTOs.MaintenancePlan
{
    public class LapKeHoachDto
    {
        public int Nam { get; set; }
        public int MaNhanVienLap { get; set; }
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
        public string? TenThietBi { get; set; }
        public DateOnly NgayDuKienBaoTri { get; set; }
    }
    // Cho trang danh sách kế hoạch
    public class KeHoachResponseDto
    {
        public int MaKeHoach { get; set; }
        public int Nam { get; set; }
        public string? TenNhanVienLap { get; set; }
        public DateOnly NgayLapKeHoach { get; set; }
        public string TrangThai { get; set; } = null!;
        public int SoThietBi { get; set; }   // tổng số thiết bị trong kế hoạch, hiển thị nhanh trên list
    }

    // Cho màn hình "+" tạo kế hoạch mới — gợi ý ngày dự kiến theo chu kỳ đã cấu hình
    public class ThietBiGoiYDto
    {
        public int MaThietBi { get; set; }
        public string TenThietBi { get; set; } = null!;
        public string? LoaiThietBi { get; set; }
        public DateOnly? NgayBaoTriGanNhat { get; set; }
        public DateOnly? NgayGoiY { get; set; }   // = NgayBaoTriTiepTheo đã tính sẵn từ trước
        public int? SoThangChuKy { get; set; }    // hiển thị cho người dùng biết "3 tháng/lần"
    }
}