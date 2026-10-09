namespace OPC.MaintenanceAPI.Middleware
{
    /// <summary>
    /// Chỉ cho phép IP trong danh sách (Security:AllowedIps).
    /// Để trống / không cấu hình → cho phép tất cả (dev).
    /// Hỗ trợ "localhost", "127.0.0.1", "::1".
    /// </summary>
    public class IpAllowlistMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly HashSet<string> _allowed;
        private readonly bool _enabled;

        public IpAllowlistMiddleware(RequestDelegate next, IConfiguration config)
        {
            _next = next;
            var list = config.GetSection("Security:AllowedIps").Get<string[]>() ?? Array.Empty<string>();
            _allowed = new HashSet<string>(
                list.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
                StringComparer.OrdinalIgnoreCase);
            _enabled = _allowed.Count > 0;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_enabled)
            {
                await _next(context);
                return;
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "";
            // Chuẩn hóa IPv6-mapped IPv4
            if (ip.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
                ip = ip["::ffff:".Length..];

            var ok = _allowed.Contains(ip)
                     || (IsLoopback(ip) && (_allowed.Contains("localhost")
                                            || _allowed.Contains("127.0.0.1")
                                            || _allowed.Contains("::1")));

            if (!ok)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    """{"loi":"IP không được phép truy cập API (cấu hình Security:AllowedIps)."}""");
                return;
            }

            await _next(context);
        }

        private static bool IsLoopback(string ip)
            => ip is "127.0.0.1" or "::1" or "localhost";
    }
}
