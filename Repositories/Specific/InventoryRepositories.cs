using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.DTOs.Inventory;

namespace OPC.MaintenanceAPI.Repositories.Specific
{
    public interface IInventoryRepository
    {
        Task<VatTu?> GetVatTuByIdAsync(int id);
        Task AddHoSoYeuCauAsync(HoSoYeuCauVatTu hoSo);
        Task AddChiTietRangeAsync(IEnumerable<ChiTietYeuCauVatTu> chiTiets);
        Task<HoSoYeuCauVatTu?> GetHoSoYeuCauByIdAsync(int id);
        Task<List<ChiTietYeuCauVatTu>> GetChiTietByHoSoAsync(int maHoSo);
        Task<HoSoSuaChua?> GetHoSoSuaChuaByIdAsync(int id);
        Task<bool> DaXuatChoYeuCauAsync(int maYeuCauVatTu);
        Task AddGiaoDichAsync(NhapXuatVatTu giaoDich);
        Task AddLichSuPheDuyetAsync(LichSuPheDuyet lichSu);
        Task<int> SaveChangesAsync();

        Task<List<VatTuDto>> GetAllVatTuAsync();
        Task AddHoSoSuDungVatTuAsync(HoSoSuDungVatTu hoSo);
        Task<HoSoVatTuResponseDto?> GetHoSoSuDungVatTuByIdAsync(int id);
        Task<HoSoSuDungVatTu?> GetHoSoSuDungEntityByIdAsync(int id);
        Task<List<HoSoVatTuResponseDto>> GetDanhSachHoSoSuDungVatTuAsync(string? trangThai);
        Task<List<BuocQuyTrinhDto>> GetQuyTrinhThietBiAsync(int maThietBi, string loaiCongViec);
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

        public async Task<HoSoSuaChua?> GetHoSoSuaChuaByIdAsync(int id) =>
            await _context.HoSoSuaChuas.FirstOrDefaultAsync(h => h.MaHoSoSuaChua == id);

        public async Task<bool> DaXuatChoYeuCauAsync(int maYeuCauVatTu) =>
            await _context.NhapXuatVatTus.AnyAsync(g =>
                g.MaYeuCauVatTu == maYeuCauVatTu && g.LoaiGiaoDich == "Xuất");
        public async Task AddGiaoDichAsync(NhapXuatVatTu giaoDich) => await _context.NhapXuatVatTus.AddAsync(giaoDich);

        public async Task AddLichSuPheDuyetAsync(LichSuPheDuyet lichSu) => await _context.LichSuPheDuyets.AddAsync(lichSu);

        public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();

        public async Task<List<VatTuDto>> GetAllVatTuAsync()
        {
            return await _context.VatTus
                .OrderBy(v => v.TenVatTu)
                .Select(v => new VatTuDto
                {
                    MaVatTu = v.MaVatTu,
                    TenVatTu = v.TenVatTu,
                    DonViTinh = v.DonViTinh,
                    SoLuongTonKho = v.SoLuongTonKho,
                    DonGia = v.DonGia
                })
                .ToListAsync();
        }

        public async Task AddHoSoSuDungVatTuAsync(HoSoSuDungVatTu hoSo) =>
            await _context.HoSoSuDungVatTus.AddAsync(hoSo);

        public async Task<HoSoSuDungVatTu?> GetHoSoSuDungEntityByIdAsync(int id) =>
            await _context.HoSoSuDungVatTus.FirstOrDefaultAsync(h => h.MaHoSoVatTu == id);

        public async Task<HoSoVatTuResponseDto?> GetHoSoSuDungVatTuByIdAsync(int id)
        {
            var h = await _context.HoSoSuDungVatTus
                .Include(x => x.ChiTietSuDungVatTus)
                .Include(x => x.MaNhanVienTHNavigation)
                .FirstOrDefaultAsync(x => x.MaHoSoVatTu == id);
            return h == null ? null : Map(h);
        }

        public async Task<List<HoSoVatTuResponseDto>> GetDanhSachHoSoSuDungVatTuAsync(string? trangThai)
        {
            var q = _context.HoSoSuDungVatTus
                .Include(x => x.ChiTietSuDungVatTus)
                .Include(x => x.MaNhanVienTHNavigation)
                .AsQueryable();
            if (!string.IsNullOrWhiteSpace(trangThai))
                q = q.Where(x => x.TrangThai == trangThai);
            var list = await q.OrderByDescending(x => x.NgayThucHien).ToListAsync();
            return list.Select(Map).ToList();
        }

        private static HoSoVatTuResponseDto Map(HoSoSuDungVatTu h) => new()
        {
            MaHoSoVatTu = h.MaHoSoVatTu,
            MaHoSoBaoTri = h.MaHoSoBaoTri,
            MaHoSoSuaChua = h.MaHoSoSuaChua,
            MaThietBi = h.MaThietBi,
            TenThietBi = h.TenThietBi,
            LoaiCongViec = h.LoaiCongViec,
            NgayThucHien = h.NgayThucHien,
            TenNhanVien = h.MaNhanVienTHNavigation?.HoTen,
            TongTien = h.TongTien,
            TrangThai = h.TrangThai,
            NgayGuiGiamDoc = h.NgayGuiGiamDoc,
            ChiTiet = h.ChiTietSuDungVatTus
                .OrderBy(c => c.SoBuoc)
                .Select(c => new ChiTietSuDungResponseDto
                {
                    SoBuoc = c.SoBuoc,
                    MoTaBuoc = c.MoTaBuoc,
                    MaVatTu = c.MaVatTu,
                    TenVatTu = c.TenVatTu,
                    SoLuong = c.SoLuong,
                    DonGia = c.DonGia,
                    ThanhTien = c.SoLuong * c.DonGia
                }).ToList()
        };

        public async Task<List<BuocQuyTrinhDto>> GetQuyTrinhThietBiAsync(int maThietBi, string loaiCongViec)
        {
            return await _context.QuyTrinhThietBis
                .Where(q => q.MaThietBi == maThietBi && q.LoaiCongViec == loaiCongViec)
                .OrderBy(q => q.SoBuoc)
                .Select(q => new BuocQuyTrinhDto
                {
                    SoBuoc = q.SoBuoc,
                    MoTaBuoc = q.MoTaBuoc
                })
                .ToListAsync();
        }

    }
}
