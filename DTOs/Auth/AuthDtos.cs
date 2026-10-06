namespace OPC.MaintenanceAPI.DTOs.Auth
{
    // ===== Đăng nhập =====
    public class DangNhapDto
    {
        public string Email { get; set; } = null!;
        public string MatKhau { get; set; } = null!;
    }

    // ===== Tạo tài khoản (Admin) =====
    public class TaoTaiKhoanDto
    {
        public string Email { get; set; } = null!;
        public string MatKhau { get; set; } = null!;
        public int MaVaiTro { get; set; }
        public string HoTen { get; set; } = null!;
        public string? SoDienThoai { get; set; }
        public string ChucVu { get; set; } = null!;
        public DateTime? NgayVaoLam { get; set; }
    }

    // ===== Sửa tài khoản (Admin) — chỉ email công ty, họ tên, chức vụ =====
    public class CapNhatTaiKhoanDto
    {
        /// <summary>Email công ty (@opc.com). Null = giữ nguyên.</summary>
        public string? Email { get; set; }
        /// <summary>Họ tên chữ cái (có thể để trống). Null = giữ nguyên.</summary>
        public string? HoTen { get; set; }
        public string ChucVu { get; set; } = null!;
    }

    // ===== Nhân viên tự cập nhật hồ sơ =====
    public class NhanVienUpdateDto
    {
        public string HoTen { get; set; } = null!;
        public string? Email { get; set; }
        public string? SoDienThoai { get; set; }
        public string? ChucVu { get; set; }
        public DateOnly? NgayVaoLam { get; set; }
        public string? TrangThai { get; set; }   // "Đang làm việc" / "Đã nghỉ việc"
    }

    // ===== Quên mật khẩu =====
    public class QuenMatKhauRequestDto
    {
        public string Email { get; set; } = null!;
    }

    public class XacNhanOtpDto
    {
        public string Email { get; set; } = null!;
        public string MaOTP { get; set; } = null!;
    }

    public class DatLaiMatKhauDto
    {
        public string Email { get; set; } = null!;
        public string MatKhauMoi { get; set; } = null!;
    }
}