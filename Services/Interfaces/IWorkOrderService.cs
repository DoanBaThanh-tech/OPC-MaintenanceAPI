using OPC.MaintenanceAPI.DTOs.WorkOrder;
using OPC.MaintenanceAPI.DTOs.Common;
namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IWorkOrderService
    {
        // Bảo trì — Luồng 6B, 7, 8, 9
        Task<(bool, string?)> TaoHoSoBaoTriAsync(TaoHoSoBaoTriDto dto);
        Task<(bool, string?)> DuyetHoSoBaoTriAsync(int id, DuyetHoSoDto dto);
        Task<(bool, string?)> PhanCongBaoTriAsync(int maHoSo, PhanCongDto dto);
        Task<(bool, string?)> GhiNhanKetQuaAsync(int maPhanCong, GhiNhanKetQuaDto dto);
        Task<(bool, string?)> XacNhanHoanThanhBaoTriAsync(int maHoSo, XacNhanDto dto);

        // Sửa chữa — Luồng 10, 11, 15, 16
        Task<(bool, string?)> TaoHoSoSuaChuaAsync(TaoHoSoSuaChuaDto dto);
        Task<(bool, string?)> DuyetHoSoSuaChuaAsync(int id, DuyetHoSoDto dto);
        Task<(bool, string?)> PhanCongSuaChuaAsync(int maHoSo, PhanCongDto dto);
        Task<(bool, string?)> XacNhanHoanThanhSuaChuaAsync(int maHoSo, XacNhanDto dto);
    }
}