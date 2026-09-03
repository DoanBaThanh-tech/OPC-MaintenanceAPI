using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.QuanLyNguoiDung;
using OPC.MaintenanceAPI.Helpers;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QuanLyNguoiDungController : ControllerBase
    {
        private readonly OPCDbContext _context;
        private readonly IConfiguration _config;

        public QuanLyNguoiDungController(OPCDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // Quy tắc mật khẩu: tối thiểu 8 ký tự, có ít nhất 1 chữ hoa, 1 ký tự đặc biệt
        private static readonly Regex MatKhauHopLe =
            new(@"^(?=.*[A-Z])(?=.*[!@#$%^&*(),.?"":{}|<>_\-]).{8,}$");

        // GET: api/QuanLyNguoiDung
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.QuanLyNguoiDungs
                .AsNoTracking()
                .OrderBy(u => u.MaNguoiDung)
                .Select(u => new
                {
                    u.MaNguoiDung,
                    u.Email,
                    u.MaVaiTro,
                    TenVaiTro = u.MaVaiTroNavigation.TenVaiTro,
                    u.TrangThai,
                    u.LanDangNhapCuoi,
                    u.NgayTao
                })
                .ToListAsync();

            return Ok(data);
        }

        // GET: api/QuanLyNguoiDung/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var user = await _context.QuanLyNguoiDungs
                .AsNoTracking()
                .Where(u => u.MaNguoiDung == id)
                .Select(u => new
                {
                    u.MaNguoiDung,
                    u.Email,
                    u.MaVaiTro,
                    TenVaiTro = u.MaVaiTroNavigation.TenVaiTro,
                    u.TrangThai,
                    u.LanDangNhapCuoi,
                    u.NgayTao
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return NotFound(new { Message = "Không tìm thấy tài khoản." });

            return Ok(user);
        }

        // POST: api/QuanLyNguoiDung  (Admin tạo tài khoản + hồ sơ nhân viên đồng thời)
        // Chỉ tài khoản đang đăng nhập với vai trò "Admin hệ thống" mới được gọi API này
        [Authorize(Roles = "Admin hệ thống")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TaoTaiKhoanDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!MatKhauHopLe.IsMatch(dto.MatKhau))
                return BadRequest(new { Message = "Mật khẩu phải có ít nhất 8 ký tự, gồm 1 chữ in hoa và 1 ký tự đặc biệt." });

            if (await _context.QuanLyNguoiDungs.AnyAsync(u => u.Email == dto.Email))
                return BadRequest(new { Message = "Email đã tồn tại." });

            if (!await _context.VaiTros.AnyAsync(v => v.MaVaiTro == dto.MaVaiTro))
                return BadRequest(new { Message = "MaVaiTro không tồn tại." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var taiKhoan = new QuanLyNguoiDung
                {
                    Email     = dto.Email,
                    MatKhau   = BCrypt.Net.BCrypt.HashPassword(dto.MatKhau),
                    MaVaiTro  = dto.MaVaiTro,
                    TrangThai = "Chưa kích hoạt"
                };
                _context.QuanLyNguoiDungs.Add(taiKhoan);
                await _context.SaveChangesAsync();

                var nhanVien = new NhanVien
                {
                    MaNguoiDung = taiKhoan.MaNguoiDung,
                    HoTen       = dto.HoTen,
                    SoDienThoai = dto.SoDienThoai
                };
                _context.NhanViens.Add(nhanVien);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return CreatedAtAction(nameof(GetById), new { id = taiKhoan.MaNguoiDung },
                    new { taiKhoan.MaNguoiDung, taiKhoan.Email, taiKhoan.TrangThai });
            }
            catch
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { Message = "Tạo tài khoản thất bại." });
            }
        }

        // POST: api/QuanLyNguoiDung/dang-nhap
        // Ai cũng gọi được (không cần [Authorize]) - vì đây chính là bước để LẤY token
        [HttpPost("dang-nhap")]
        public async Task<IActionResult> DangNhap([FromBody] DangNhapDto dto)
        {
            var user = await _context.QuanLyNguoiDungs
                .Include(u => u.MaVaiTroNavigation)
                .FirstOrDefaultAsync(u => u.Email == dto.Email);

            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.MatKhau, user.MatKhau))
                return Unauthorized(new { Message = "Email hoặc mật khẩu không đúng." });

            if (user.TrangThai == "Đã khóa")
                return Unauthorized(new { Message = "Tài khoản đã bị khóa." });

            if (user.TrangThai == "Chưa kích hoạt")
                return Unauthorized(new { Message = "Tài khoản chưa được kích hoạt." });

            user.LanDangNhapCuoi = DateTime.Now;
            await _context.SaveChangesAsync();

            var token = JwtHelper.TaoToken(
                user.MaNguoiDung,
                user.Email,
                user.MaVaiTro,
                user.MaVaiTroNavigation.TenVaiTro,
                _config);

            return Ok(new
            {
                user.MaNguoiDung,
                user.Email,
                VaiTro = user.MaVaiTroNavigation.TenVaiTro,
                user.MaVaiTro,
                Token = token
            });
        }

        // PUT: api/QuanLyNguoiDung/5/khoa  (Admin khóa tài khoản)
        [Authorize(Roles = "Admin hệ thống")]
        [HttpPut("{id}/khoa")]
        public async Task<IActionResult> KhoaTaiKhoan(int id)
        {
            var user = await _context.QuanLyNguoiDungs.FindAsync(id);
            if (user == null)
                return NotFound(new { Message = "Không tìm thấy tài khoản." });

            user.TrangThai = "Đã khóa";
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Đã khóa tài khoản." });
        }

        // PUT: api/QuanLyNguoiDung/5/kich-hoat  (Admin kích hoạt tài khoản)
        [Authorize(Roles = "Admin hệ thống")]
        [HttpPut("{id}/kich-hoat")]
        public async Task<IActionResult> KichHoatTaiKhoan(int id)
        {
            var user = await _context.QuanLyNguoiDungs.FindAsync(id);
            if (user == null)
                return NotFound(new { Message = "Không tìm thấy tài khoản." });

            user.TrangThai = "Đang hoạt động";
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Đã kích hoạt tài khoản." });
        }
    }
}