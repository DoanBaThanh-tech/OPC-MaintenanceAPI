using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.ThietBi;


namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ThietBiController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public ThietBiController(OPCDbContext context) => _context = context;

        // GET: api/ThietBi?page=1&pageSize=20
        [HttpGet]
        public async Task<IActionResult> GetAll(int page = 1, int pageSize = 20)
        {
            var total = await _context.ThietBis.CountAsync();

            var data = await _context.ThietBis
                .AsNoTracking()
                .OrderBy(t => t.MaThietBi)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new
                {
                    t.MaThietBi,
                    t.TenThietBi,
                    t.LoaiThietBi,
                    t.ViTriLapDat,
                    t.TinhTrangHienTai,
                    t.NgayBaoTriTiepTheo
                })
                .ToListAsync();

            return Ok(new
            {
                Page = page,
                PageSize = pageSize,
                TotalRecords = total,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                Data = data
            });
        }

        // GET: api/ThietBi/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var thietBi = await _context.ThietBis
                .AsNoTracking()
                .Where(t => t.MaThietBi == id)
                .Select(t => new
                {
                    t.MaThietBi,
                    t.TenThietBi,
                    t.LoaiThietBi,
                    t.ViTriLapDat,
                    t.NgayLapDat,
                    t.TinhTrangHienTai,
                    t.GhiChu,
                    t.NgayBaoTriGanNhat,
                    t.NgayBaoTriTiepTheo,
                    t.SoThangDeXuat,
                    t.MaChuKy
                })
                .FirstOrDefaultAsync();

            if (thietBi == null)
                return NotFound(new { Message = "Không tìm thấy thiết bị." });

            return Ok(thietBi);
        }

        
        // POST: api/ThietBi
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ThietBiCreateDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var thietBi = new ThietBi
            {
                MaChuKy = dto.MaChuKy,
                TenThietBi = dto.TenThietBi,
                LoaiThietBi = dto.LoaiThietBi,
                ViTriLapDat = dto.ViTriLapDat,
                NgayLapDat = dto.NgayLapDat,
                TinhTrangHienTai = dto.TinhTrangHienTai,
                GhiChu = dto.GhiChu
            };

            // kiểm tra MaChuKy có tồn tại không, tránh lỗi FK khó hiểu từ SQL
            var chuKyTonTai = await _context.ChuKyBaoTris.AnyAsync(c => c.MaChuKy == thietBi.MaChuKy);
            if (!chuKyTonTai)
                return BadRequest(new { Message = "MaChuKy không tồn tại." });

            _context.ThietBis.Add(thietBi);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = thietBi.MaThietBi }, thietBi);
        }

       // PUT: api/ThietBi/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] ThietBiUpdateDto dto)
        {
            if (id != dto.MaThietBi)
                return BadRequest(new { Message = "Id không khớp." });

            var existing = await _context.ThietBis.FindAsync(id);
            if (existing == null)
                return NotFound(new { Message = "Không tìm thấy thiết bị." });

            var chuKyTonTai = await _context.ChuKyBaoTris.AnyAsync(c => c.MaChuKy == dto.MaChuKy);
            if (!chuKyTonTai)
                return BadRequest(new { Message = "MaChuKy không tồn tại." });

            existing.TenThietBi       = dto.TenThietBi;
            existing.LoaiThietBi      = dto.LoaiThietBi;
            existing.ViTriLapDat      = dto.ViTriLapDat;
            existing.TinhTrangHienTai = dto.TinhTrangHienTai;
            existing.GhiChu           = dto.GhiChu;
            existing.MaChuKy          = dto.MaChuKy;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }
    }
}