using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class LichSuThietBiController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public LichSuThietBiController(OPCDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetAll(int? maThietBi)
        {
            var query = _context.LichSuThietBis.AsNoTracking().AsQueryable();
            if (maThietBi.HasValue) query = query.Where(l => l.MaThietBi == maThietBi);

            var data = await query
                .OrderByDescending(l => l.NgayHoanThanh)
                .Select(l => new
                {
                    l.MaLichSu,
                    TenThietBi = l.MaThietBiNavigation.TenThietBi,
                    LoaiHoSo = l.MaHoSoBaoTri != null ? "Bảo trì" : "Sửa chữa",
                    l.NgayHoanThanh,
                    l.KetQua,
                    l.GhiChu
                })
                .ToListAsync();

            return Ok(data);
        }
    }
}