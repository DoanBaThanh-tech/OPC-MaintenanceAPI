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
 
        Task<List<PhanQuyenVaiTro>> GetPhanQuyenByVaiTroAsync(int maVaiTro);
        Task XoaPhanQuyenTheoVaiTroAsync(int maVaiTro);
        Task ThemDanhSachPhanQuyenAsync(List<PhanQuyenVaiTro> danhSach);
 
        Task<List<NhatKyHeThong>> GetNhatKyAsync(string? tuKhoa, string? phuongThucHTTP, DateTime? tuNgay, DateTime? denNgay);
    }

    public class SystemRepository : BaseRepository<VaiTro>, ISystemRepository
    {
        public SystemRepository(OPCDbContext context) : base(context) { }
 
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