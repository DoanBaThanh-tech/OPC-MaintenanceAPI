using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin hệ thống")]
    public class NhatKyHeThongController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public NhatKyHeThongController(OPCDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetAll(int page = 1, int pageSize = 50)
        {
            var total = await _context.NhatKyHeThongs.CountAsync();
            var data = await _context.NhatKyHeThongs
                .AsNoTracking()
                .OrderByDescending(n => n.ThoiGianTruyCap)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(n => new
                {
                    n.MaNhatKy,
                    NhanVien = n.MaNhanVienNavigation.HoTen,
                    n.TenApi,
                    n.PhuongThucHttp,
                    n.ThoiGianTruyCap,
                    n.DiaChiIp
                })
                .ToListAsync();

            return Ok(new { Page = page, PageSize = pageSize, TotalRecords = total, Data = data });
        }
    }
}