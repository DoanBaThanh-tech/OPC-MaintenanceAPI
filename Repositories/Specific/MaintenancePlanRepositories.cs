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
        Task<List<KeHoachBaoTri>> GetAllKeHoachAsync();
        Task<List<ChuKyBaoTri>> GetAllChuKyAsync();
        Task<bool> TonTaiKeHoachTheoThietBiNamAsync(int maThietBi, int nam);
        Task<ThietBi?> GetThietBiAsync(int maThietBi);
        Task<int> SaveChangesAsync();
    }

    public class MaintenancePlanRepository : IMaintenancePlanRepository
    {
        private readonly OPCDbContext _context;
        public MaintenancePlanRepository(OPCDbContext context) => _context = context;

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

        public Task<ThietBi?> GetThietBiAsync(int maThietBi) =>
            _context.ThietBis.Include(t => t.MaChuKyNavigation)
                .FirstOrDefaultAsync(t => t.MaThietBi == maThietBi);

        public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();
    }
}