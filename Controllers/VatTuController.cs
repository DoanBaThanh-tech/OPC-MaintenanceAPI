using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.VatTu;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class VatTuController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public VatTuController(OPCDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.VatTus
                .AsNoTracking()
                .OrderBy(v => v.MaVatTu)
                .Select(v => new
                {
                    v.MaVatTu, v.TenVatTu, v.DonViTinh, v.SoLuongTonKho, v.MucTonKhoToiThieu,
                    CanhBao = v.MucTonKhoToiThieu != null && v.SoLuongTonKho < v.MucTonKhoToiThieu
                })
                .ToListAsync();
            return Ok(data);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.VatTus.AsNoTracking()
                .FirstOrDefaultAsync(v => v.MaVatTu == id);
            if (item == null) return NotFound(new { Message = "Không tìm thấy vật tư." });
            return Ok(item);
        }

        [Authorize(Roles = "Quản lý vật tư,Admin hệ thống")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] VatTuCreateDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var vatTu = new VatTu
            {
                TenVatTu = dto.TenVatTu,
                DonViTinh = dto.DonViTinh,
                SoLuongTonKho = dto.SoLuongTonKho,
                MucTonKhoToiThieu = dto.MucTonKhoToiThieu,
                GhiChu = dto.GhiChu
            };
            _context.VatTus.Add(vatTu);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = vatTu.MaVatTu }, vatTu);
        }

        [Authorize(Roles = "Quản lý vật tư,Admin hệ thống")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] VatTuUpdateDto dto)
        {
            if (id != dto.MaVatTu) return BadRequest(new { Message = "Id không khớp." });

            var existing = await _context.VatTus.FindAsync(id);
            if (existing == null) return NotFound(new { Message = "Không tìm thấy vật tư." });

            existing.TenVatTu = dto.TenVatTu;
            existing.DonViTinh = dto.DonViTinh;
            existing.MucTonKhoToiThieu = dto.MucTonKhoToiThieu;
            existing.GhiChu = dto.GhiChu;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }
    }
}