using OPC.MaintenanceAPI.DTOs.WorkOrder;
using OPC.MaintenanceAPI.DTOs.Common;
namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IWorkOrderService
    {
        // Bảo trì — Luồng 6B, 7, 8, 9
        Task<List<object>> GetHoSoBaoTriTheoTrangThaiAsync(string? trangThai);   // dòng mới
        Task<object?> GetHoSoBaoTriByIdAsync(int id);
        Task<(bool, string?)> TaoHoSoBaoTriAsync(int maNguoiDungTao, TaoHoSoBaoTriDto dto);
        Task<(bool, string?)> DuyetHoSoBaoTriAsync(int id, int maNguoiDungDuyet, DuyetHoSoDto dto);
        Task<(bool, string?)> PhanCongBaoTriAsync(int maHoSo, PhanCongDto dto);
        Task<(bool, string?)> GhiNhanKetQuaAsync(int maPhanCong, GhiNhanKetQuaDto dto);
        Task<(bool, string?)> XacNhanHoanThanhBaoTriAsync(int maHoSo, XacNhanDto dto);
        Task<List<object>> GetHoSoBaoTriTheoTrangThaiAsync(string? trangThai, int? nam);

        // Sửa chữa — Luồng 10, 11, 15, 16  
        Task<List<object>> GetHoSoSuaChuaTheoTrangThaiAsync(string trangThai);
        Task<object?> GetChiTietHoSoSuaChuaAsync(int id);   
        Task<(bool, string?)> TaoHoSoSuaChuaAsync(TaoHoSoSuaChuaDto dto);
        Task<(bool, string?)> DuyetHoSoSuaChuaAsync(int id, int maNguoiDungDuyet, DuyetHoSoDto dto);
        Task<(bool, string?)> PhanCongSuaChuaAsync(int maHoSo, PhanCongDto dto);
        Task<(bool, string?)> XacNhanHoanThanhSuaChuaAsync(int maHoSo, XacNhanDto dto);
    }
}