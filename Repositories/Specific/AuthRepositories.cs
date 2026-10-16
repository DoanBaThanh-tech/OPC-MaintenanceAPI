using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Repositories.Base;

namespace OPC.MaintenanceAPI.Repositories.Specific
{
    // ===== QuanLyNguoiDung =====
    public interface IQuanLyNguoiDungRepository : IBaseRepository<QuanLyNguoiDung>
    {
        Task<QuanLyNguoiDung?> GetByEmailAsync(string email);
        Task<bool> EmailExistsAsync(string email);
        Task<int> DemTaiKhoanTheoVaiTroAsync(int maVaiTro, int? loaiTruMaNguoiDung = null);
        Task<VaiTro?> GetVaiTroByIdAsync(int maVaiTro);
        Task<bool> CoQuyenAsync(int maNguoiDung, string tenChucNang, string loaiQuyen);
        Task<List<QuanLyNguoiDung>> GetAllWithDetailsAsync();
    }

    public class QuanLyNguoiDungRepository : BaseRepository<QuanLyNguoiDung>, IQuanLyNguoiDungRepository
    {
        public QuanLyNguoiDungRepository(OPCDbContext context) : base(context) { }
        
        public async Task<bool> CoQuyenAsync(int maNguoiDung, string tenChucNang, string loaiQuyen)
    {
        var user = await _dbSet.FirstOrDefaultAsync(u => u.MaNguoiDung == maNguoiDung);
        if (user == null) return false;

        var quyen = await (
            from pq in _context.PhanQuyenVaiTros
            join cn in _context.DanhMucChucNangs on pq.MaChucNang equals cn.MaChucNang
            where pq.MaVaiTro == user.MaVaiTro && cn.TenChucNang == tenChucNang
            select pq
        ).FirstOrDefaultAsync();

        if (quyen == null) return false;

        return loaiQuyen switch
        {
            "Xem" => quyen.DuocXem,
            "Tao" => quyen.DuocTao,
            "Sua" => quyen.DuocSua,
            "Duyet" => quyen.DuocDuyet,
            _ => false
        };
    }

        public async Task<QuanLyNguoiDung?> GetByEmailAsync(string email)
        {
            var e = (email ?? "").Trim().ToLowerInvariant();
            try { e = e.Normalize(System.Text.NormalizationForm.FormC); } catch { }

            // 1) Khớp chính xác (đã chuẩn hóa)
            var user = await _dbSet.Include(u => u.MaVaiTroNavigation)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == e);
            if (user != null) return user;

            // 2) Fallback: bỏ dấu tiếng Việt (ttsx.lăn ↔ ttsx.lan) — tránh lệch gõ dấu
            static string BoDau(string s)
            {
                var formD = s.Normalize(System.Text.NormalizationForm.FormD);
                var sb = new System.Text.StringBuilder(formD.Length);
                foreach (var c in formD)
                {
                    var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                    if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
                        sb.Append(c);
                }
                return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
            }

            var eNoAccent = BoDau(e);
            if (eNoAccent == e) return null;

            // Lấy id rồi load lại tracked (tránh AsNoTracking làm Update LanDangNhapCuoi lỗi)
            var id = await _dbSet.AsNoTracking()
                .Where(u => u.Email != null)
                .Select(u => new { u.MaNguoiDung, u.Email })
                .ToListAsync();
            var hit = id.FirstOrDefault(u =>
                BoDau(u.Email.Trim().ToLowerInvariant()) == eNoAccent);
            if (hit == null) return null;

            return await _dbSet.Include(u => u.MaVaiTroNavigation)
                .FirstOrDefaultAsync(u => u.MaNguoiDung == hit.MaNguoiDung);
        }

        public async Task<List<QuanLyNguoiDung>> GetAllWithDetailsAsync() =>
            await _dbSet
                .Include(u => u.MaVaiTroNavigation)
                .Include(u => u.NhanVien)
                .OrderByDescending(u => u.NgayTao)
                .AsNoTracking()
                .ToListAsync();

        public Task<bool> EmailExistsAsync(string email) =>
            _dbSet.AnyAsync(u => u.Email == email);

        public Task<int> DemTaiKhoanTheoVaiTroAsync(int maVaiTro, int? loaiTruMaNguoiDung = null) =>
            _dbSet.CountAsync(u => u.MaVaiTro == maVaiTro
                                && u.TrangThai != "Đã khóa"
                                && (loaiTruMaNguoiDung == null || u.MaNguoiDung != loaiTruMaNguoiDung));

        // Dùng thẳng _context kế thừa từ BaseRepository<T>, không cần khai báo field riêng
        public Task<VaiTro?> GetVaiTroByIdAsync(int maVaiTro) =>
            _context.VaiTros.FirstOrDefaultAsync(v => v.MaVaiTro == maVaiTro);
    }

    // ===== NhanVien =====
    public interface INhanVienRepository : IBaseRepository<NhanVien>
    {
        Task<NhanVien?> GetByMaNguoiDungAsync(int maNguoiDung);
        /// <summary>Tìm NV theo email cá nhân (Gmail) đã gắn trên hồ sơ.</summary>
        Task<NhanVien?> GetByEmailLienHeAsync(string email);
        /// <summary>Email cá nhân đã được tài khoản khác dùng chưa (loại trừ chính user hiện tại).</summary>
        Task<bool> EmailLienHeDangDungBoiNguoiKhacAsync(string email, int maNguoiDungHienTai);
    }

    public class NhanVienRepository : BaseRepository<NhanVien>, INhanVienRepository
    {
        public NhanVienRepository(OPCDbContext context) : base(context) { }

        public Task<NhanVien?> GetByMaNguoiDungAsync(int maNguoiDung) =>
            _dbSet.FirstOrDefaultAsync(n => n.MaNguoiDung == maNguoiDung);

        public async Task<NhanVien?> GetByEmailLienHeAsync(string email)
        {
            var e = (email ?? "").Trim().ToLowerInvariant();
            try { e = e.Normalize(System.Text.NormalizationForm.FormC); } catch { }

            var direct = await _dbSet.FirstOrDefaultAsync(n =>
                n.Email != null && n.Email.ToLower() == e);
            if (direct != null) return direct;

            // Fallback không phân biệt hoa thường / khoảng trắng thừa
            var all = await _dbSet.AsNoTracking()
                .Where(n => n.Email != null && n.Email != "")
                .Select(n => new { n.MaNhanVien, n.Email })
                .ToListAsync();
            var hit = all.FirstOrDefault(n =>
                (n.Email ?? "").Trim().ToLowerInvariant() == e);
            if (hit == null) return null;
            return await _dbSet.FirstOrDefaultAsync(n => n.MaNhanVien == hit.MaNhanVien);
        }

        public async Task<bool> EmailLienHeDangDungBoiNguoiKhacAsync(string email, int maNguoiDungHienTai)
        {
            var e = (email ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(e)) return false;
            return await _dbSet.AnyAsync(n =>
                n.MaNguoiDung != maNguoiDungHienTai
                && n.Email != null
                && n.Email.ToLower() == e);
        }
    }

    // ===== XacThucQuenMatKhau =====
    public interface IXacThucQuenMatKhauRepository : IBaseRepository<XacThucQuenMatKhau>
    {
        Task<XacThucQuenMatKhau?> GetHopLeAsync(int maNguoiDung, string maOtp);
        Task<XacThucQuenMatKhau?> GetDaXacNhanAsync(int maNguoiDung);
        Task<XacThucQuenMatKhau?> GetMoiNhatAsync(int maNguoiDung);
    }

    public class XacThucQuenMatKhauRepository : BaseRepository<XacThucQuenMatKhau>, IXacThucQuenMatKhauRepository
    {
        public XacThucQuenMatKhauRepository(OPCDbContext context) : base(context) { }

        public Task<XacThucQuenMatKhau?> GetHopLeAsync(int maNguoiDung, string maOtp) =>
            _dbSet.Where(x => x.MaNguoiDung == maNguoiDung && x.MaOTP == maOtp && x.TrangThaiXacThuc == "Chưa dùng")
                  .OrderByDescending(x => x.MaXacThuc).FirstOrDefaultAsync();

        public Task<XacThucQuenMatKhau?> GetDaXacNhanAsync(int maNguoiDung) =>
            _dbSet.Where(x => x.MaNguoiDung == maNguoiDung && x.TrangThaiXacThuc == "Đã xác nhận")
                  .OrderByDescending(x => x.MaXacThuc).FirstOrDefaultAsync();

        // Điều kiện: lấy lần yêu cầu OTP gần nhất để tính throttle 10 phút
        public Task<XacThucQuenMatKhau?> GetMoiNhatAsync(int maNguoiDung) =>
            _dbSet.Where(x => x.MaNguoiDung == maNguoiDung)
                  .OrderByDescending(x => x.NgayTao)
                  .FirstOrDefaultAsync();
    }
}