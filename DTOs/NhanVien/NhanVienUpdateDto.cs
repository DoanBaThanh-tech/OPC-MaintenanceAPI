namespace OPC.MaintenanceAPI.DTOs.NhanVien
{
    public class NhanVienUpdateDto
    {
        public int MaNhanVien { get; set; }
        public string HoTen { get; set; } = null!;
        public string? Email { get; set; }
        public string? SoDienThoai { get; set; }
        public string? ChucVu { get; set; }
        public DateOnly? NgayVaoLam { get; set; }
        public string? TrangThai { get; set; }   // "Đang làm việc" / "Đã nghỉ việc"
    }
}