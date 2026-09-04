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
    }
}