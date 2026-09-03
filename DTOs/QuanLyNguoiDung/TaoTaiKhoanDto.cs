namespace OPC.MaintenanceAPI.DTOs.QuanLyNguoiDung
{
    public class TaoTaiKhoanDto
    {
        public string Email { get; set; } = null!;
        public string MatKhau { get; set; } = null!;
        public int MaVaiTro { get; set; }
        public string HoTen { get; set; } = null!;
        public string? SoDienThoai { get; set; }
    }
}