using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.ChuKyBaoTri;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ChuKyBaoTriController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public ChuKyBaoTriController(OPCDbContext context) => _context = context;

        // GET: api/ChuKyBaoTri
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.ChuKyBaoTris
                .AsNoTracking()
                .OrderBy(c => c.MaChuKy)
                .ToListAsync();
            return Ok(data);
        }

        // GET: api/ChuKyBaoTri/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.ChuKyBaoTris
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.MaChuKy == id);

            if (item == null)
                return NotFound(new { Message = "Không tìm thấy chu kỳ." });

            return Ok(item);
        }

        // POST: api/ChuKyBaoTri
        [Authorize(Roles = "Admin hệ thống,Tổ trưởng kỹ thuật")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ChuKyBaoTriCreateDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var chuKy = new ChuKyBaoTri
            {
                LoaiThietBi        = dto.LoaiThietBi,
                SoThangChuKyDeXuat = dto.SoThangChuKyDeXuat,
                MoTa                = dto.MoTa
            };

            _context.ChuKyBaoTris.Add(chuKy);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = chuKy.MaChuKy }, chuKy);
        }
    }
}