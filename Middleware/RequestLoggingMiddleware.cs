using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;

namespace OPC.MaintenanceAPI.Middleware
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;

        // Các đường dẫn không cần ghi log (đăng nhập/swagger/log chính nó...)
        private static readonly string[] BoQuaDuongDan = { "/swagger", "/api/QuanLyNguoiDung/dang-nhap" };

        public RequestLoggingMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context, OPCDbContext dbContext)
        {
            await _next(context);   // chạy request thật trước, ghi log sau khi có kết quả

            var path = context.Request.Path.Value ?? "";
            if (BoQuaDuongDan.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                return;

            if (context.User?.Identity?.IsAuthenticated != true)
                return;   // chỉ log request đã đăng nhập, vì cần biết MaNhanVien

            var maNguoiDungClaim = context.User.FindFirstValue("MaNguoiDung");
            if (maNguoiDungClaim == null) return;

            var nhanVien = await dbContext.NhanViens
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.MaNguoiDung == int.Parse(maNguoiDungClaim));
            if (nhanVien == null) return;

            dbContext.NhatKyHeThongs.Add(new NhatKyHeThong
            {
                MaNhanVien     = nhanVien.MaNhanVien,
                TenApi         = path,
                PhuongThucHttp = context.Request.Method,
                DiaChiIp       = context.Connection.RemoteIpAddress?.ToString()
            });

            await dbContext.SaveChangesAsync();
        }
    }
}