using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.DTOs.MaintenancePlan;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public class MaintenancePlanService : IMaintenancePlanService
    {
        private readonly IMaintenancePlanRepository _repo;
        private readonly INhanVienRepository _nhanVienRepo;

        public MaintenancePlanService(IMaintenancePlanRepository repo, INhanVienRepository nhanVienRepo)
        {
            _repo = repo;
            _nhanVienRepo = nhanVienRepo;
        }

        // maNguoiDungTao lấy từ JWT (Controller truyền vào) — không tin dữ liệu client gửi
        public async Task<(bool, string?)> LapKeHoachAsync(int maNguoiDungTao, LapKeHoachDto dto)
        {
            if (dto.ThietBiDuocChon == null || dto.ThietBiDuocChon.Count == 0)
                return (false, "Vui lòng chọn ít nhất 1 thiết bị.");

            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungTao);
            if (nhanVien == null) return (false, "Không xác định được người lập kế hoạch.");

            var keHoach = new KeHoachBaoTri
            {
                MaChuKy = dto.MaChuKy,
                Nam = dto.Nam,
                MaNhanVienLap = nhanVien.MaNhanVien,
                NgayLapKeHoach = DateOnly.FromDateTime(DateTime.Now),
                TrangThai = "Đang lập"
            };
            await _repo.AddKeHoachAsync(keHoach);
            await _repo.SaveChangesAsync();

            var chiTiets = dto.ThietBiDuocChon.Select(t => new ChiTietKeHoachBaoTri
            {
                MaKeHoach = keHoach.MaKeHoach,
                MaThietBi = t.MaThietBi,
                NgayDuKienBaoTri = t.NgayDuKienBaoTri
            });
            await _repo.AddChiTietRangeAsync(chiTiets);
            await _repo.SaveChangesAsync();

            return (true, null);
        }

        public async Task<List<ChiTietKeHoachDto>> GetChiTietChuaCoHoSoAsync() =>
            (await _repo.GetChiTietChuaCoHoSoAsync()).Select(MapChiTiet).ToList();

        public async Task<List<ChiTietKeHoachDto>> GetChiTietTheoKeHoachAsync(int maKeHoach) =>
            (await _repo.GetChiTietTheoKeHoachAsync(maKeHoach)).Select(MapChiTiet).ToList();

        public async Task<List<KeHoachResponseDto>> GetAllKeHoachAsync() =>
            (await _repo.GetAllKeHoachAsync()).Select(k => new KeHoachResponseDto
            {
                MaKeHoach = k.MaKeHoach,
                MaChuKy = k.MaChuKy,
                TenChuKy = k.MaChuKyNavigation?.LoaiThietBi,
                Nam = k.Nam,
                TenNhanVienLap = k.MaNhanVienLapNavigation?.HoTen,
                NgayLapKeHoach = k.NgayLapKeHoach,
                TrangThai = k.TrangThai ?? "Chưa xác định",
                SoThietBi = k.ChiTietKeHoachBaoTris?.Count ?? 0
            }).ToList();

        public async Task<List<ChuKyResponseDto>> GetAllChuKyAsync() =>
            (await _repo.GetAllChuKyAsync()).Select(c => new ChuKyResponseDto
            {
                MaChuKy = c.MaChuKy,
                LoaiThietBi = c.LoaiThietBi,
                SoThangChuKyDeXuat = c.SoThangChuKyDeXuat,
                MoTa = c.MoTa
            }).ToList();

        private static ChiTietKeHoachDto MapChiTiet(ChiTietKeHoachBaoTri c) => new()
        {
            MaChiTietKeHoach = c.MaChiTietKeHoach,
            MaThietBi = c.MaThietBi,
            TenThietBi = c.MaThietBiNavigation?.TenThietBi,
            NgayDuKienBaoTri = c.NgayDuKienBaoTri,
            MaHoSoBaoTri = c.MaHoSoBaoTri
        };
    }
}