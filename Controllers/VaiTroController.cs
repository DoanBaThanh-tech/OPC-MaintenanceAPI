using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.VaiTro;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VaiTroController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public VaiTroController(OPCDbContext context) => _context = context;

        // GET: api/VaiTro
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.VaiTros
                .AsNoTracking()
                .OrderBy(v => v.MaVaiTro)
                .Select(v => new
                {
                    v.MaVaiTro,
                    v.TenVaiTro,
                    v.CapDoQuyen,
                    v.TrangThai
                })
                .ToListAsync();

            return Ok(data);
        }

        // GET: api/VaiTro/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var vaiTro = await _context.VaiTros
                .AsNoTracking()
                .Where(v => v.MaVaiTro == id)
                .Select(v => new
                {
                    v.MaVaiTro,
                    v.TenVaiTro,
                    v.CapDoQuyen,
                    v.MoTa,
                    v.TrangThai,
                    v.NgayTao
                })
                .FirstOrDefaultAsync();

            if (vaiTro == null)
                return NotFound(new { Message = "Không tìm thấy vai trò." });

            return Ok(vaiTro);
        }

        // POST: api/VaiTro
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] VaiTroCreateDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // chặn tạo trùng tên vai trò
            var trungTen = await _context.VaiTros.AnyAsync(v => v.TenVaiTro == dto.TenVaiTro);
            if (trungTen)
                return BadRequest(new { Message = "Tên vai trò đã tồn tại." });

            var vaiTro = new VaiTro
            {
                TenVaiTro  = dto.TenVaiTro,
                CapDoQuyen = dto.CapDoQuyen,
                MoTa       = dto.MoTa
            };

            _context.VaiTros.Add(vaiTro);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = vaiTro.MaVaiTro }, vaiTro);
        }

        // PUT: api/VaiTro/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] VaiTroUpdateDto dto)
        {
            if (id != dto.MaVaiTro)
                return BadRequest(new { Message = "Id không khớp." });

            var existing = await _context.VaiTros.FindAsync(id);
            if (existing == null)
                return NotFound(new { Message = "Không tìm thấy vai trò." });

            existing.TenVaiTro  = dto.TenVaiTro;
            existing.CapDoQuyen = dto.CapDoQuyen;
            existing.MoTa       = dto.MoTa;
            existing.TrangThai  = dto.TrangThai;   // dùng để "ngừng sử dụng" thay vì xóa

            await _context.SaveChangesAsync();
            return Ok(existing);
        }
    }
}