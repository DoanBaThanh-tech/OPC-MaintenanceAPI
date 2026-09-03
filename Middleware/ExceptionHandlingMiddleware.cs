using System.Net;
using System.Text.Json;

namespace OPC.MaintenanceAPI.Middleware
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi hệ thống tại {Path}", context.Request.Path);

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

                var result = JsonSerializer.Serialize(new
                {
                    Message = "Đã có lỗi xảy ra ở hệ thống, vui lòng thử lại sau hoặc liên hệ quản trị viên.",
                    ErrorId = Guid.NewGuid().ToString("N")[..8]
                });

                await context.Response.WriteAsync(result);
            }
        }
    }
}