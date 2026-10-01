using OPC.MaintenanceAPI.DTOs.MaintenancePlan;

namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IMaintenancePlanService
    {
        Task<List<ChiTietKeHoachDto>> GetChiTietChuaCoHoSoAsync();
        Task<List<ChiTietKeHoachDto>> GetChiTietTheoKeHoachAsync(int maKeHoach);
        Task<List<KeHoachResponseDto>> GetAllKeHoachAsync();
        Task<List<ChuKyResponseDto>> GetAllChuKyAsync();
        Task<List<int>> GetDanhSachNamDaLapAsync();
        Task<(bool, string?)> TaoNamMoiAsync(int maNguoiDungTao, TaoNamMoiDto dto);
        Task<(bool, string?)> ThemThietBiVaoNamAsync(int maNguoiDungTao, ThemThietBiVaoNamDto dto);
        Task<(bool ThanhCong, string? Loi)> ThemLanBaoTriAsync(int maKeHoach, ThemLanBaoTriDto dto);
        // Gộp bước "đăng ký ngày" + "lập kế hoạch" cũ thành 1 hàm duy nhất
        Task<(bool ThanhCong, string? Loi)> TaoKeHoachAsync(int maNguoiDungTao, TaoKeHoachDto dto);
        /// <summary>Danh sách thiết bị đến hạn / trễ hạn BT trong tháng (chưa có HS).</summary>
        Task<List<HangChoDenHanDto>> GetHangChoDenHanAsync(int nam, int thang);
        /// <summary>Tạo HS bảo trì hàng loạt cho nhiều thiết bị trong 1 tháng.</summary>
        Task<(bool ok, string? loi, TaoHangLoatBaoTriKetQuaDto? ketQua)> TaoHangLoatBaoTriAsync(
            int maNguoiDungTao, TaoHangLoatBaoTriDto dto);
    }
}