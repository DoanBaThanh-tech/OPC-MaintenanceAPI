using OPC.MaintenanceAPI.DTOs.MaintenancePlan;

namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IMaintenancePlanService
    {
        Task<List<ChiTietKeHoachDto>> GetChiTietChuaCoHoSoAsync();
        Task<List<ChiTietKeHoachDto>> GetChiTietTheoKeHoachAsync(int maKeHoach);
        Task<List<KeHoachResponseDto>> GetAllKeHoachAsync();
        Task<List<ChuKyResponseDto>> GetAllChuKyAsync();
        Task<(bool ThanhCong, string? Loi)> TaoYeuCauNgayBaoTriAsync(int maNguoiDungTao, TaoYeuCauNgayBaoTriDto dto);
        Task<List<YeuCauNgayBaoTriResponseDto>> GetYeuCauChoLapKeHoachAsync();
        Task<(bool ThanhCong, string? Loi)> LapKeHoachTuYeuCauAsync(int maNguoiDungTao, LapKeHoachDto dto);
    }
}