using OPC.MaintenanceAPI.DTOs.WorkOrder;
using OPC.MaintenanceAPI.DTOs.Common;
namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IWorkOrderService
    {
        // Bảo trì
        Task<List<object>> GetHoSoBaoTriTheoTrangThaiAsync(string? trangThai, int? nam);
        Task<List<int>> GetCacNamCoHoSoBaoTriAsync(string? trangThai);
        Task<object?> GetHoSoBaoTriByIdAsync(int id);
        Task<(bool, string?)> TaoHoSoBaoTriAsync(int maNguoiDungTao, TaoHoSoBaoTriDto dto);
        Task<(bool, string?)> DuyetHoSoBaoTriAsync(int id, int maNguoiDungDuyet, DuyetHoSoDto dto);
        Task<(bool, string?)> PhanCongBaoTriAsync(int maHoSo, int maNguoiDungPhanCong, PhanCongDto dto);
        Task<List<object>> GetLichSuPhanCongAsync();
        Task<(bool, string?)> GhiNhanKetQuaAsync(int maPhanCong, GhiNhanKetQuaDto dto);
        Task<(bool, string?)> XacNhanHoanThanhBaoTriAsync(int maHoSo, XacNhanDto dto);

        // Sửa chữa
        Task<List<object>> GetHoSoSuaChuaTheoTrangThaiAsync(string trangThai);
        Task<object?> GetChiTietHoSoSuaChuaAsync(int id);
        Task<(bool, string?)> TaoHoSoSuaChuaAsync(TaoHoSoSuaChuaDto dto);
        Task<(bool, string?)> DuyetHoSoSuaChuaAsync(int id, int maNguoiDungDuyet, DuyetHoSoDto dto);
        Task<(bool, string?)> PhanCongSuaChuaAsync(int maHoSo, PhanCongDto dto);
        Task<(bool, string?)> XacNhanHoanThanhSuaChuaAsync(int maHoSo, XacNhanDto dto);
    }
}