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
        /// <summary>
        /// Đồng bộ TinhTrangHienTai theo hồ sơ BT/SC còn hiệu lực
        /// (Chờ duyệt / Đã duyệt / Đang thực hiện). SC ưu tiên hơn BT.
        /// </summary>
        Task<int> DongBoTrangThaiTuHoSoAsync();
        Task<int> SaveChangesAsync();
    }

    public class EquipmentRepository : IEquipmentRepository
    {
        private readonly OPCDbContext _context;
        public EquipmentRepository(OPCDbContext context) => _context = context;

        private static readonly string[] TrangThaiHoSoHieuLuc =
        {
            "Chờ duyệt", "Đã duyệt", "Đang thực hiện","Từ chối"
        };

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

        public async Task<int> DongBoTrangThaiTuHoSoAsync()
        {
            var maTbSuaChua = await _context.HoSoSuaChuas
                .Where(h => TrangThaiHoSoHieuLuc.Contains(h.TrangThai))
                .Select(h => h.MaThieBi)
                .Distinct()
                .ToListAsync();

            var maTbBaoTri = await _context.HoSoBaoTris
                .Where(h => TrangThaiHoSoHieuLuc.Contains(h.TrangThai))
                .Select(h => h.MaThieBi)
                .Distinct()
                .ToListAsync();

            var setSC = maTbSuaChua.ToHashSet();
            var setBT = maTbBaoTri.ToHashSet();

            // Cần tracking để cập nhật
            var all = await _context.ThietBis.ToListAsync();
            var soDoi = 0;

            foreach (var t in all)
            {
                string moi;
                if (setSC.Contains(t.MaThietBi))
                    moi = "Sửa chữa";
                else if (setBT.Contains(t.MaThietBi))
                    moi = "Bảo trì";
                else
                    moi = "Sản xuất";

                var hienTai = (t.TinhTrangHienTai ?? "").Trim();
                if (!string.Equals(hienTai, moi, StringComparison.Ordinal))
                {
                    t.TinhTrangHienTai = moi;
                    soDoi++;
                }
            }

            if (soDoi > 0)
                await _context.SaveChangesAsync();

            return soDoi;
        }

        public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();
    }
}