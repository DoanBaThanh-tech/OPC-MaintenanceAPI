using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.DTOs.MaintenancePlan;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public class MaintenancePlanService : IMaintenancePlanService
    {
        private readonly IMaintenancePlanRepository _repo;
        public MaintenancePlanService(IMaintenancePlanRepository repo) => _repo = repo;

        // Luồng 6A — Decision: phải chọn ít nhất 1 thiết bị
        public async Task<(bool, string?)> LapKeHoachAsync(LapKeHoachDto dto)
        {
            if (dto.ThietBiDuocChon == null || dto.ThietBiDuocChon.Count == 0)
                return (false, "Vui lòng chọn ít nhất 1 thiết bị.");

            var keHoach = new KeHoachBaoTri
            {
                Nam = dto.Nam,
                MaNhanVienLap = dto.MaNhanVienLap,
                NgayLapKeHoach = DateOnly.FromDateTime(DateTime.Now),   // sửa dòng này
                TrangThai = "Đã lập"
            };
            await _repo.AddKeHoachAsync(keHoach);
            await _repo.SaveChangesAsync(); // cần MaKeHoach (PK) trước khi tạo chi tiết

            var chiTiets = dto.ThietBiDuocChon.Select(t => new ChiTietKeHoachBaoTri
            {
                MaKeHoach = keHoach.MaKeHoach,
                MaThietBi = t.MaThietBi,
                NgayDuKienBaoTri = t.NgayDuKienBaoTri
                // MaHoSoBaoTri để trống — chỉ gắn khi Luồng 6B tạo hồ sơ thật
            });
            await _repo.AddChiTietRangeAsync(chiTiets);
            await _repo.SaveChangesAsync();

            return (true, null);
        }

        public async Task<List<ChiTietKeHoachDto>> GetChiTietChuaCoHoSoAsync() =>
            (await _repo.GetChiTietChuaCoHoSoAsync()).Select(c => new ChiTietKeHoachDto
            {
                MaChiTietKeHoach = c.MaChiTietKeHoach,
                TenThietBi = c.MaThietBiNavigation?.TenThietBi,
                NgayDuKienBaoTri = c.NgayDuKienBaoTri
            }).ToList();
    }
}