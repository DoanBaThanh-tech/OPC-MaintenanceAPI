using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Data;

namespace OPC.MaintenanceAPI.Repositories.Specific
{
    public interface IWorkOrderRepository
    {
        // Hồ sơ bảo trì
        Task AddHoSoBaoTriAsync(HoSoBaoTri hoSo);
        Task<HoSoBaoTri?> GetHoSoBaoTriByIdAsync(int id);
        Task<List<HoSoBaoTri>> GetHoSoBaoTriByTrangThaiAsync(string trangThai);

        // Hồ sơ sửa chữa
        Task AddHoSoSuaChuaAsync(HoSoSuaChua hoSo);
        Task<HoSoSuaChua?> GetHoSoSuaChuaByIdAsync(int id);
        Task<List<HoSoSuaChua>> GetHoSoSuaChuaByTrangThaiAsync(string trangThai);

        // Phân công + kết quả
        Task AddPhanCongAsync(PhanCongCongViec phanCong);
        Task<PhanCongCongViec?> GetPhanCongByIdAsync(int id);
        Task AddKetQuaAsync(KetQuaThucHien ketQua);

        // Lịch sử phê duyệt
        Task AddLichSuPheDuyetAsync(LichSuPheDuyet lichSu);

        Task<int> SaveChangesAsync();
    }

    public class WorkOrderRepository : IWorkOrderRepository
    {
        private readonly OPCDbContext _context;
        public WorkOrderRepository(OPCDbContext context) => _context = context;

        public async Task AddHoSoBaoTriAsync(HoSoBaoTri hoSo) => await _context.HoSoBaoTris.AddAsync(hoSo);

        public async Task<HoSoBaoTri?> GetHoSoBaoTriByIdAsync(int id) =>
            await _context.HoSoBaoTris
                .Include(h => h.MaThieBiNavigation)
                .Include(h => h.MaPhanCongNavigation)
                .FirstOrDefaultAsync(h => h.MaHoSoBaoTri == id);

        public async Task<List<HoSoBaoTri>> GetHoSoBaoTriByTrangThaiAsync(string trangThai) =>
            await _context.HoSoBaoTris.Where(h => h.TrangThai == trangThai).ToListAsync();

        public async Task AddHoSoSuaChuaAsync(HoSoSuaChua hoSo) => await _context.HoSoSuaChuas.AddAsync(hoSo);

        public async Task<HoSoSuaChua?> GetHoSoSuaChuaByIdAsync(int id) =>
            await _context.HoSoSuaChuas
                .Include(h => h.MaThieBiNavigation)
                .Include(h => h.MaPhanCongNavigation)
                .FirstOrDefaultAsync(h => h.MaHoSoSuaChua == id);

        public async Task<List<HoSoSuaChua>> GetHoSoSuaChuaByTrangThaiAsync(string trangThai) =>
            await _context.HoSoSuaChuas.Where(h => h.TrangThai == trangThai).ToListAsync();

        public async Task AddPhanCongAsync(PhanCongCongViec phanCong) =>
            await _context.PhanCongCongViecs.AddAsync(phanCong);

        public async Task<PhanCongCongViec?> GetPhanCongByIdAsync(int id) =>
            await _context.PhanCongCongViecs.FirstOrDefaultAsync(p => p.MaPhanCong == id);

        public async Task AddKetQuaAsync(KetQuaThucHien ketQua) => await _context.KetQuaThucHiens.AddAsync(ketQua);

        public async Task AddLichSuPheDuyetAsync(LichSuPheDuyet lichSu) =>
            await _context.LichSuPheDuyets.AddAsync(lichSu);

        public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();
    }
}