using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Data;

namespace OPC.MaintenanceAPI.Repositories.Specific
{
    public interface IEquipmentRepository
    {
        Task<List<ThietBi>> GetAllAsync();
        Task<ThietBi?> GetByIdAsync(int id);
        Task<bool> ExistsAsync(string tenThietBi, string viTriLapDat);
        Task<bool> DangCoHoSoDangThucHienAsync(int maThietBi);
        Task AddAsync(ThietBi thietBi);
        Task<List<LichSuThietBi>> GetLichSuAsync(int maThietBi);
        Task AddLichSuAsync(LichSuThietBi lichSu);
        Task<int> SaveChangesAsync();
    }

    public class EquipmentRepository : IEquipmentRepository
    {
        private readonly OPCDbContext _context;
        public EquipmentRepository(OPCDbContext context) => _context = context;

        public async Task<List<ThietBi>> GetAllAsync() =>
            await _context.ThietBis
                .Include(t => t.MaChuKyNavigation)
                .AsNoTracking()
                .OrderBy(t => t.LoaiThietBi)
                .ThenBy(t => t.TenThietBi)
                .ToListAsync();

        public async Task<ThietBi?> GetByIdAsync(int id) =>
            await _context.ThietBis
                .Include(t => t.MaChuKyNavigation)
                .FirstOrDefaultAsync(t => t.MaThietBi == id);

        public async Task<bool> ExistsAsync(string tenThietBi, string viTriLapDat) =>
            await _context.ThietBis.AnyAsync(t => t.TenThietBi == tenThietBi && t.ViTriLapDat == viTriLapDat);

        // Kiểm tra thiết bị có hồ sơ bảo trì HOẶC sửa chữa đang "Đang thực hiện"
        public async Task<bool> DangCoHoSoDangThucHienAsync(int maThietBi)
        {
            var coBaoTri = await _context.HoSoBaoTris
                .AnyAsync(h => h.MaThieBi == maThietBi && h.TrangThai == "Đang thực hiện");
            var coSuaChua = await _context.HoSoSuaChuas
                .AnyAsync(h => h.MaThieBi == maThietBi && h.TrangThai == "Đang thực hiện");
            return coBaoTri || coSuaChua;
        }

        public async Task AddAsync(ThietBi thietBi) => await _context.ThietBis.AddAsync(thietBi);

        public async Task<List<LichSuThietBi>> GetLichSuAsync(int maThietBi) =>
            await _context.LichSuThietBis
                .Where(l => l.MaThietBi == maThietBi)
                .OrderByDescending(l => l.NgayHoanThanh)
                .AsNoTracking()
                .ToListAsync();

        public async Task AddLichSuAsync(LichSuThietBi lichSu) => await _context.LichSuThietBis.AddAsync(lichSu);

        public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();
    }
}