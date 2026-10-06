using OPC.MaintenanceAPI.DTOs.Auth;

namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public class AuthResult
    {
        public bool ThanhCong { get; set; }
        public string? Message { get; set; }
        public object? Data { get; set; }
    }

    public interface IAuthService
    {
        Task<List<object>> GetAllAsync();
        Task<AuthResult> GetByIdAsync(int id);
        Task<AuthResult> DangNhapAsync(DangNhapDto dto);
        Task<AuthResult> TaoTaiKhoanAsync(TaoTaiKhoanDto dto);
        Task<AuthResult> CapNhatTaiKhoanAsync(int id, CapNhatTaiKhoanDto dto);
        Task<AuthResult> KichHoatAsync(int id);
        Task<AuthResult> KhoaAsync(int id);
        Task<AuthResult> YeuCauOtpAsync(QuenMatKhauRequestDto dto);
        Task<AuthResult> XacNhanOtpAsync(XacNhanOtpDto dto);
        Task<AuthResult> DatLaiMatKhauAsync(DatLaiMatKhauDto dto);
        /// <summary>Hồ sơ cá nhân của user đang đăng nhập.</summary>
        Task<AuthResult> GetHoSoCaNhanAsync(int maNguoiDung);
        /// <summary>Tự cập nhật họ tên / SĐT / chức vụ / ngày vào làm — không đổi email, vai trò.</summary>
        Task<AuthResult> CapNhatHoSoCaNhanAsync(int maNguoiDung, NhanVienUpdateDto dto);
    }
}