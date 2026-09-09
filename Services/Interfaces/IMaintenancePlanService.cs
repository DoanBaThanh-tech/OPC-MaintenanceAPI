using OPC.MaintenanceAPI.DTOs.MaintenancePlan;

namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IMaintenancePlanService
    {
        Task<List<ChiTietKeHoachDto>> GetChiTietChuaCoHoSoAsync();
        Task<List<ChiTietKeHoachDto>> GetChiTietTheoKeHoachAsync(int maKeHoach);
        Task<List<KeHoachResponseDto>> GetAllKeHoachAsync();
        Task<List<ChuKyResponseDto>> GetAllChuKyAsync();
        Task<(bool ThanhCong, string? Loi)> ThemLanBaoTriAsync(int maKeHoach, ThemLanBaoTriDto dto);
        // Gộp bước "đăng ký ngày" + "lập kế hoạch" cũ thành 1 hàm duy nhất
        Task<(bool ThanhCong, string? Loi)> TaoKeHoachAsync(int maNguoiDungTao, TaoKeHoachDto dto);
    }
}