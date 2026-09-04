namespace OPC.MaintenanceAPI.DTOs.WorkOrder
{
    // ===== Tạo hồ sơ =====
    public class TaoHoSoBaoTriDto
    {
        public int MaThietBi { get; set; }
        public int MaNhanVienTao { get; set; }
        public string? NoiDungCongViec { get; set; }
        public string? ThoiGianDuKien { get; set; }
        public bool GuiDuyet { get; set; }   // true = Gửi duyệt, false = Lưu nháp
    }

    public class TaoHoSoSuaChuaDto
    {
        public int MaThietBi { get; set; }
        public int MaNhanVienTao { get; set; }
        public string MoTaHuHong { get; set; } = null!;
        public string? PhuongAnSuaChua { get; set; }
        public bool GuiDuyet { get; set; }
    }

    
    // ===== Phân công =====
    public class PhanCongDto
    {
        public int MaNhanVienThucHien { get; set; }
        public int MaNhanVienPhanCong { get; set; }
        public DateTime NgayBatDauDuKien { get; set; }
        public DateTime NgayKetThucDuKien { get; set; }
    }

    // ===== Ghi nhận kết quả =====
    public class GhiNhanKetQuaDto
    {
        public int MaNhanVienGhiNhan { get; set; }
        public string SoLieuGhiNhan { get; set; } = null!;
        public string? HinhAnh { get; set; }
        public string? GhiChu { get; set; }
    }

    // ===== Xác nhận đóng hồ sơ =====
    public class XacNhanDto
    {
        public bool Dat { get; set; }   // true = Đạt yêu cầu, false = Chưa đạt
    }
}