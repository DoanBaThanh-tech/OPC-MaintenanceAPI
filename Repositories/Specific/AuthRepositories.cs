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
    }

    public class QuanLyNguoiDungRepository : BaseRepository<QuanLyNguoiDung>, IQuanLyNguoiDungRepository
    {
        public QuanLyNguoiDungRepository(OPCDbContext context) : base(context) { }

        public Task<QuanLyNguoiDung?> GetByEmailAsync(string email) =>
            _dbSet.Include(u => u.MaVaiTroNavigation).FirstOrDefaultAsync(u => u.Email == email);

        public Task<bool> EmailExistsAsync(string email) =>
            _dbSet.AnyAsync(u => u.Email == email);
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
    }
}