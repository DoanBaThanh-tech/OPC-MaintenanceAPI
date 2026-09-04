using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Data;

namespace OPC.MaintenanceAPI.Repositories.Specific
{
    public interface IMaintenancePlanRepository
    {
        Task<List<ThietBi>> GetThietBiKemChuKyAsync();
        Task AddKeHoachAsync(KeHoachBaoTri keHoach);
        Task AddChiTietRangeAsync(IEnumerable<ChiTietKeHoachBaoTri> chiTiets);
        Task<List<ChiTietKeHoachBaoTri>> GetChiTietChuaCoHoSoAsync();
        Task<ChiTietKeHoachBaoTri?> GetChiTietByIdAsync(int id);
        Task<int> SaveChangesAsync();
    }

    public class MaintenancePlanRepository : IMaintenancePlanRepository
    {
        private readonly OPCDbContext _context;
        public MaintenancePlanRepository(OPCDbContext context) => _context = context;

        // Query phức tạp: join ThietBi với ChuKyBaoTri theo LoaiThietBi để tính ngày dự kiến
        public async Task<List<ThietBi>> GetThietBiKemChuKyAsync() =>
            await _context.ThietBis.ToListAsync(); // Chu kỳ được ráp bên Service theo LoaiThietBi

        public async Task AddKeHoachAsync(KeHoachBaoTri keHoach) => await _context.KeHoachBaoTris.AddAsync(keHoach);

        public async Task AddChiTietRangeAsync(IEnumerable<ChiTietKeHoachBaoTri> chiTiets) =>
            await _context.ChiTietKeHoachBaoTris.AddRangeAsync(chiTiets);

        public async Task<List<ChiTietKeHoachBaoTri>> GetChiTietChuaCoHoSoAsync() =>
            await _context.ChiTietKeHoachBaoTris
                .Include(c => c.MaThietBiNavigation)
                .Where(c => c.MaHoSoBaoTri == null)
                .ToListAsync();

        public async Task<ChiTietKeHoachBaoTri?> GetChiTietByIdAsync(int id) =>
            await _context.ChiTietKeHoachBaoTris
                .Include(c => c.MaThietBiNavigation)
                .FirstOrDefaultAsync(c => c.MaChiTietKeHoach == id);

        public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();
    }
}