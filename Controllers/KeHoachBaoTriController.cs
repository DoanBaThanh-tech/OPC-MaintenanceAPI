using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.KeHoachBaoTri;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class KeHoachBaoTriController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public KeHoachBaoTriController(OPCDbContext context) => _context = context;

        // GET: api/KeHoachBaoTri
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.KeHoachBaoTris
                .AsNoTracking()
                .OrderByDescending(k => k.MaKeHoach)
                .Select(k => new
                {
                    k.MaKeHoach,
                    k.MaChuKy,
                    LoaiThietBi = k.MaChuKyNavigation.LoaiThietBi,
                    NguoiLap    = k.MaNhanVienLapNavigation.HoTen,
                    k.Nam,
                    k.NgayLapKeHoach,
                    k.TrangThai
                })
                .ToListAsync();

            return Ok(data);
        }

        // GET: api/KeHoachBaoTri/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.KeHoachBaoTris
                .AsNoTracking()
                .Where(k => k.MaKeHoach == id)
                .Select(k => new
                {
                    k.MaKeHoach,
                    k.MaChuKy,
                    LoaiThietBi = k.MaChuKyNavigation.LoaiThietBi,
                    NguoiLap    = k.MaNhanVienLapNavigation.HoTen,
                    k.Nam,
                    k.NgayLapKeHoach,
                    k.TrangThai,
                    k.NgayTao
                })
                .FirstOrDefaultAsync();

            if (item == null)
                return NotFound(new { Message = "Không tìm thấy kế hoạch." });

            return Ok(item);
        }

        // POST: api/KeHoachBaoTri
        [Authorize(Roles = "Tổ trưởng kỹ thuật")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] KeHoachBaoTriCreateDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var maNguoiDungClaim = User.FindFirstValue("MaNguoiDung");
            if (maNguoiDungClaim == null)
                return Unauthorized(new { Message = "Không xác định được người đăng nhập." });

            var nhanVien = await _context.NhanViens
                .FirstOrDefaultAsync(n => n.MaNguoiDung == int.Parse(maNguoiDungClaim));
            if (nhanVien == null)
                return Unauthorized(new { Message = "Không tìm thấy hồ sơ nhân viên." });

            if (!await _context.ChuKyBaoTris.AnyAsync(c => c.MaChuKy == dto.MaChuKy))
                return BadRequest(new { Message = "MaChuKy không tồn tại." });

            var keHoach = new KeHoachBaoTri
            {
                MaChuKy        = dto.MaChuKy,
                MaNhanVienLap  = nhanVien.MaNhanVien,
                Nam            = dto.Nam,
                NgayLapKeHoach = dto.NgayLapKeHoach,
                TrangThai      = "Đang lập"
            };

            _context.KeHoachBaoTris.Add(keHoach);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = keHoach.MaKeHoach }, new
            {
                keHoach.MaKeHoach,
                keHoach.MaChuKy,
                keHoach.MaNhanVienLap,
                keHoach.Nam,
                keHoach.NgayLapKeHoach,
                keHoach.TrangThai
            });
        }
    }
}