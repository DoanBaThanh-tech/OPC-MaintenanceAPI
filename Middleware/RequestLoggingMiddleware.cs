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
            var methodEarly = (context.Request.Method ?? "GET").ToUpperInvariant();
            // Chỉ log GET / POST / PUT (PATCH coi như PUT). Bỏ DELETE.
            var skip = BoQuaDuongDan.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                       || methodEarly == "DELETE"
                       || methodEarly is not ("GET" or "POST" or "PUT" or "PATCH");

            // Buffer body để đọc field (POST/PUT/PATCH) trước khi pipeline chạy
            string? bodyPreview = null;
            // Method có thể null theo nullable analysis — dùng bản đã chuẩn hóa
            var isWrite = methodEarly is "POST" or "PUT" or "PATCH";
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
                    // Cho phép body lớn hơn để ghi đủ field + giá trị (tối đa ~16KB)
                    if (raw.Length > 0 && raw.Length <= 16384)
                        bodyPreview = TomTatBodyGiaTri(raw);
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
                // Lưu đúng method HTTP — không dùng CRUD
                var loai = method switch
                {
                    "PATCH" => "PUT",
                    "GET" or "POST" or "PUT" => method,
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

        /// <summary>Tóm tắt body JSON kèm giá trị field (ẩn mật khẩu/token).</summary>
        private static string? TomTatBodyGiaTri(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var lines = new List<string>();
                CollectFieldValues(doc.RootElement, lines, "", 0);
                lines = lines
                    .Where(l => !l.Contains("password", StringComparison.OrdinalIgnoreCase)
                                && !l.Contains("matKhau", StringComparison.OrdinalIgnoreCase)
                                && !l.Contains("token", StringComparison.OrdinalIgnoreCase)
                                && !l.Contains("matkhau", StringComparison.OrdinalIgnoreCase))
                    .Take(40)
                    .ToList();
                if (lines.Count == 0) return null;
                var s = string.Join("\n", lines);
                return s.Length > 2800 ? s[..2800] + "…" : s;
            }
            catch
            {
                var s = raw.Trim();
                if (s.Length > 400) s = s[..400] + "…";
                return s;
            }
        }

        private static void CollectFieldValues(JsonElement el, List<string> lines, string prefix, int depth)
        {
            if (depth > 5 || lines.Count >= 40) return;
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in el.EnumerateObject())
                    {
                        var name = string.IsNullOrEmpty(prefix) ? prop.Name : prefix + "." + prop.Name;
                        if (prop.Value.ValueKind is JsonValueKind.Object)
                            CollectFieldValues(prop.Value, lines, name, depth + 1);
                        else if (prop.Value.ValueKind is JsonValueKind.Array)
                            CollectFieldValues(prop.Value, lines, name, depth + 1);
                        else
                            lines.Add($"{name} = {FormatJsonValue(prop.Value)}");
                    }
                    break;
                case JsonValueKind.Array:
                    var i = 0;
                    var count = el.GetArrayLength();
                    foreach (var item in el.EnumerateArray())
                    {
                        if (i >= 5)
                        {
                            lines.Add($"{prefix} … (+{count - 5} phần tử)");
                            break;
                        }
                        if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                            CollectFieldValues(item, lines, $"{prefix}[{i}]", depth + 1);
                        else
                            lines.Add($"{prefix}[{i}] = {FormatJsonValue(item)}");
                        i++;
                    }
                    break;
                default:
                    if (!string.IsNullOrEmpty(prefix))
                        lines.Add($"{prefix} = {FormatJsonValue(el)}");
                    break;
            }
        }

        private static string FormatJsonValue(JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.String =>
                    Truncate(el.GetString() ?? "", 120),
                JsonValueKind.Number => el.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "null",
                _ => Truncate(el.GetRawText(), 120)
            };
        }

        private static string Truncate(string s, int max)
            => s.Length <= max ? s : s[..max] + "…";

        private static string? TomTatQuery(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            var q = query.StartsWith("?") ? query[1..] : query;
            var parts = q.Split('&', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            var lines = new List<string>();
            foreach (var p in parts.Take(20))
            {
                var idx = p.IndexOf('=');
                if (idx <= 0)
                {
                    lines.Add(Uri.UnescapeDataString(p));
                    continue;
                }
                var k = Uri.UnescapeDataString(p[..idx]);
                var v = Uri.UnescapeDataString(p[(idx + 1)..]);
                if (k.Contains("password", StringComparison.OrdinalIgnoreCase)
                    || k.Contains("token", StringComparison.OrdinalIgnoreCase)
                    || k.Contains("matKhau", StringComparison.OrdinalIgnoreCase))
                    continue;
                lines.Add($"{k} = {Truncate(v, 120)}");
            }
            return lines.Count == 0 ? null : string.Join("\n", lines);
        }

        private static string TaoChiTiet(string method, string path, string? query, string? bodyFields, int status)
        {
            var m = method.ToUpperInvariant();
            if (m == "PATCH") m = "PUT";
            var hanhDong = m switch
            {
                "GET" => "GET — Người dùng lấy / xem dữ liệu",
                "POST" => "POST — Người dùng tạo mới hoặc gửi xử lý",
                "PUT" => "PUT — Người dùng cập nhật / chỉnh sửa dữ liệu",
                _ => $"{m} — Gọi API"
            };
            var trangThai = status >= 200 && status < 300
                ? $"{status} Thành công"
                : status >= 400 && status < 500
                    ? $"{status} Lỗi client"
                    : status >= 500
                        ? $"{status} Lỗi server"
                        : status.ToString();

            var sb = new StringBuilder();
            sb.AppendLine(hanhDong);
            sb.AppendLine($"API: {path}");
            sb.AppendLine($"Kết quả: {trangThai}");

            var queryLines = TomTatQuery(query);
            if (!string.IsNullOrEmpty(queryLines))
            {
                sb.AppendLine("── Tham số truy vấn (GET/URL) ──");
                sb.AppendLine(queryLines);
            }

            if (!string.IsNullOrEmpty(bodyFields))
            {
                sb.AppendLine(m == "GET"
                    ? "── Dữ liệu liên quan ──"
                    : "── Dữ liệu gửi lên (body) ──");
                sb.AppendLine(bodyFields);
            }
            else if (m is "POST" or "PUT")
            {
                sb.AppendLine("── Dữ liệu gửi lên (body) ──");
                sb.AppendLine("(không có body hoặc body rỗng)");
            }

            var s = sb.ToString().Trim();
            return s.Length > 3500 ? s[..3500] + "…" : s;
        }
    }
}