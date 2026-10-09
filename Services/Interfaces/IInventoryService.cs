using OPC.MaintenanceAPI.DTOs.Inventory;
using OPC.MaintenanceAPI.DTOs.Common;
namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IInventoryService
    {
        Task<(bool ThanhCong, string? Loi, bool DuVatTu)> KiemTraTonKhoAsync(List<KiemTraVatTuDto> danhSach);
        Task<(bool, string?)> TaoYeuCauVatTuAsync(TaoYeuCauVatTuDto dto);
        Task<(bool, string?)> DuyetYeuCauVatTuAsync(int id, int maNguoiDungDuyet, DuyetHoSoDto dto);
        Task<(bool, string?)> NhapKhoAsync(NhapKhoDto dto);
        Task<(bool, string?)> XuatKhoAsync(int maYeuCauVatTu, int maNhanVienGiaoDich);
        Task<bool> DaXuatChoYeuCauAsync(int maYeuCauVatTu);

        Task<List<VatTuDto>> GetDanhSachVatTuAsync();
        Task<(bool, string?, HoSoVatTuResponseDto?)> TaoHoSoSuDungVatTuAsync(TaoHoSoVatTuDto dto, int maNguoiDung);
        /// <summary>Cập nhật chi tiết vật tư (số lượng / thêm / xóa dòng) khi hồ sơ còn Chờ gửi.</summary>
        Task<(bool, string?, HoSoVatTuResponseDto?)> CapNhatHoSoSuDungVatTuAsync(int id, TaoHoSoVatTuDto dto, int maNguoiDung);
        Task<HoSoVatTuResponseDto?> GetHoSoVatTuTheoCongViecAsync(int? maHoSoBaoTri, int? maHoSoSuaChua);
        Task<List<HoSoVatTuResponseDto>> GetDanhSachHoSoVatTuAsync(string? trangThai = null);
        Task<HoSoVatTuResponseDto?> GetHoSoVatTuByIdAsync(int id);
        Task<(bool, string?)> GuiHoSoVatTuChoGiamDocAsync(int id);
        Task<(bool, string?)> XacNhanHoSoVatTuAsync(int id, int maNguoiDung);
        Task<List<BuocQuyTrinhDto>> GetQuyTrinhThietBiAsync(int maThietBi, string loaiCongViec);
        Task<List<object>> GetDanhSachQuyTrinhAsync(string? loaiCongViec);
        Task<(bool ok, string? loi)> CapNhatQuyTrinhThietBiAsync(CapNhatQuyTrinhThietBiDto dto);
    }
}
