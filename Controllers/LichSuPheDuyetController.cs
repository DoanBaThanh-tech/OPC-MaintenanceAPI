using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class LichSuPheDuyetController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public LichSuPheDuyetController(OPCDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetAll(int? maHoSoBaoTri, int? maHoSoSuaChua, int? maYeuCauVatTu)
        {
            var query = _context.LichSuPheDuyets.AsNoTracking().AsQueryable();
            if (maHoSoBaoTri.HasValue) query = query.Where(l => l.MaHoSoBaoTri == maHoSoBaoTri);
            if (maHoSoSuaChua.HasValue) query = query.Where(l => l.MaHoSoSuaChua == maHoSoSuaChua);
            if (maYeuCauVatTu.HasValue) query = query.Where(l => l.MaYeuCauVatTu == maYeuCauVatTu);

            var data = await query
                .OrderByDescending(l => l.NgayDuyet)
                .Select(l => new
                {
                    l.MaPheDuyet,
                    l.MaHoSoBaoTri,
                    l.MaHoSoSuaChua,
                    l.MaYeuCauVatTu,
                    NguoiDuyet = l.MaNhanVienDuyetNavigation.HoTen,
                    l.QuyetDinh,
                    l.LyDo,
                    l.NgayDuyet
                })
                .ToListAsync();

            return Ok(data);
        }
    }
}