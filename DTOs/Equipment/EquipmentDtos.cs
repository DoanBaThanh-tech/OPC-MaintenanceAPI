namespace OPC.MaintenanceAPI.DTOs.Equipment
{
    public class TaoThietBiDto
    {
        public string TenThietBi { get; set; } = null!;
        public string? LoaiThietBi { get; set; }
        public string ViTriLapDat { get; set; } = null!;
        public DateOnly? NgayLapDat { get; set; }
        public int? MaChuKy { get; set; }
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

    /// <summary>
    /// DTO đầy đủ thông tin thiết bị — dùng cho danh sách & chi tiết.
    /// TinhTrangHienTai chuẩn: "Sản xuất" | "Bảo trì" | "Sửa chữa"
    /// </summary>
    public class ThietBiResponseDto
    {
        public int MaThietBi { get; set; }
        public string TenThietBi { get; set; } = null!;
        public string? LoaiThietBi { get; set; }
        public string? ViTriLapDat { get; set; }
        public DateOnly? NgayLapDat { get; set; }
        public string TinhTrangHienTai { get; set; } = "Sản xuất";
        public string? GhiChu { get; set; }
        public DateOnly? NgayBaoTriGanNhat { get; set; }
        public DateOnly? NgayBaoTriTiepTheo { get; set; }
        public int? SoThangDeXuat { get; set; }
        public int MaChuKy { get; set; }
        /// <summary>Tên danh mục = LoaiThietBi của ChuKyBaoTri (fallback LoaiThietBi của thiết bị)</summary>
        public string? TenDanhMuc { get; set; }
        public string? MoTaChuKy { get; set; }
    }

    /// <summary>Nhóm thiết bị theo danh mục để quản lý bảo trì / sửa chữa</summary>
    public class NhomThietBiDto
    {
        public string TenDanhMuc { get; set; } = null!;
        public int MaChuKy { get; set; }
        public int? SoThangChuKy { get; set; }
        public int SoLuong { get; set; }
        public int SoSanXuat { get; set; }
        public int SoBaoTri { get; set; }
        public int SoSuaChua { get; set; }
        public List<ThietBiResponseDto> DanhSach { get; set; } = new();
    }

    public class ThongKeThietBiDto
    {
        public int TongSo { get; set; }
        public int SoSanXuat { get; set; }
        public int SoBaoTri { get; set; }
        public int SoSuaChua { get; set; }
    }

    public class LichSuThietBiDto
    {
        public int MaLichSu { get; set; }
        public DateTime? NgayHoanThanh { get; set; }
        public string? KetQua { get; set; }
        public string? GhiChu { get; set; }
    }

    /// <summary>Hằng số trạng thái thiết bị — dùng thống nhất toàn hệ thống</summary>
    public static class TrangThaiThietBiConst
    {
        public const string SanXuat = "Sản xuất";
        public const string BaoTri = "Bảo trì";
        public const string SuaChua = "Sửa chữa";

        /// <summary>
        /// Chuẩn hóa giá trị cũ ("Hoạt động", "Đang hoạt động", "Hoạt động tốt"...)
        /// về 3 trạng thái nghiệp vụ mới.
        /// </summary>
        public static string ChuanHoa(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return SanXuat;
            var t = raw.Trim().ToLowerInvariant();
            if (t.Contains("bảo trì") || t.Contains("bao tri")) return BaoTri;
            if (t.Contains("sửa chữa") || t.Contains("sua chua")) return SuaChua;
            return SanXuat;
        }
    }
}