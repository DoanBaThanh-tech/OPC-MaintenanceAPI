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
        Task<List<object>> GetLichSuPheDuyetAsync(string? loai, int? nam);
        Task<List<int>> GetCacNamCoLichSuPheDuyetAsync(string? loai);
        Task<(bool, string?)> HuyPhanCongAsync(int maPhanCong, int maNguoiDung);
        Task<(bool, string?)> GhiNhanKetQuaAsync(int maPhanCong, GhiNhanKetQuaDto dto);
        Task<(bool, string?)> XacNhanHoanThanhBaoTriAsync(int maHoSo, XacNhanDto dto);
        Task<(bool, string?)> NhanVienXacNhanBaoTriAsync(int maHoSo, int maNguoiDung);
        Task<(bool, string?)> NhanVienTuChoiBaoTriAsync(int maHoSo, int maNguoiDung, TuChoiNhanViecDto dto);
        Task<(bool, string?)> CapNhatHoSoBaoTriBiTuChoiAsync(int id, CapNhatHoSoBaoTriDto dto);

        /// Danh sách yêu cầu được phân công cho NVKT đang đăng nhập (Bảo trì / Sửa chữa)
        Task<List<object>> GetYeuCauCuaNhanVienAsync(int maNguoiDung, string? loai = null, string? trangThaiPhanCong = null);
        /// Danh sách yêu cầu đã Xác nhận (dùng cho combobox Kết quả thực hiện)
        Task<List<object>> GetYeuCauDaXacNhanAsync(int maNguoiDung, string? loai = null);

        // Sửa chữa
        Task<List<object>> GetHoSoSuaChuaTheoTrangThaiAsync(string trangThai);
        Task<object?> GetChiTietHoSoSuaChuaAsync(int id);
        Task<(bool, string?)> TaoHoSoSuaChuaAsync(TaoHoSoSuaChuaDto dto);
        Task<(bool, string?)> DuyetHoSoSuaChuaAsync(int id, int maNguoiDungDuyet, DuyetHoSoDto dto);
        Task<(bool, string?)> PhanCongSuaChuaAsync(int maHoSo, PhanCongDto dto);
        Task<(bool, string?)> XacNhanHoanThanhSuaChuaAsync(int maHoSo, XacNhanDto dto);
        Task<(bool, string?)> NhanVienXacNhanSuaChuaAsync(int maHoSo, int maNguoiDung);
        Task<(bool, string?)> NhanVienTuChoiSuaChuaAsync(int maHoSo, int maNguoiDung, TuChoiNhanViecDto dto);

        // Yêu cầu bảo trì: Tổ trưởng cơ điện tạo → Xưởng (TTSX) xác nhận/từ chối
        Task<(bool, string?)> TaoYeuCauBaoTriAsync(int maNguoiDung, TaoYeuCauBaoTriDto dto);
        Task<List<object>> GetYeuCauBaoTriAsync(string? trangThai, int? nam, int? thang);
        Task<(bool, string?)> XacNhanYeuCauBaoTriAsync(int maYeuCau, int maNguoiDung, XacNhanYeuCauBaoTriDto dto);
        /// Sửa yêu cầu bị từ chối → gửi lại (trạng thái về Chờ xác nhận)
        Task<(bool, string?)> SuaYeuCauBaoTriAsync(int maYeuCau, int maNguoiDung, SuaYeuCauBaoTriDto dto);
        /// Danh sách YC đã xác nhận — dùng khi Tổ trưởng cơ điện tạo kế hoạch/hồ sơ
        Task<List<object>> GetYeuCauDaXacNhanDeTaoHoSoAsync(int? nam, int? thang);
    }
}
