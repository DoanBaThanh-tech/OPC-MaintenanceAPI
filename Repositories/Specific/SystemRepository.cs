using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Repositories.Base;
namespace OPC.MaintenanceAPI.Repositories.Specific
{
    public interface ISystemRepository : IBaseRepository<VaiTro>
    {
        Task<List<(VaiTro VaiTro, int SoNguoiDung)>> GetAllVaiTroWithUserCountAsync();
        Task<bool> VaiTroDangCoNguoiDungAsync(int maVaiTro);
 
        Task<List<DanhMucChucNang>> GetChucNangGroupedAsync();
        Task<List<object>> GetDanhSachNhanVienAsync(string? vaiTro = null);
        Task<List<PhanQuyenVaiTro>> GetPhanQuyenByVaiTroAsync(int maVaiTro);
        Task XoaPhanQuyenTheoVaiTroAsync(int maVaiTro);
        Task ThemDanhSachPhanQuyenAsync(List<PhanQuyenVaiTro> danhSach);
 
        Task<List<NhatKyHeThong>> GetNhatKyAsync(string? tuKhoa, string? phuongThucHTTP, DateTime? tuNgay, DateTime? denNgay);
    }

    public class SystemRepository : BaseRepository<VaiTro>, ISystemRepository
    {
        public SystemRepository(OPCDbContext context) : base(context) { }


        public async Task<List<object>> GetDanhSachNhanVienAsync(string? vaiTro = null)
        {
            var query = _context.NhanViens
                .Include(nv => nv.MaNguoiDungNavigation)
                    .ThenInclude(nd => nd.MaVaiTroNavigation)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(vaiTro))
            {
                query = query.Where(nv =>
                    nv.MaNguoiDungNavigation.MaVaiTroNavigation.TenVaiTro.Contains(vaiTro));
            }

            // Đếm số công việc BT + SC "Đang thực hiện" theo từng NV (tối đa 3)
            const int toiDa = WorkOrderRepository.SoThietBiToiDaMoiNhanVien;

            var demTheoNv = await _context.HoSoBaoTris
                .Where(h => h.TrangThai == "Đang thực hiện" && h.MaPhanCong != null)
                .Join(_context.PhanCongCongViecs,
                    h => h.MaPhanCong,
                    p => p.MaPhanCong,
                    (h, p) => p.MaNhanVienThucHien)
                .Concat(
                    _context.HoSoSuaChuas
                        .Where(h => h.TrangThai == "Đang thực hiện" && h.MaPhanCong != null)
                        .Join(_context.PhanCongCongViecs,
                            h => h.MaPhanCong,
                            p => p.MaPhanCong,
                            (h, p) => p.MaNhanVienThucHien)
                )
                .GroupBy(maNv => maNv)
                .Select(g => new { MaNhanVien = g.Key, SoCongViec = g.Count() })
                .ToListAsync();

            var demMap = demTheoNv.ToDictionary(x => x.MaNhanVien, x => x.SoCongViec);

            var list = await query
                .Select(nv => new
                {
                    maNhanVien = nv.MaNhanVien,
                    hoTen = nv.HoTen,
                    email = nv.Email,
                    soDienThoai = nv.SoDienThoai,
                    chucVu = nv.ChucVu,
                    trangThai = nv.TrangThai,
                    tenVaiTro = nv.MaNguoiDungNavigation.MaVaiTroNavigation.TenVaiTro
                })
                .ToListAsync();

            return list
                .Select(nv =>
                {
                    var soCv = demMap.TryGetValue(nv.maNhanVien, out var n) ? n : 0;
                    // Chỉ rảnh khi 0/3 — 1/3, 2/3, 3/3 đều không chọn
                    var khongNhanThem = soCv > 0;
                    return (object)new
                    {
                        nv.maNhanVien,
                        nv.hoTen,
                        nv.email,
                        nv.soDienThoai,
                        nv.chucVu,
                        nv.trangThai,
                        nv.tenVaiTro,
                        soCongViecDangLam = soCv,
                        soCongViecToiDa = toiDa,
                        dangBan = khongNhanThem,
                        ghiChuBan = khongNhanThem
                            ? $"Đang đảm nhận {soCv}/{toiDa} thiết bị — hoàn thành hết (về 0/{toiDa}) mới được phân công thêm"
                            : (string?)null
                    };
                })
                .ToList();
        }




 
        public async Task<List<(VaiTro, int)>> GetAllVaiTroWithUserCountAsync()
        {
            var data = await _context.VaiTros
                .Select(v => new { VaiTro = v, SoNguoiDung = v.QuanLyNguoiDungs.Count })
                .AsNoTracking()
                .ToListAsync();
            return data.Select(x => (x.VaiTro, x.SoNguoiDung)).ToList();
        }
 
        public async Task<bool> VaiTroDangCoNguoiDungAsync(int maVaiTro) =>
            await _context.QuanLyNguoiDungs.AnyAsync(u => u.MaVaiTro == maVaiTro);
 
        public async Task<List<DanhMucChucNang>> GetChucNangGroupedAsync() =>
            await _context.DanhMucChucNangs
                .AsNoTracking()
                .OrderBy(c => c.NhomChucNang)
                .ToListAsync();
 
        public async Task<List<PhanQuyenVaiTro>> GetPhanQuyenByVaiTroAsync(int maVaiTro) =>
            await _context.PhanQuyenVaiTros
                .Where(p => p.MaVaiTro == maVaiTro)
                .AsNoTracking()
                .ToListAsync();
 
        public async Task XoaPhanQuyenTheoVaiTroAsync(int maVaiTro)
        {
            var cu = await _context.PhanQuyenVaiTros.Where(p => p.MaVaiTro == maVaiTro).ToListAsync();
            _context.PhanQuyenVaiTros.RemoveRange(cu);
        }
 
        public async Task ThemDanhSachPhanQuyenAsync(List<PhanQuyenVaiTro> danhSach) =>
            await _context.PhanQuyenVaiTros.AddRangeAsync(danhSach);
 
        public async Task<List<NhatKyHeThong>> GetNhatKyAsync(
            string? tuKhoa, string? phuongThucHTTP, DateTime? tuNgay, DateTime? denNgay)
        {
            var query = _context.NhatKyHeThongs.Include(n => n.MaNhanVienNavigation).AsQueryable();
 
            if (!string.IsNullOrWhiteSpace(tuKhoa))
                query = query.Where(n => n.TenApi.Contains(tuKhoa) ||
                                          n.MaNhanVienNavigation.HoTen.Contains(tuKhoa));
 
            if (!string.IsNullOrWhiteSpace(phuongThucHTTP))
                query = query.Where(n => n.PhuongThucHttp == phuongThucHTTP);
 
            if (tuNgay.HasValue)
                query = query.Where(n => n.ThoiGianTruyCap >= tuNgay.Value);
 
            if (denNgay.HasValue)
                query = query.Where(n => n.ThoiGianTruyCap <= denNgay.Value);
 
            return await query.OrderByDescending(n => n.ThoiGianTruyCap).AsNoTracking().ToListAsync();
        }
    }
}