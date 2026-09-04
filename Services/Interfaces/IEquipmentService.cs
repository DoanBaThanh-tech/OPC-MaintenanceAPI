using OPC.MaintenanceAPI.DTOs.Equipment;

namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IEquipmentService
    {
        Task<List<ThietBiResponseDto>> GetAllAsync();
        Task<ThietBiResponseDto?> GetByIdAsync(int id);
        Task<(bool ThanhCong, string? Loi)> TaoMoiAsync(TaoThietBiDto dto);
        Task<(bool ThanhCong, string? Loi)> CapNhatAsync(int id, CapNhatThietBiDto dto);
        Task<List<LichSuThietBiDto>> GetLichSuAsync(int maThietBi);
    }
}