namespace OPC.MaintenanceAPI.DTOs.System
{
    // ===== VaiTro =====
    public class VaiTroDto
    {
        public string TenVaiTro { get; set; } = null!;
        public int CapDoQuyen { get; set; }
    }

    // ===== Phân quyền =====
    public class ChucNangQuyenDto
    {
        public int MaChucNang { get; set; }
        public string TenChucNang { get; set; } = null!;
        public string NhomChucNang { get; set; } = null!;
        public bool DuocXem { get; set; }
        public bool DuocTao { get; set; }
        public bool DuocSua { get; set; }
        public bool DuocDuyet { get; set; }
    }

    public class CapNhatPhanQuyenDto
    {
        public List<QuyenChucNangInputDto> DanhSachQuyen { get; set; } = new();
    }

    public class QuyenChucNangInputDto
    {
        public int MaChucNang { get; set; }
        public bool DuocXem { get; set; }
        public bool DuocTao { get; set; }
        public bool DuocSua { get; set; }
        public bool DuocDuyet { get; set; }
    }

    // ===== Nhật ký hệ thống =====
    public class NhatKyFilterDto
    {
        public string? TuKhoa { get; set; }
        public string? PhuongThucHTTP { get; set; }
        public DateTime? TuNgay { get; set; }
        public DateTime? DenNgay { get; set; }
    }
}