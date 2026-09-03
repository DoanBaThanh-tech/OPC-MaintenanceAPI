using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.ChiTietKeHoachBaoTri;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ChiTietKeHoachBaoTriController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public ChiTietKeHoachBaoTriController(OPCDbContext context) => _context = context;

        // GET: api/ChiTietKeHoachBaoTri?maKeHoach=1
        [HttpGet]
        public async Task<IActionResult> GetAll(int? maKeHoach)
        {
            var query = _context.ChiTietKeHoachBaoTris.AsNoTracking().AsQueryable();

            if (maKeHoach.HasValue)
                query = query.Where(c => c.MaKeHoach == maKeHoach);

            var data = await query
                .Select(c => new
                {
                    c.MaChiTietKeHoach,
                    c.MaKeHoach,
                    c.MaThietBi,
                    TenThietBi = c.MaThietBiNavigation.TenThietBi,
                    c.NgayDuKienBaoTri,
                    c.TrangThai,
                    c.MaHoSoBaoTri
                })
                .ToListAsync();

            return Ok(data);
        }

        // GET: api/ChiTietKeHoachBaoTri/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.ChiTietKeHoachBaoTris
                .AsNoTracking()
                .Where(c => c.MaChiTietKeHoach == id)
                .Select(c => new
                {
                    c.MaChiTietKeHoach,
                    c.MaKeHoach,
                    c.MaThietBi,
                    TenThietBi = c.MaThietBiNavigation.TenThietBi,
                    c.NgayDuKienBaoTri,
                    c.TrangThai,
                    c.MaHoSoBaoTri,
                    c.GhiChu
                })
                .FirstOrDefaultAsync();

            if (item == null)
                return NotFound(new { Message = "Không tìm thấy dòng chi tiết kế hoạch." });

            return Ok(item);
        }

        // POST: api/ChiTietKeHoachBaoTri
        [Authorize(Roles = "Tổ trưởng kỹ thuật")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ChiTietKeHoachCreateDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!await _context.KeHoachBaoTris.AnyAsync(k => k.MaKeHoach == dto.MaKeHoach))
                return BadRequest(new { Message = "MaKeHoach không tồn tại." });

            if (!await _context.ThietBis.AnyAsync(t => t.MaThietBi == dto.MaThietBi))
                return BadRequest(new { Message = "MaThietBi không tồn tại." });

            var chiTiet = new ChiTietKeHoachBaoTri
            {
                MaKeHoach        = dto.MaKeHoach,
                MaThietBi        = dto.MaThietBi,
                NgayDuKienBaoTri = dto.NgayDuKienBaoTri,
                TrangThai        = "Chưa tạo hồ sơ"
            };

            _context.ChiTietKeHoachBaoTris.Add(chiTiet);
            await _context.SaveChangesAsync();

             return CreatedAtAction(nameof(GetById), new { id = chiTiet.MaChiTietKeHoach }, new
            {
                chiTiet.MaChiTietKeHoach,
                chiTiet.MaKeHoach,
                chiTiet.MaThietBi,
                chiTiet.NgayDuKienBaoTri,
                chiTiet.TrangThai
            });
        }
    }
}