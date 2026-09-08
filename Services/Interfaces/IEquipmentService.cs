using OPC.MaintenanceAPI.DTOs.Equipment;

namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IEquipmentService
    {
        Task<ThietBiResponseDto?> GetByIdAsync(int id);
        Task<(bool ThanhCong, string? Loi)> TaoMoiAsync(TaoThietBiDto dto);
        Task<(bool ThanhCong, string? Loi)> CapNhatAsync(int id, CapNhatThietBiDto dto);
        Task<List<LichSuThietBiDto>> GetLichSuAsync(int maThietBi);
        Task<List<ThietBiResponseDto>> GetAllAsync(int? maChuKy = null);
    }
}