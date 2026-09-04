using OPC.MaintenanceAPI.DTOs.Inventory;
using OPC.MaintenanceAPI.DTOs.Common;
namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IInventoryService
    {
        Task<(bool ThanhCong, string? Loi, bool DuVatTu)> KiemTraTonKhoAsync(List<KiemTraVatTuDto> danhSach);
        Task<(bool, string?)> TaoYeuCauVatTuAsync(TaoYeuCauVatTuDto dto);
        Task<(bool, string?)> DuyetYeuCauVatTuAsync(int id, DuyetHoSoDto dto);
        Task<(bool, string?)> NhapKhoAsync(NhapKhoDto dto);
        Task<(bool, string?)> XuatKhoAsync(int maYeuCauVatTu);
    }
}