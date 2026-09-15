using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Data;

namespace OPC.MaintenanceAPI.Repositories.Specific
{
    public interface IMaintenancePlanRepository
    {
        Task AddKeHoachAsync(KeHoachBaoTri keHoach);
        Task AddChiTietRangeAsync(IEnumerable<ChiTietKeHoachBaoTri> chiTiets);
        Task<List<ChiTietKeHoachBaoTri>> GetChiTietChuaCoHoSoAsync();
        Task<List<ChiTietKeHoachBaoTri>> GetChiTietTheoKeHoachAsync(int maKeHoach);
        Task<ChiTietKeHoachBaoTri?> GetChiTietKeHoachByIdAsync(int id);
        Task<List<int>> GetDistinctNamAsync();
        Task<List<KeHoachBaoTri>> GetAllKeHoachAsync();
        Task<List<ChuKyBaoTri>> GetAllChuKyAsync();
        Task<bool> TonTaiKeHoachTheoThietBiNamAsync(int maThietBi, int nam);
        /// <summary>Đã có dòng kế hoạch cho thiết bị trong đúng tháng/năm (theo NgayDuKienBaoTri).</summary>
        Task<bool> TonTaiKeHoachTheoThietBiThangAsync(int maThietBi, int nam, int thang);
        /// <summary>Đã có hồ sơ bảo trì gắn với chi tiết kế hoạch của thiết bị trong tháng/năm.</summary>
        Task<bool> TonTaiHoSoBaoTriTheoThietBiThangAsync(int maThietBi, int nam, int thang);
        Task<ThietBi?> GetThietBiAsync(int maThietBi);
        Task<KeHoachBaoTri?> GetKeHoachByIdAsync(int maKeHoach);
        Task<DateOnly?> GetNgayBaoTriGanNhatAsync(int maKeHoach, int maThietBi);
        Task<KeHoachBaoTri?> GetKeHoachByNamAsync(int nam);
        Task<bool> NamDaTonTaiAsync(int nam);
        Task<int> SaveChangesAsync();
    }


    public class MaintenancePlanRepository : IMaintenancePlanRepository
    {
        private readonly OPCDbContext _context;
        public MaintenancePlanRepository(OPCDbContext context) => _context = context;
        
        public Task<KeHoachBaoTri?> GetKeHoachByNamAsync(int nam) =>
            _context.KeHoachBaoTris.FirstOrDefaultAsync(k => k.Nam == nam);

        public Task<bool> NamDaTonTaiAsync(int nam) =>
            _context.KeHoachBaoTris.AnyAsync(k => k.Nam == nam);
        public async Task<KeHoachBaoTri?> GetKeHoachByIdAsync(int maKeHoach) =>
        await _context.KeHoachBaoTris
            .Include(k => k.MaChuKyNavigation)
            .FirstOrDefaultAsync(k => k.MaKeHoach == maKeHoach);
        
                // Lấy ngày dự kiến bảo trì gần nhất (mới nhất) của thiết bị trong đúng kế hoạch này,
        // dùng để gợi ý ngày cho lần bảo trì tiếp theo

        public async Task<List<int>> GetDistinctNamAsync() =>
        await _context.KeHoachBaoTris
            .Select(k => k.Nam)
            .Distinct()
            .OrderByDescending(n => n)
            .ToListAsync();
        public async Task<DateOnly?> GetNgayBaoTriGanNhatAsync(int maKeHoach, int maThietBi) =>
            await _context.ChiTietKeHoachBaoTris
                .Where(c => c.MaKeHoach == maKeHoach && c.MaThietBi == maThietBi)
                .OrderByDescending(c => c.NgayDuKienBaoTri)
                .Select(c => (DateOnly?)c.NgayDuKienBaoTri)
                .FirstOrDefaultAsync();

        public async Task AddKeHoachAsync(KeHoachBaoTri keHoach) => await _context.KeHoachBaoTris.AddAsync(keHoach);

        public async Task AddChiTietRangeAsync(IEnumerable<ChiTietKeHoachBaoTri> chiTiets) =>
            await _context.ChiTietKeHoachBaoTris.AddRangeAsync(chiTiets);

        public async Task<List<ChiTietKeHoachBaoTri>> GetChiTietChuaCoHoSoAsync() =>
            await _context.ChiTietKeHoachBaoTris
                .Include(c => c.MaThietBiNavigation)
                .Where(c => c.MaHoSoBaoTri == null)
                .ToListAsync();

        public async Task<List<ChiTietKeHoachBaoTri>> GetChiTietTheoKeHoachAsync(int maKeHoach) =>
            await _context.ChiTietKeHoachBaoTris
                .Include(c => c.MaThietBiNavigation)
                .Where(c => c.MaKeHoach == maKeHoach)
                .ToListAsync();

        public async Task<ChiTietKeHoachBaoTri?> GetChiTietKeHoachByIdAsync(int id) =>
            await _context.ChiTietKeHoachBaoTris
                .Include(c => c.MaThietBiNavigation)
                .FirstOrDefaultAsync(c => c.MaChiTietKeHoach == id);

        public async Task<List<KeHoachBaoTri>> GetAllKeHoachAsync() =>
            await _context.KeHoachBaoTris
                .Include(k => k.MaNhanVienLapNavigation)
                .Include(k => k.MaChuKyNavigation)
                .Include(k => k.ChiTietKeHoachBaoTris)
                    .ThenInclude(c => c.MaThietBiNavigation)
                .OrderByDescending(k => k.Nam)
                .ToListAsync();

        public async Task<List<ChuKyBaoTri>> GetAllChuKyAsync() =>
            await _context.ChuKyBaoTris.ToListAsync();

        public async Task<bool> TonTaiKeHoachTheoThietBiNamAsync(int maThietBi, int nam) =>
            await _context.ChiTietKeHoachBaoTris.AnyAsync(c =>
                c.MaThietBi == maThietBi && c.MaKeHoachNavigation != null && c.MaKeHoachNavigation.Nam == nam);

        public async Task<bool> TonTaiKeHoachTheoThietBiThangAsync(int maThietBi, int nam, int thang) =>
            await _context.ChiTietKeHoachBaoTris.AnyAsync(c =>
                c.MaThietBi == maThietBi
                && c.NgayDuKienBaoTri.Year == nam
                && c.NgayDuKienBaoTri.Month == thang);

        public async Task<bool> TonTaiHoSoBaoTriTheoThietBiThangAsync(int maThietBi, int nam, int thang) =>
            await _context.ChiTietKeHoachBaoTris.AnyAsync(c =>
                c.MaThietBi == maThietBi
                && c.NgayDuKienBaoTri.Year == nam
                && c.NgayDuKienBaoTri.Month == thang
                && c.MaHoSoBaoTri != null);

        public Task<ThietBi?> GetThietBiAsync(int maThietBi) =>
            _context.ThietBis.Include(t => t.MaChuKyNavigation)
                .FirstOrDefaultAsync(t => t.MaThietBi == maThietBi);

        public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();
    }
}