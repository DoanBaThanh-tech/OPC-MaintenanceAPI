using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;

namespace OPC.MaintenanceAPI.Data.Seed
{
    /// <summary>
    /// Seed / đồng bộ 10 nhân viên kỹ thuật (QuanLyNguoiDung + NhanVien).
    /// - Tên đầy đủ, không có số trong họ tên.
    /// - Mật khẩu chung: 123456aA@
    /// - Idempotent: chạy lại khi start API vẫn an toàn.
    /// </summary>
    public static class NhanVienKyThuatSeeder
    {
        /// <summary>Mật khẩu đăng nhập chung cho toàn bộ NVKT seed.</summary>
        public const string MatKhauChung = "123456aA@";

        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OPCDbContext>();

            var vaiTro = await db.VaiTros.FirstOrDefaultAsync(v => v.TenVaiTro == "Nhân viên kỹ thuật");
            if (vaiTro == null)
            {
                vaiTro = new VaiTro
                {
                    TenVaiTro = "Nhân viên kỹ thuật",
                    CapDoQuyen = 3,
                    MoTa = "Nhân viên thực hiện bảo trì / sửa chữa",
                    TrangThai = true,
                    NgayTao = DateTime.Now
                };
                db.VaiTros.Add(vaiTro);
                await db.SaveChangesAsync();
            }

            var matKhauHash = BCrypt.Net.BCrypt.HashPassword(MatKhauChung);

            var mau = new[]
            {
                new { Email = "hung.nguyen@opc.com",   HoTen = "Nguyễn Văn Hùng",  Sdt = "0902000001", ChucVu = "Nhân viên kỹ thuật" },
                new { Email = "mai.tran@opc.com",      HoTen = "Trần Thị Mai",     Sdt = "0902000002", ChucVu = "Nhân viên kỹ thuật" },
                new { Email = "tuan.le@opc.com",       HoTen = "Lê Minh Tuấn",     Sdt = "0902000003", ChucVu = "Nhân viên kỹ thuật" },
                new { Email = "huong.pham@opc.com",    HoTen = "Phạm Thị Hương",   Sdt = "0902000004", ChucVu = "Nhân viên kỹ thuật" },
                new { Email = "duc.hoang@opc.com",     HoTen = "Hoàng Văn Đức",    Sdt = "0902000005", ChucVu = "Nhân viên kỹ thuật" },
                new { Email = "lan.vo@opc.com",        HoTen = "Võ Thị Lan",       Sdt = "0902000006", ChucVu = "Nhân viên kỹ thuật" },
                new { Email = "bao.dang@opc.com",      HoTen = "Đặng Quốc Bảo",    Sdt = "0902000007", ChucVu = "Nhân viên kỹ thuật" },
                new { Email = "ngoc.bui@opc.com",      HoTen = "Bùi Thị Ngọc",     Sdt = "0902000008", ChucVu = "Nhân viên kỹ thuật" },
                new { Email = "phong.phan@opc.com",    HoTen = "Phan Thanh Phong", Sdt = "0902000009", ChucVu = "Nhân viên kỹ thuật" },
                new { Email = "ha.luong@opc.com",      HoTen = "Lương Thị Hà",     Sdt = "0902000010", ChucVu = "Nhân viên kỹ thuật" },
            };

            var emailSeed = mau.Select(m => m.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Xóa / vô hiệu hóa NVKT cũ không thuộc 10 email seed
            var nvktCu = await db.QuanLyNguoiDungs
                .Include(u => u.NhanVien)
                .Where(u => u.MaVaiTro == vaiTro.MaVaiTro)
                .ToListAsync();

            foreach (var u in nvktCu)
            {
                if (emailSeed.Contains(u.Email ?? ""))
                    continue;

                var maNv = u.NhanVien?.MaNhanVien;
                if (maNv == null)
                {
                    db.QuanLyNguoiDungs.Remove(u);
                    continue;
                }

                var dangDung =
                    await db.PhanCongCongViecs.AnyAsync(p =>
                        p.MaNhanVienThucHien == maNv || p.MaNhanVienPhanCong == maNv)
                    || await db.HoSoBaoTris.AnyAsync(h =>
                        h.MaNhanVienTao == maNv || h.MaNhanVienDuyet == maNv)
                    || await db.HoSoSuaChuas.AnyAsync(h =>
                        h.MaNhanVienTao == maNv || h.MaNhanVienDuyet == maNv)
                    || await db.KetQuaThucHiens.AnyAsync(k => k.MaNhanVienGhiNhan == maNv)
                    || await db.KeHoachBaoTris.AnyAsync(k => k.MaNhanVienLap == maNv)
                    || await db.LichSuPheDuyets.AnyAsync(l => l.MaNhanVienDuyet == maNv)
                    || await db.NhatKyHeThongs.AnyAsync(n => n.MaNhanVien == maNv);

                if (dangDung)
                {
                    u.TrangThai = "Ngừng hoạt động";
                    if (u.NhanVien != null)
                    {
                        u.NhanVien.TrangThai = "Nghỉ việc";
                        u.NhanVien.ChucVu = "Nhân viên kỹ thuật (cũ)";
                    }
                    continue;
                }

                if (u.NhanVien != null)
                    db.NhanViens.Remove(u.NhanVien);
                db.QuanLyNguoiDungs.Remove(u);
            }

            await db.SaveChangesAsync();

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
                        MatKhau = matKhauHash,
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
                        NgayVaoLam = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
                        TrangThai = "Đang làm việc",
                        NgayTao = DateTime.Now,
                    });
                    await db.SaveChangesAsync();
                }
                else
                {
                    user.MaVaiTro = vaiTro.MaVaiTro;
                    user.TrangThai = "Đang hoạt động";
                    user.MatKhau = matKhauHash;

                    if (user.NhanVien == null)
                    {
                        db.NhanViens.Add(new NhanVien
                        {
                            MaNguoiDung = user.MaNguoiDung,
                            HoTen = m.HoTen,
                            Email = m.Email,
                            SoDienThoai = m.Sdt,
                            ChucVu = m.ChucVu,
                            NgayVaoLam = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
                            TrangThai = "Đang làm việc",
                            NgayTao = DateTime.Now,
                        });
                    }
                    else
                    {
                        var nv = user.NhanVien;
                        nv.HoTen = m.HoTen;
                        nv.Email = m.Email;
                        nv.SoDienThoai = m.Sdt;
                        nv.ChucVu = m.ChucVu;
                        nv.TrangThai = "Đang làm việc";
                    }

                    await db.SaveChangesAsync();
                }
            }
        }
    }
}
