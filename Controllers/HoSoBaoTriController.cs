using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.HoSoBaoTri;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class HoSoBaoTriController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public HoSoBaoTriController(OPCDbContext context) => _context = context;

        // Lấy MaNhanVien của người đang đăng nhập, dựa vào MaNguoiDung trong token
        private async Task<NhanVien?> LayNhanVienHienTai()
        {
            var maNguoiDungClaim = User.FindFirstValue("MaNguoiDung");
            if (maNguoiDungClaim == null) return null;

            return await _context.NhanViens
                .FirstOrDefaultAsync(n => n.MaNguoiDung == int.Parse(maNguoiDungClaim));
        }

        // GET: api/HoSoBaoTri?trangThai=Chờ duyệt&page=1&pageSize=20
        [HttpGet]
        public async Task<IActionResult> GetAll(string? trangThai, int page = 1, int pageSize = 20)
        {
            var query = _context.HoSoBaoTris.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(trangThai))
                query = query.Where(h => h.TrangThai == trangThai);

            var total = await query.CountAsync();

            var data = await query
                .OrderByDescending(h => h.NgayTao)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(h => new
                {
                    h.MaHoSoBaoTri,
                    TenThietBi = h.MaThieBiNavigation.TenThietBi,
                    NguoiTao = h.MaNhanVienTaoNavigation.HoTen,
                    NguoiDuyet = h.MaNhanVienDuyetNavigation != null ? h.MaNhanVienDuyetNavigation.HoTen : null,
                    h.TrangThai,
                    h.NgayTao,
                    h.NgayDuyet,
                    DaPhanCong = h.MaPhanCong != null
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

        // GET: api/HoSoBaoTri/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var hoSo = await _context.HoSoBaoTris
                .AsNoTracking()
                .Where(h => h.MaHoSoBaoTri == id)
                .Select(h => new
                {
                    h.MaHoSoBaoTri,
                    h.MaThieBi,
                    TenThietBi = h.MaThieBiNavigation.TenThietBi,
                    NguoiTao = h.MaNhanVienTaoNavigation.HoTen,
                    NguoiDuyet = h.MaNhanVienDuyetNavigation != null ? h.MaNhanVienDuyetNavigation.HoTen : null,
                    h.NoiDungCongViec,
                    h.ThoiGianDuKien,
                    h.TrangThai,
                    h.LyDoTuChoi,
                    h.NgayTao,
                    h.NgayDuyet,
                    h.MaPhanCong
                })
                .FirstOrDefaultAsync();

            if (hoSo == null)
                return NotFound(new { Message = "Không tìm thấy hồ sơ bảo trì." });

            return Ok(hoSo);
        }

        // POST: api/HoSoBaoTri  (Tổ trưởng tạo hồ sơ - bắt buộc xuất phát từ 1 dòng kế hoạch)
        [Authorize(Roles = "Tổ trưởng kỹ thuật")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] HoSoBaoTriCreateDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var nhanVien = await LayNhanVienHienTai();
            if (nhanVien == null)
                return Unauthorized(new { Message = "Không xác định được nhân viên đang đăng nhập." });

            var chiTiet = await _context.ChiTietKeHoachBaoTris
                .FirstOrDefaultAsync(c => c.MaChiTietKeHoach == dto.MaChiTietKeHoach);

            if (chiTiet == null)
                return BadRequest(new { Message = "Không tìm thấy dòng kế hoạch tương ứng." });

            if (chiTiet.MaHoSoBaoTri != null)
                return BadRequest(new { Message = "Dòng kế hoạch này đã có hồ sơ bảo trì, không tạo trùng được." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var hoSo = new HoSoBaoTri
                {
                    MaThieBi         = chiTiet.MaThietBi,
                    MaNhanVienTao    = nhanVien.MaNhanVien,
                    NoiDungCongViec  = dto.NoiDungCongViec,
                    ThoiGianDuKien   = dto.ThoiGianDuKien,
                    TrangThai        = "Chờ duyệt"
                };
                _context.HoSoBaoTris.Add(hoSo);
                await _context.SaveChangesAsync();

                // liên kết ngược lại dòng kế hoạch với hồ sơ vừa tạo
                chiTiet.MaHoSoBaoTri = hoSo.MaHoSoBaoTri;
                chiTiet.TrangThai = "Đã tạo hồ sơ";
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return CreatedAtAction(nameof(GetById), new { id = hoSo.MaHoSoBaoTri }, new
                {
                    hoSo.MaHoSoBaoTri,
                    hoSo.MaThieBi,
                    hoSo.MaNhanVienTao,
                    hoSo.NoiDungCongViec,
                    hoSo.ThoiGianDuKien,
                    hoSo.TrangThai,
                    hoSo.NgayTao
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { Message = "Tạo hồ sơ thất bại." });
            }
        }

        // PUT: api/HoSoBaoTri/5/duyet  (Giám đốc/PGĐ duyệt hoặc từ chối)
        [Authorize(Roles = "Giám đốc/Phó giám đốc")]
        [HttpPut("{id}/duyet")]
        public async Task<IActionResult> DuyetHoSo(int id, [FromBody] HoSoBaoTriApproveDto dto)
        {
            if (dto.QuyetDinh != "Duyệt" && dto.QuyetDinh != "Từ chối")
                return BadRequest(new { Message = "QuyetDinh chỉ nhận giá trị 'Duyệt' hoặc 'Từ chối'." });

            var nhanVien = await LayNhanVienHienTai();
            if (nhanVien == null)
                return Unauthorized(new { Message = "Không xác định được nhân viên đang đăng nhập." });

            var hoSo = await _context.HoSoBaoTris.FindAsync(id);
            if (hoSo == null)
                return NotFound(new { Message = "Không tìm thấy hồ sơ bảo trì." });

            if (hoSo.TrangThai != "Chờ duyệt")
                return BadRequest(new { Message = "Hồ sơ này đã được xử lý trước đó, không thể duyệt lại." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                hoSo.MaNhanVienDuyet = nhanVien.MaNhanVien;
                hoSo.NgayDuyet       = DateTime.Now;
                hoSo.TrangThai       = dto.QuyetDinh == "Duyệt" ? "Đã duyệt" : "Từ chối";
                hoSo.LyDoTuChoi      = dto.QuyetDinh == "Từ chối" ? dto.LyDo : null;
                await _context.SaveChangesAsync();

                // ghi lại lịch sử phê duyệt
                var lichSu = new LichSuPheDuyet
                {
                    MaHoSoBaoTri    = hoSo.MaHoSoBaoTri,
                    MaNhanVienDuyet = nhanVien.MaNhanVien,
                    QuyetDinh       = dto.QuyetDinh,
                    LyDo            = dto.LyDo
                };
                _context.LichSuPheDuyets.Add(lichSu);
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