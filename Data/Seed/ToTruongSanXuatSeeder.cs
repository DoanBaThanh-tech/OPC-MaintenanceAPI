using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;

namespace OPC.MaintenanceAPI.Data.Seed
{
    /// <summary>
    /// Seed vai trò + 2 tài khoản Tổ trưởng sản xuất.
    /// Mật khẩu: 123456aA@
    /// </summary>
    public static class ToTruongSanXuatSeeder
    {
        public const string MatKhauChung = "123456aA@";

        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OPCDbContext>();

            var vaiTro = await db.VaiTros.FirstOrDefaultAsync(v => v.TenVaiTro == "Tổ trưởng sản xuất");
            if (vaiTro == null)
            {
                vaiTro = new VaiTro
                {
                    TenVaiTro = "Tổ trưởng sản xuất",
                    CapDoQuyen = 4,
                    MoTa = "Gửi yêu cầu bảo trì thiết bị theo lịch xưởng",
                    TrangThai = true,
                    NgayTao = DateTime.Now
                };
                db.VaiTros.Add(vaiTro);
                await db.SaveChangesAsync();
            }

            var hash = BCrypt.Net.BCrypt.HashPassword(MatKhauChung);
            var mau = new[]
            {
                new { Email = "ttsx.minh@opc.com", HoTen = "Nguyễn Văn Minh", Sdt = "0903000001" },
                new { Email = "ttsx.lan@opc.com",  HoTen = "Trần Thị Lan",   Sdt = "0903000002" },
            };

            foreach (var m in mau)
            {
                var user = await db.QuanLyNguoiDungs
                    .Include(u => u.NhanVien)
                    .FirstOrDefaultAsync(u => u.Email == m.Email);

                if (user == null)
                {
                    user = new QuanLyNguoiDung
                    {
                        Email = m.Email,
                        MatKhau = hash,
                        MaVaiTro = vaiTro.MaVaiTro,
                        TrangThai = "Đang hoạt động",
                        NgayTao = DateTime.Now,
                    };
                    db.QuanLyNguoiDungs.Add(user);
                    await db.SaveChangesAsync();

                    db.NhanViens.Add(new NhanVien
                    {
                        MaNguoiDung = user.MaNguoiDung,
                        HoTen = m.HoTen,
                        Email = m.Email,
                        SoDienThoai = m.Sdt,
                        ChucVu = "Tổ trưởng sản xuất",
                        NgayVaoLam = DateOnly.FromDateTime(DateTime.Today.AddYears(-2)),
                        TrangThai = "Đang làm việc",
                        NgayTao = DateTime.Now,
                    });
                    await db.SaveChangesAsync();
                }
                else
                {
                    user.MaVaiTro = vaiTro.MaVaiTro;
                    user.TrangThai = "Đang hoạt động";
                    user.MatKhau = hash;
                    if (user.NhanVien != null)
                    {
                        user.NhanVien.HoTen = m.HoTen;
                        user.NhanVien.ChucVu = "Tổ trưởng sản xuất";
                        user.NhanVien.TrangThai = "Đang làm việc";
                    }
                    await db.SaveChangesAsync();
                }
            }
        }
    }
}
