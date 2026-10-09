using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using System.Threading.RateLimiting;
using OPC.MaintenanceAPI.Middleware;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Implementations;
using OPC.MaintenanceAPI.Services.Interfaces;
using OPC.MaintenanceAPI.Core.Authorization;
using OPC.MaintenanceAPI.Data.Seed;
using OPC.MaintenanceAPI.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
var builder = WebApplication.CreateBuilder(args);

// ===== Đăng ký Repository + Service theo đúng 6 nhóm Controller =====

// Auth: QuanLyNguoiDung, NhanVien, XacThucQuenMatKhau
builder.Services.AddScoped<IQuanLyNguoiDungRepository, QuanLyNguoiDungRepository>();
builder.Services.AddScoped<INhanVienRepository, NhanVienRepository>();
builder.Services.AddScoped<IXacThucQuenMatKhauRepository, XacThucQuenMatKhauRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, QuyenPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, QuyenAuthorizationHandler>();


// System: VaiTro, PhanQuyenVaiTro, DanhMucChucNang, NhatKyHeThong
builder.Services.AddScoped<ISystemRepository, SystemRepository>();
builder.Services.AddScoped<ISystemService, SystemService>();

// Equipment: ThietBi, LichSuThietBi
builder.Services.AddScoped<IEquipmentRepository, EquipmentRepository>();
builder.Services.AddScoped<IEquipmentService, EquipmentService>();

// MaintenancePlan: KeHoachBaoTri, ChiTietKeHoachBaoTri, ChuKyBaoTri
builder.Services.AddScoped<IMaintenancePlanRepository, MaintenancePlanRepository>();
builder.Services.AddScoped<IMaintenancePlanService, MaintenancePlanService>();

// WorkOrder: HoSoBaoTri, HoSoSuaChua, PhanCongCongViec, KetQuaThucHien, LichSuPheDuyet
builder.Services.AddScoped<IWorkOrderRepository, WorkOrderRepository>();
builder.Services.AddScoped<IWorkOrderService, WorkOrderService>();

// Inventory: VatTu, HoSoYeuCauVatTu, ChiTietYeuCauVatTu, NhapXuatVatTu
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IInventoryService, InventoryService>();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        o.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập token theo dạng: Bearer {token}"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowApp", policy =>
    {
        // Dev: mở. Production: chỉ origin app nội bộ (nếu có web).
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            var origins = builder.Configuration.GetSection("Security:CorsOrigins").Get<string[]>()
                          ?? Array.Empty<string>();
            if (origins.Length > 0)
                policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
            else
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
    });
});
builder.Services.AddDbContext<OPCDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Chống brute-force đăng nhập (in-memory)
builder.Services.AddSingleton<LoginAttemptGuard>();

// Rate limiting: login chặt, API chung vừa phải
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync(
            """{"loi":"Quá nhiều request. Vui lòng thử lại sau ít phút."}""", token);
    };

    // Đăng nhập / quên MK: 10 req / phút / IP
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // API chung: 180 req / phút / IP (kể cả đã đăng nhập)
    options.AddPolicy("api", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 180,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 240,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

await VaiTroSeeder.SeedAsync(app.Services);
await NhanVienKyThuatSeeder.SeedAsync(app.Services);
await ToTruongSanXuatSeeder.SeedAsync(app.Services);

app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger / OpenAPI chỉ bật khi Development — production không lộ danh sách API
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "OPC Maintenance API v1");
        c.RoutePrefix = "swagger";
    });
    app.MapOpenApi();
}

// Chỉ redirect HTTPS khi thật sự có https port
var httpsPort = Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORT");
if (!string.IsNullOrEmpty(httpsPort) || app.Configuration["HTTPS_PORT"] != null)
{
    app.UseHttpsRedirection();
}

// IP allowlist (nếu Security:AllowedIps có phần tử)
app.UseMiddleware<IpAllowlistMiddleware>();

app.UseCors("AllowApp");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RequestLoggingMiddleware>();
app.MapControllers().RequireRateLimiting("api");
app.Run();