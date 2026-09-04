using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using OPC.MaintenanceAPI.Middleware;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Implementations;
using OPC.MaintenanceAPI.Services.Interfaces;
var builder = WebApplication.CreateBuilder(args);

// ===== Đăng ký Repository + Service theo đúng 6 nhóm Controller =====

// Auth: QuanLyNguoiDung, NhanVien, XacThucQuenMatKhau
builder.Services.AddScoped<IQuanLyNguoiDungRepository, QuanLyNguoiDungRepository>();
builder.Services.AddScoped<INhanVienRepository, NhanVienRepository>();
builder.Services.AddScoped<IXacThucQuenMatKhauRepository, XacThucQuenMatKhauRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();

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

builder.Services.AddControllers();
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
        policy.AllowAnyOrigin()   // giai đoạn dev có thể để mở, lên production nên giới hạn domain cụ thể
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
builder.Services.AddDbContext<OPCDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();


app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RequestLoggingMiddleware>();
app.MapControllers();
app.Run();