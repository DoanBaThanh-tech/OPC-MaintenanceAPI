using OPC.MaintenanceAPI.DTOs.MaintenancePlan;

namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IMaintenancePlanService
    {
        Task<(bool ThanhCong, string? Loi)> LapKeHoachAsync(LapKeHoachDto dto);
        Task<List<ChiTietKeHoachDto>> GetChiTietChuaCoHoSoAsync();
    }
}