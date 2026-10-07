using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Core.Entities;

namespace OPC.MaintenanceAPI.Middleware
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;

        private static readonly string[] BoQuaDuongDan =
        {
            "/swagger",
            "/api/QuanLyNguoiDung/dang-nhap",
            "/api/system/nhatky",
            "/api/System/nhatky",
        };

        public RequestLoggingMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context, OPCDbContext dbContext)
        {
            var path = context.Request.Path.Value ?? "";
            var skip = BoQuaDuongDan.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            // Buffer body để đọc field (POST/PUT/PATCH) trước khi pipeline chạy
            string? bodyPreview = null;
            var isWrite = HttpMethods.IsPost(context.Request.Method)
                          || HttpMethods.IsPut(context.Request.Method)
                          || HttpMethods.IsPatch(context.Request.Method);
            if (!skip && isWrite)
            {
                context.Request.EnableBuffering();
                try
                {
                    using var reader = new StreamReader(
                        context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                        bufferSize: 1024, leaveOpen: true);
                    var raw = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0;
                    if (raw.Length > 0 && raw.Length <= 8192)
                        bodyPreview = TomTatBody(raw);
                }
                catch
                {
                    try { context.Request.Body.Position = 0; } catch { /* ignore */ }
                }
            }

            await _next(context);

            if (skip) return;
            if (context.User?.Identity?.IsAuthenticated != true) return;

            var maNguoiDungClaim = context.User.FindFirstValue("MaNguoiDung");
            if (maNguoiDungClaim == null) return;

            try
            {
                var nhanVien = await dbContext.NhanViens
                    .AsNoTracking()
                    .FirstOrDefaultAsync(n => n.MaNguoiDung == int.Parse(maNguoiDungClaim));
                if (nhanVien == null) return;

                var method = (context.Request.Method ?? "GET").ToUpperInvariant();
                var loai = method switch
                {
                    "GET" => "Read",
                    "POST" => "Create",
                    "PUT" or "PATCH" => "Update",
                    "DELETE" => "Delete",
                    _ => method
                };

                var query = context.Request.QueryString.HasValue
                    ? context.Request.QueryString.Value
                    : null;
                if (query != null && query.Length > 500)
                    query = query[..500];

                var chiTiet = TaoChiTiet(method, path, query, bodyPreview, context.Response.StatusCode);

                dbContext.NhatKyHeThongs.Add(new NhatKyHeThong
                {
                    MaNhanVien = nhanVien.MaNhanVien,
                    TenApi = path.Length > 200 ? path[..200] : path,
                    PhuongThucHttp = method.Length > 10 ? method[..10] : method,
                    ThoiGianTruyCap = DateTime.Now,
                    DiaChiIp = context.Connection.RemoteIpAddress?.ToString(),
                    StatusCode = context.Response.StatusCode,
                    QueryString = query,
                    LoaiHanhDong = loai,
                    ChiTiet = chiTiet
                });

                await dbContext.SaveChangesAsync();
            }
            catch
            {
                // Không làm fail request vì log
            }
        }

        private static string? TomTatBody(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var keys = new List<string>();
                CollectKeys(doc.RootElement, keys, "");
                // Ẩn field nhạy cảm
                keys = keys
                    .Where(k => !k.Contains("password", StringComparison.OrdinalIgnoreCase)
                                && !k.Contains("matKhau", StringComparison.OrdinalIgnoreCase)
                                && !k.Contains("token", StringComparison.OrdinalIgnoreCase))
                    .Take(24)
                    .ToList();
                if (keys.Count == 0) return null;
                var s = "Fields: " + string.Join(", ", keys);
                return s.Length > 900 ? s[..900] : s;
            }
            catch
            {
                var s = raw.Trim();
                if (s.Length > 200) s = s[..200] + "…";
                return "Body: " + s;
            }
        }

        private static void CollectKeys(JsonElement el, List<string> keys, string prefix)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in el.EnumerateObject())
                    {
                        var name = string.IsNullOrEmpty(prefix) ? prop.Name : prefix + "." + prop.Name;
                        if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                            CollectKeys(prop.Value, keys, name);
                        else
                            keys.Add(name);
                    }
                    break;
                case JsonValueKind.Array:
                    var i = 0;
                    foreach (var item in el.EnumerateArray())
                    {
                        if (i >= 3) { keys.Add(prefix + "[]…"); break; }
                        CollectKeys(item, keys, prefix + $"[{i}]");
                        i++;
                    }
                    break;
            }
        }

        private static string TaoChiTiet(string method, string path, string? query, string? bodyFields, int status)
        {
            var parts = new List<string>
            {
                $"HTTP {method} → {status}",
                $"API: {path}"
            };
            if (!string.IsNullOrEmpty(query))
                parts.Add("Query: " + query);
            if (!string.IsNullOrEmpty(bodyFields))
                parts.Add(bodyFields);
            var s = string.Join(" | ", parts);
            return s.Length > 2000 ? s[..2000] : s;
        }
    }
}
