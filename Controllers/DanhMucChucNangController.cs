using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.DanhMucChucNang;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DanhMucChucNangController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public DanhMucChucNangController(OPCDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.DanhMucChucNangs.AsNoTracking()
                .OrderBy(c => c.MaChucNang).ToListAsync();
            return Ok(data);
        }

        [Authorize(Roles = "Admin hệ thống")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DanhMucChucNangCreateDto dto)
        {
            var item = new DanhMucChucNang
            {
                TenChucNang = dto.TenChucNang,
                NhomChucNang = dto.NhomChucNang,
                MoTa = dto.MoTa
            };
            _context.DanhMucChucNangs.Add(item);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetAll), null, item);
        }
    }
}