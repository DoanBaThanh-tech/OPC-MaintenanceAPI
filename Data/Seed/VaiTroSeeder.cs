
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;

namespace OPC.MaintenanceAPI.Data.Seed
{
    // Seeder tách vai trò gộp "Giám đốc/Phó giám đốc" thành 2 vai trò riêng biệt.
    // An toàn chạy lại nhiều lần (idempotent) - sau lần chạy đầu sẽ không còn
    // dòng "Giám đốc/Phó giám đốc" để tìm nữa nên tự động bỏ qua các lần sau.
    public static class VaiTroSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OPCDbContext>();

            var vaiTroGop = await db.VaiTros.FirstOrDefaultAsync(v => v.TenVaiTro == "Giám đốc/Phó giám đốc");
            if (vaiTroGop != null)
            {
                vaiTroGop.TenVaiTro = "Giám đốc";

                db.VaiTros.Add(new VaiTro
                {
                    TenVaiTro = "Phó giám đốc",
                    CapDoQuyen = vaiTroGop.CapDoQuyen
                });
                await db.SaveChangesAsync();
            }

            var taiKhoanTest = await db.QuanLyNguoiDungs
                .FirstOrDefaultAsync(u => u.Email == "giamdoc2@opc.com.vn" && u.TrangThai == "Đang hoạt động");
            if (taiKhoanTest != null)
            {
                taiKhoanTest.TrangThai = "Đã khóa";
                await db.SaveChangesAsync();
            }
        }
    }
}