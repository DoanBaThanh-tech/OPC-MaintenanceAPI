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
        Task<HoSoSuDungVatTu?> GetHoSoSuDungEntityWithChiTietByIdAsync(int id);
        Task<HoSoSuDungVatTu?> GetHoSoSuDungEntityByCongViecAsync(int? maHoSoBaoTri, int? maHoSoSuaChua);
        Task RemoveChiTietSuDungRangeAsync(IEnumerable<ChiTietSuDungVatTu> chiTiets);
        Task<List<HoSoVatTuResponseDto>> GetDanhSachHoSoSuDungVatTuAsync(string? trangThai);
        Task<List<BuocQuyTrinhDto>> GetQuyTrinhThietBiAsync(int maThietBi, string loaiCongViec);
        Task CapNhatMoTaBuocQuyTrinhAsync(int maThietBi, string loaiCongViec, List<BuocQuyTrinhDto> buoc);
        /// <summary>Danh sách quy trình (TB × loại) cho combo khi tạo hồ sơ.</summary>
        Task<List<object>> GetDanhSachQuyTrinhAsync(string? loaiCongViec);
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

        public async Task<HoSoSuDungVatTu?> GetHoSoSuDungEntityWithChiTietByIdAsync(int id) =>
            await _context.HoSoSuDungVatTus
                .Include(x => x.ChiTietSuDungVatTus)
                .FirstOrDefaultAsync(h => h.MaHoSoVatTu == id);

        public async Task<HoSoSuDungVatTu?> GetHoSoSuDungEntityByCongViecAsync(int? maHoSoBaoTri, int? maHoSoSuaChua)
        {
            var q = _context.HoSoSuDungVatTus
                .Include(x => x.ChiTietSuDungVatTus)
                .AsQueryable();
            if (maHoSoBaoTri.HasValue && maHoSoBaoTri.Value > 0)
                q = q.Where(h => h.MaHoSoBaoTri == maHoSoBaoTri.Value);
            else if (maHoSoSuaChua.HasValue && maHoSoSuaChua.Value > 0)
                q = q.Where(h => h.MaHoSoSuaChua == maHoSoSuaChua.Value);
            else
                return null;
            // Lấy hồ sơ mới nhất (có thể có nhiều lần tạo)
            return await q.OrderByDescending(h => h.NgayTao).FirstOrDefaultAsync();
        }

        public Task RemoveChiTietSuDungRangeAsync(IEnumerable<ChiTietSuDungVatTu> chiTiets)
        {
            _context.ChiTietSuDungVatTus.RemoveRange(chiTiets);
            return Task.CompletedTask;
        }

        public async Task<HoSoVatTuResponseDto?> GetHoSoSuDungVatTuByIdAsync(int id)
        {
            var h = await _context.HoSoSuDungVatTus
                .Include(x => x.ChiTietSuDungVatTus)
                .Include(x => x.MaNhanVienTHNavigation)
                .FirstOrDefaultAsync(x => x.MaHoSoVatTu == id);
            if (h == null) return null;
            var tenNvkt = await LayTenNvktPhanCongAsync(h.MaHoSoBaoTri, h.MaHoSoSuaChua);
            return Map(h, tenNvkt);
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

            // Gộp tên tất cả NVKT tổ trưởng đã phân công (theo từng hồ sơ BT/SC)
            var result = new List<HoSoVatTuResponseDto>(list.Count);
            foreach (var h in list)
            {
                var tenNvkt = await LayTenNvktPhanCongAsync(h.MaHoSoBaoTri, h.MaHoSoSuaChua);
                result.Add(Map(h, tenNvkt));
            }
            return result;
        }

        /// <summary>
        /// Lấy đủ họ tên NVKT mà tổ trưởng đã chọn lúc phân công (loại trừ Đã hủy).
        /// Fallback: người tạo hồ sơ vật tư nếu chưa có phân công.
        /// </summary>
        private async Task<string?> LayTenNvktPhanCongAsync(int? maHoSoBaoTri, int? maHoSoSuaChua)
        {
            if ((!maHoSoBaoTri.HasValue || maHoSoBaoTri.Value <= 0) &&
                (!maHoSoSuaChua.HasValue || maHoSoSuaChua.Value <= 0))
                return null;

            var q = _context.PhanCongCongViecs
                .AsNoTracking()
                .Include(p => p.MaNhanVienThucHienNavigation)
                .Where(p => p.TrangThai == null || p.TrangThai != "Đã hủy");

            if (maHoSoBaoTri.HasValue && maHoSoBaoTri.Value > 0)
                q = q.Where(p => p.MaHoSoBaoTri == maHoSoBaoTri.Value);
            else
                q = q.Where(p => p.MaHoSoSuaChua == maHoSoSuaChua!.Value);

            var tens = await q
                .OrderBy(p => p.NgayPhanCong)
                .ThenBy(p => p.MaPhanCong)
                .Select(p => p.MaNhanVienThucHienNavigation.HoTen)
                .Where(t => t != null && t != "")
                .Distinct()
                .ToListAsync();

            if (tens.Count == 0) return null;
            return string.Join(", ", tens);
        }

        private static HoSoVatTuResponseDto Map(HoSoSuDungVatTu h, string? tenNvktDayDu) => new()
        {
            MaHoSoVatTu = h.MaHoSoVatTu,
            MaHoSoBaoTri = h.MaHoSoBaoTri,
            MaHoSoSuaChua = h.MaHoSoSuaChua,
            MaThietBi = h.MaThietBi,
            TenThietBi = h.TenThietBi,
            LoaiCongViec = h.LoaiCongViec,
            NgayThucHien = h.NgayThucHien,
            // Ưu tiên đủ NVKT tổ trưởng đã chọn; fallback người lập HS VT
            TenNhanVien = !string.IsNullOrWhiteSpace(tenNvktDayDu)
                ? tenNvktDayDu
                : h.MaNhanVienTHNavigation?.HoTen,
            TongTien = h.TongTien,
            TrangThai = h.TrangThai,
            NgayGuiGiamDoc = h.NgayGuiGiamDoc,
            GhiChu = h.GhiChu,
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

        public async Task CapNhatMoTaBuocQuyTrinhAsync(
            int maThietBi, string loaiCongViec, List<BuocQuyTrinhDto> buoc)
        {
            var rows = await _context.QuyTrinhThietBis
                .Where(q => q.MaThietBi == maThietBi && q.LoaiCongViec == loaiCongViec)
                .ToListAsync();
            foreach (var item in buoc)
            {
                var row = rows.FirstOrDefault(r => r.SoBuoc == item.SoBuoc);
                if (row == null) continue;
                var moTa = (item.MoTaBuoc ?? "").Trim();
                if (moTa.Length > 500) moTa = moTa[..500];
                if (string.IsNullOrEmpty(moTa)) moTa = $"Bước {item.SoBuoc}";
                row.MoTaBuoc = moTa;
            }
            await _context.SaveChangesAsync();
        }

        public async Task<List<object>> GetDanhSachQuyTrinhAsync(string? loaiCongViec)
        {
            var q = _context.QuyTrinhThietBis
                .Include(x => x.MaThietBiNavigation)
                .AsQueryable();
            if (!string.IsNullOrWhiteSpace(loaiCongViec))
                q = q.Where(x => x.LoaiCongViec == loaiCongViec.Trim());

            var groups = await q
                .GroupBy(x => new { x.MaThietBi, x.LoaiCongViec })
                .Select(g => new
                {
                    g.Key.MaThietBi,
                    g.Key.LoaiCongViec,
                    SoBuoc = g.Count(),
                    TenThietBi = g.Select(x => x.MaThietBiNavigation!.TenThietBi).FirstOrDefault()
                })
                .OrderBy(x => x.TenThietBi)
                .ThenBy(x => x.LoaiCongViec)
                .ToListAsync();

            return groups.Select(x => (object)new
            {
                x.MaThietBi,
                TenThietBi = x.TenThietBi ?? $"TB #{x.MaThietBi}",
                x.LoaiCongViec,
                x.SoBuoc,
                Nhan = $"{x.TenThietBi ?? $"TB #{x.MaThietBi}"} — {x.LoaiCongViec} ({x.SoBuoc} bước)"
            }).ToList();
        }
    }
}
