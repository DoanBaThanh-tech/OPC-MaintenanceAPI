using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.PhanQuyenVaiTro;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin hệ thống")]
    public class PhanQuyenVaiTroController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public PhanQuyenVaiTroController(OPCDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetAll(int? maVaiTro)
        {
            var query = _context.PhanQuyenVaiTros.AsNoTracking().AsQueryable();
            if (maVaiTro.HasValue) query = query.Where(p => p.MaVaiTro == maVaiTro);

            var data = await query
                .Select(p => new
                {
                    p.MaPhanQuyen,
                    TenVaiTro = p.MaVaiTroNavigation.TenVaiTro,
                    TenChucNang = p.MaChucNangNavigation.TenChucNang,
                    p.DuocXem, p.DuocTao, p.DuocSua, p.DuocDuyet
                })
                .ToListAsync();

            return Ok(data);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PhanQuyenVaiTroCreateDto dto)
        {
            if (!await _context.VaiTros.AnyAsync(v => v.MaVaiTro == dto.MaVaiTro))
                return BadRequest(new { Message = "MaVaiTro không tồn tại." });
            if (!await _context.DanhMucChucNangs.AnyAsync(c => c.MaChucNang == dto.MaChucNang))
                return BadRequest(new { Message = "MaChucNang không tồn tại." });

            if (await _context.PhanQuyenVaiTros.AnyAsync(p => p.MaVaiTro == dto.MaVaiTro && p.MaChucNang == dto.MaChucNang))
                return BadRequest(new { Message = "Đã tồn tại phân quyền cho vai trò và chức năng này." });

            var item = new PhanQuyenVaiTro
            {
                MaVaiTro = dto.MaVaiTro,
                MaChucNang = dto.MaChucNang,
                DuocXem = dto.DuocXem,
                DuocTao = dto.DuocTao,
                DuocSua = dto.DuocSua,
                DuocDuyet = dto.DuocDuyet
            };
            _context.PhanQuyenVaiTros.Add(item);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetAll), null, item);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] PhanQuyenVaiTroCreateDto dto)
        {
            var existing = await _context.PhanQuyenVaiTros.FindAsync(id);
            if (existing == null) return NotFound(new { Message = "Không tìm thấy phân quyền." });

            existing.DuocXem = dto.DuocXem;
            existing.DuocTao = dto.DuocTao;
            existing.DuocSua = dto.DuocSua;
            existing.DuocDuyet = dto.DuocDuyet;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }
    }
}