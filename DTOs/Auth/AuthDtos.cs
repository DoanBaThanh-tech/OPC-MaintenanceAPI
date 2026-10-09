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
        /// <summary>Có thể để trống — hệ thống ghi «Chưa cập nhật».</summary>
        public string? HoTen { get; set; }
        public string? SoDienThoai { get; set; }
        public DateTime? NgayVaoLam { get; set; }
    }

    // ===== Sửa tài khoản (Admin) — chỉ đổi vai trò =====
    public class CapNhatTaiKhoanDto
    {
        public int MaVaiTro { get; set; }
    }

    // ===== Nhân viên tự cập nhật hồ sơ (không đổi email đăng nhập @opc.com / vai trò) =====
    public class NhanVienUpdateDto
    {
        public string HoTen { get; set; } = null!;
        public string? SoDienThoai { get; set; }
        public DateOnly? NgayVaoLam { get; set; }
        /// <summary>Email thật nhận OTP (Gmail/Outlook…) — khác email đăng nhập @opc.com.</summary>
        public string? EmailLienHe { get; set; }
    }

    // ===== Quên mật khẩu =====
    public class QuenMatKhauRequestDto
    {
        /// <summary>Email đăng nhập công ty (vd. user@opc.com).</summary>
        public string Email { get; set; } = null!;
        /// <summary>Gmail/Outlook cá nhân nhận OTP (bắt buộc nếu email đăng nhập là ảo).</summary>
        public string? EmailNhanOtp { get; set; }
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