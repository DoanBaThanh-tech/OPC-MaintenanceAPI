using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.DTOs.NhanVien;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]   // bắt buộc đăng nhập mới gọi được bất kỳ API nào trong Controller này
    public class NhanVienController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public NhanVienController(OPCDbContext context) => _context = context;

        // GET: api/NhanVien?page=1&pageSize=20
        [HttpGet]
        public async Task<IActionResult> GetAll(int page = 1, int pageSize = 20)
        {
            var total = await _context.NhanViens.CountAsync();

            var data = await _context.NhanViens
                .AsNoTracking()
                .OrderBy(n => n.MaNhanVien)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(n => new
                {
                    n.MaNhanVien,
                    n.HoTen,
                    n.Email,
                    n.SoDienThoai,
                    n.ChucVu,
                    n.TrangThai,
                    TenVaiTro = n.MaNguoiDungNavigation.MaVaiTroNavigation.TenVaiTro
                })
                .ToListAsync();

            return Ok(new
            {
                Page = page,
                PageSize = pageSize,
                TotalRecords = total,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                Data = data
            });
        }

        // GET: api/NhanVien/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var nhanVien = await _context.NhanViens
                .AsNoTracking()
                .Where(n => n.MaNhanVien == id)
                .Select(n => new
                {
                    n.MaNhanVien,
                    n.MaNguoiDung,
                    n.HoTen,
                    n.Email,
                    n.SoDienThoai,
                    n.ChucVu,
                    n.NgayVaoLam,
                    n.TrangThai,
                    n.NgayTao,
                    TenVaiTro = n.MaNguoiDungNavigation.MaVaiTroNavigation.TenVaiTro
                })
                .FirstOrDefaultAsync();

            if (nhanVien == null)
                return NotFound(new { Message = "Không tìm thấy nhân viên." });

            return Ok(nhanVien);
        }

        // GET: api/NhanVien/theo-vai-tro/4
        // Dùng cho combobox "chọn nhân viên kỹ thuật" khi Tổ trưởng tạo phân công công việc
        [HttpGet("theo-vai-tro/{maVaiTro}")]
        public async Task<IActionResult> GetByVaiTro(int maVaiTro)
        {
            var data = await _context.NhanViens
                .AsNoTracking()
                .Where(n => n.MaNguoiDungNavigation.MaVaiTro == maVaiTro
                         && n.MaNguoiDungNavigation.TrangThai == "Đang hoạt động")
                .Select(n => new
                {
                    n.MaNhanVien,
                    n.HoTen,
                    n.ChucVu
                })
                .OrderBy(n => n.HoTen)
                .ToListAsync();

            return Ok(data);
        }

        // PUT: api/NhanVien/5
        // Chỉ Admin được sửa hồ sơ nhân viên (họ tên, email, sđt, chức vụ, trạng thái làm việc)
        [Authorize(Roles = "Admin hệ thống")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] NhanVienUpdateDto dto)
        {
            if (id != dto.MaNhanVien)
                return BadRequest(new { Message = "Id không khớp." });

            var existing = await _context.NhanViens.FindAsync(id);
            if (existing == null)
                return NotFound(new { Message = "Không tìm thấy nhân viên." });

            existing.HoTen       = dto.HoTen;
            existing.Email       = dto.Email;
            existing.SoDienThoai = dto.SoDienThoai;
            existing.ChucVu      = dto.ChucVu;
            existing.NgayVaoLam  = dto.NgayVaoLam;
            existing.TrangThai   = dto.TrangThai;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }
    }
}