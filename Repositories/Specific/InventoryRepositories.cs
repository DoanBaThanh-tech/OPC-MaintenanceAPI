using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Data;

namespace OPC.MaintenanceAPI.Repositories.Specific
{
    public interface IInventoryRepository
    {
        Task<VatTu?> GetVatTuByIdAsync(int id);
        Task AddHoSoYeuCauAsync(HoSoYeuCauVatTu hoSo);
        Task AddChiTietRangeAsync(IEnumerable<ChiTietYeuCauVatTu> chiTiets);
        Task<HoSoYeuCauVatTu?> GetHoSoYeuCauByIdAsync(int id);
        Task<List<ChiTietYeuCauVatTu>> GetChiTietByHoSoAsync(int maHoSo);
        Task AddGiaoDichAsync(NhapXuatVatTu giaoDich);
        Task AddLichSuPheDuyetAsync(LichSuPheDuyet lichSu);
        Task<int> SaveChangesAsync();
    }

    public class InventoryRepository : IInventoryRepository
    {
        private readonly OPCDbContext _context;
        public InventoryRepository(OPCDbContext context) => _context = context;

        public async Task<VatTu?> GetVatTuByIdAsync(int id) =>
            await _context.VatTus.FirstOrDefaultAsync(v => v.MaVatTu == id);

        public async Task AddHoSoYeuCauAsync(HoSoYeuCauVatTu hoSo) => await _context.HoSoYeuCauVatTus.AddAsync(hoSo);

        public async Task AddChiTietRangeAsync(IEnumerable<ChiTietYeuCauVatTu> chiTiets) =>
            await _context.ChiTietYeuCauVatTus.AddRangeAsync(chiTiets);

        public async Task<HoSoYeuCauVatTu?> GetHoSoYeuCauByIdAsync(int id) =>
            await _context.HoSoYeuCauVatTus.FirstOrDefaultAsync(h => h.MaYeuCauVatTu == id);

        public async Task<List<ChiTietYeuCauVatTu>> GetChiTietByHoSoAsync(int maHoSo) =>
            await _context.ChiTietYeuCauVatTus
                .Include(c => c.MaVatTuNavigation)
                .Where(c => c.MaYeuCauVatTu == maHoSo)
                .ToListAsync();

        public async Task AddGiaoDichAsync(NhapXuatVatTu giaoDich) => await _context.NhapXuatVatTus.AddAsync(giaoDich);

        public async Task AddLichSuPheDuyetAsync(LichSuPheDuyet lichSu) => await _context.LichSuPheDuyets.AddAsync(lichSu);

        public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();
    }
}