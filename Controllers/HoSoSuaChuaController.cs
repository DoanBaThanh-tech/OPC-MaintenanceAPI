using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.HoSoSuaChua;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class HoSoSuaChuaController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public HoSoSuaChuaController(OPCDbContext context) => _context = context;

        private async Task<NhanVien?> LayNhanVienHienTai()
        {
            var maNguoiDungClaim = User.FindFirstValue("MaNguoiDung");
            if (maNguoiDungClaim == null) return null;
            return await _context.NhanViens
                .FirstOrDefaultAsync(n => n.MaNguoiDung == int.Parse(maNguoiDungClaim));
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(string? trangThai, int page = 1, int pageSize = 20)
        {
            var query = _context.HoSoSuaChuas.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(trangThai))
                query = query.Where(h => h.TrangThai == trangThai);

            var total = await query.CountAsync();
            var data = await query
                .OrderByDescending(h => h.NgayTao)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(h => new
                {
                    h.MaHoSoSuaChua,
                    TenThietBi = h.MaThieBiNavigation.TenThietBi,
                    NguoiTao = h.MaNhanVienTaoNavigation.HoTen,
                    NguoiDuyet = h.MaNhanVienDuyetNavigation != null ? h.MaNhanVienDuyetNavigation.HoTen : null,
                    h.TrangThai,
                    h.NgayTao,
                    DaPhanCong = h.MaPhanCong != null
                })
                .ToListAsync();

            return Ok(new
            {
                Page = page, PageSize = pageSize, TotalRecords = total,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize), Data = data
            });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.HoSoSuaChuas
                .AsNoTracking()
                .Where(h => h.MaHoSoSuaChua == id)
                .Select(h => new
                {
                    h.MaHoSoSuaChua,
                    h.MaThieBi,
                    TenThietBi = h.MaThieBiNavigation.TenThietBi,
                    NguoiTao = h.MaNhanVienTaoNavigation.HoTen,
                    NguoiDuyet = h.MaNhanVienDuyetNavigation != null ? h.MaNhanVienDuyetNavigation.HoTen : null,
                    h.MoTaHuHong,
                    h.PhuongAnSuaChua,
                    h.TrangThai,
                    h.LyDoTuChoi,
                    h.NgayTao,
                    h.NgayDuyet,
                    h.MaPhanCong
                })
                .FirstOrDefaultAsync();

            if (item == null) return NotFound(new { Message = "Không tìm thấy hồ sơ sửa chữa." });
            return Ok(item);
        }

        [Authorize(Roles = "Tổ trưởng kỹ thuật")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] HoSoSuaChuaCreateDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var nhanVien = await LayNhanVienHienTai();
            if (nhanVien == null) return Unauthorized(new { Message = "Không xác định được nhân viên." });

            if (!await _context.ThietBis.AnyAsync(t => t.MaThietBi == dto.MaThieBi))
                return BadRequest(new { Message = "MaThieBi không tồn tại." });

            var hoSo = new HoSoSuaChua
            {
                MaThieBi        = dto.MaThieBi,
                MaNhanVienTao   = nhanVien.MaNhanVien,
                MoTaHuHong      = dto.MoTaHuHong,
                PhuongAnSuaChua = dto.PhuongAnSuaChua,
                TrangThai       = "Chờ duyệt"
            };
            _context.HoSoSuaChuas.Add(hoSo);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = hoSo.MaHoSoSuaChua }, new
            {
                hoSo.MaHoSoSuaChua, hoSo.MaThieBi, hoSo.TrangThai
            });
        }

        [Authorize(Roles = "Giám đốc/Phó giám đốc")]
        [HttpPut("{id}/duyet")]
        public async Task<IActionResult> DuyetHoSo(int id, [FromBody] HoSoSuaChuaApproveDto dto)
        {
            if (dto.QuyetDinh != "Duyệt" && dto.QuyetDinh != "Từ chối")
                return BadRequest(new { Message = "QuyetDinh chỉ nhận giá trị 'Duyệt' hoặc 'Từ chối'." });

            var nhanVien = await LayNhanVienHienTai();
            if (nhanVien == null) return Unauthorized(new { Message = "Không xác định được nhân viên." });

            var hoSo = await _context.HoSoSuaChuas.FindAsync(id);
            if (hoSo == null) return NotFound(new { Message = "Không tìm thấy hồ sơ." });
            if (hoSo.TrangThai != "Chờ duyệt")
                return BadRequest(new { Message = "Hồ sơ đã được xử lý trước đó." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                hoSo.MaNhanVienDuyet = nhanVien.MaNhanVien;
                hoSo.NgayDuyet = DateTime.Now;
                hoSo.TrangThai = dto.QuyetDinh == "Duyệt" ? "Đã duyệt" : "Từ chối";
                hoSo.LyDoTuChoi = dto.QuyetDinh == "Từ chối" ? dto.LyDo : null;
                await _context.SaveChangesAsync();

                _context.LichSuPheDuyets.Add(new LichSuPheDuyet
                {
                    MaHoSoSuaChua = hoSo.MaHoSoSuaChua,
                    MaNhanVienDuyet = nhanVien.MaNhanVien,
                    QuyetDinh = dto.QuyetDinh,
                    LyDo = dto.LyDo
                });
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return Ok(new { Message = $"Đã {dto.QuyetDinh.ToLower()} hồ sơ." });
            }
            catch
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { Message = "Xử lý duyệt thất bại." });
            }
        }
    }
}