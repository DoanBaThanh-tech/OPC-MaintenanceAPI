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
    }

    public class QuanLyNguoiDungRepository : BaseRepository<QuanLyNguoiDung>, IQuanLyNguoiDungRepository
    {
        public QuanLyNguoiDungRepository(OPCDbContext context) : base(context) { }
        
        public Task<QuanLyNguoiDung?> GetByEmailAsync(string email) =>
            _dbSet.Include(u => u.MaVaiTroNavigation).FirstOrDefaultAsync(u => u.Email == email);

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
    }

    public class NhanVienRepository : BaseRepository<NhanVien>, INhanVienRepository
    {
        public NhanVienRepository(OPCDbContext context) : base(context) { }

        public Task<NhanVien?> GetByMaNguoiDungAsync(int maNguoiDung) =>
            _dbSet.FirstOrDefaultAsync(n => n.MaNguoiDung == maNguoiDung);
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