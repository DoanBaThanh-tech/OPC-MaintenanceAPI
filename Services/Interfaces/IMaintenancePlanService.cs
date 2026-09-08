using OPC.MaintenanceAPI.DTOs.MaintenancePlan;

namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IMaintenancePlanService
    {
        Task<(bool, string?)> LapKeHoachAsync(int maNguoiDungTao, LapKeHoachDto dto);
        Task<List<ChiTietKeHoachDto>> GetChiTietChuaCoHoSoAsync();
        Task<List<ChiTietKeHoachDto>> GetChiTietTheoKeHoachAsync(int maKeHoach);
        Task<List<KeHoachResponseDto>> GetAllKeHoachAsync();
        Task<List<ChuKyResponseDto>> GetAllChuKyAsync();
    }
}