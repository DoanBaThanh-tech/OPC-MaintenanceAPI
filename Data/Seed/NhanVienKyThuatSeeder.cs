using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;

namespace OPC.MaintenanceAPI.Data.Seed
{
    /// <summary>
    /// Seed nhân viên kỹ thuật đầy đủ (QuanLyNguoiDung + NhanVien) nếu chưa có.
    /// Idempotent — chạy lại an toàn.
    /// Mật khẩu mặc định: 123456aA@
    /// </summary>
    public static class NhanVienKyThuatSeeder
    {
        private static readonly string MatKhauMacDinhHash =
            BCrypt.Net.BCrypt.HashPassword("123456aA@");

        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OPCDbContext>();

            // Đảm bảo có vai trò "Nhân viên kỹ thuật"
            var vaiTro = await db.VaiTros.FirstOrDefaultAsync(v => v.TenVaiTro == "Nhân viên kỹ thuật");
            if (vaiTro == null)
            {
                vaiTro = new VaiTro { TenVaiTro = "Nhân viên kỹ thuật", CapDoQuyen = 3 };
                db.VaiTros.Add(vaiTro);
                await db.SaveChangesAsync();
            }

            var mau = new[]
            {
                new { Email = "nvkt1@opc.com", HoTen = "Phạm Văn Kỹ 1", Sdt = "0901000004", ChucVu = "Nhân viên kỹ thuật" },
                new { Email = "nvkt2@opc.com", HoTen = "Hoàng Thị Kỹ 2", Sdt = "0901000005", ChucVu = "Nhân viên kỹ thuật" },
                new { Email = "nvkt3@opc.com", HoTen = "Võ Minh Kỹ 3", Sdt = "0901000006", ChucVu = "Nhân viên kỹ thuật" },
                new { Email = "nvkt4@opc.com", HoTen = "Nguyễn Văn An", Sdt = "0901000007", ChucVu = "Nhân viên kỹ thuật" },
                new { Email = "nvkt5@opc.com", HoTen = "Trần Thị Bình", Sdt = "0901000008", ChucVu = "Nhân viên kỹ thuật" },
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
                        MatKhau = MatKhauMacDinhHash,
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
                        ChucVu = m.ChucVu,
                        NgayVaoLam = DateOnly.FromDateTime(DateTime.Now.AddYears(-1)),
                        TrangThai = "Đang làm việc",
                        NgayTao = DateTime.Now,
                    });
                    await db.SaveChangesAsync();
                }
                else
                {
                    // Bổ sung / cập nhật thông tin NhanVien nếu thiếu
                    if (user.NhanVien == null)
                    {
                        db.NhanViens.Add(new NhanVien
                        {
                            MaNguoiDung = user.MaNguoiDung,
                            HoTen = m.HoTen,
                            Email = m.Email,
                            SoDienThoai = m.Sdt,
                            ChucVu = m.ChucVu,
                            NgayVaoLam = DateOnly.FromDateTime(DateTime.Now.AddYears(-1)),
                            TrangThai = "Đang làm việc",
                            NgayTao = DateTime.Now,
                        });
                    }
                    else
                    {
                        var nv = user.NhanVien;
                        if (string.IsNullOrWhiteSpace(nv.HoTen)) nv.HoTen = m.HoTen;
                        if (string.IsNullOrWhiteSpace(nv.Email)) nv.Email = m.Email;
                        if (string.IsNullOrWhiteSpace(nv.SoDienThoai)) nv.SoDienThoai = m.Sdt;
                        if (string.IsNullOrWhiteSpace(nv.ChucVu)) nv.ChucVu = m.ChucVu;
                        if (string.IsNullOrWhiteSpace(nv.TrangThai)) nv.TrangThai = "Đang làm việc";
                    }

                    if (user.MaVaiTro != vaiTro.MaVaiTro)
                        user.MaVaiTro = vaiTro.MaVaiTro;
                    if (user.TrangThai != "Đang hoạt động")
                        user.TrangThai = "Đang hoạt động";

                    await db.SaveChangesAsync();
                }
            }
        }
    }
}