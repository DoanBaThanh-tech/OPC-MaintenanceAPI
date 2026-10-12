namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface IThongBaoService
    {
        Task<object> LayDanhSachAsync(int maNguoiDung, bool? chiChuaDoc = null);
        Task<(bool ok, string? loi)> DanhDauDaDocAsync(int maNguoiDung, int maThongBao);
        Task<(bool ok, string? loi)> DanhDauTatCaDaDocAsync(int maNguoiDung);
        Task<(bool ok, string? loi)> DanhDauTheoHoSoAsync(int maNguoiDung, int? maHoSoBaoTri, int? maHoSoSuaChua);
        Task TaoThongBaoPhanCongAsync(
            IEnumerable<int> maNhanVienNhans,
            string loai,
            string tenThietBi,
            int? maHoSoBaoTri,
            int? maHoSoSuaChua);
    }
}
