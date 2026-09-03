using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.PhanCongCongViec;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PhanCongCongViecController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public PhanCongCongViecController(OPCDbContext context) => _context = context;

        private async Task<NhanVien?> LayNhanVienHienTai()
        {
            var maNguoiDungClaim = User.FindFirstValue("MaNguoiDung");
            if (maNguoiDungClaim == null) return null;
            return await _context.NhanViens
                .FirstOrDefaultAsync(n => n.MaNguoiDung == int.Parse(maNguoiDungClaim));
        }

        // GET: api/PhanCongCongViec
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.PhanCongCongViecs
                .AsNoTracking()
                .OrderByDescending(p => p.MaPhanCong)
                .Select(p => new
                {
                    p.MaPhanCong,
                    NhanVienThucHien = p.MaNhanVienThucHienNavigation.HoTen,
                    NguoiPhanCong    = p.MaNhanVienPhanCongNavigation.HoTen,
                    p.NgayBatDauDuKien,
                    p.NgayKetThucDuKien,
                    p.TrangThai,
                    p.NgayPhanCong,
                    LoaiHoSo   = p.HoSoBaoTri != null ? "Bảo trì" : "Sửa chữa",
                    TenThietBi = p.HoSoBaoTri != null
                        ? p.HoSoBaoTri.MaThieBiNavigation.TenThietBi
                        : p.HoSoSuaChua!.MaThieBiNavigation.TenThietBi
                })
                .ToListAsync();

            return Ok(data);
        }

        // GET: api/PhanCongCongViec/cua-toi  (Nhân viên xem việc được giao cho chính mình)
        [HttpGet("cua-toi")]
        public async Task<IActionResult> GetCuaToi()
        {
            var nhanVien = await LayNhanVienHienTai();
            if (nhanVien == null)
                return Unauthorized(new { Message = "Không xác định được nhân viên đang đăng nhập." });

            var data = await _context.PhanCongCongViecs
                .AsNoTracking()
                .Where(p => p.MaNhanVienThucHien == nhanVien.MaNhanVien)
                .OrderByDescending(p => p.MaPhanCong)
                .Select(p => new
                {
                    p.MaPhanCong,
                    p.NgayBatDauDuKien,
                    p.NgayKetThucDuKien,
                    p.TrangThai,
                    LoaiHoSo = p.HoSoBaoTri != null ? "Bảo trì" : "Sửa chữa",
                    TenThietBi = p.HoSoBaoTri != null
                        ? p.HoSoBaoTri.MaThieBiNavigation.TenThietBi
                        : p.HoSoSuaChua!.MaThieBiNavigation.TenThietBi,
                    NoiDung = p.HoSoBaoTri != null
                        ? p.HoSoBaoTri.NoiDungCongViec
                        : p.HoSoSuaChua!.MoTaHuHong
                })
                .ToListAsync();

            return Ok(data);
        }

        // GET: api/PhanCongCongViec/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.PhanCongCongViecs
                .AsNoTracking()
                .Where(p => p.MaPhanCong == id)
                .Select(p => new
                {
                    p.MaPhanCong,
                    NhanVienThucHien = p.MaNhanVienThucHienNavigation.HoTen,
                    NguoiPhanCong    = p.MaNhanVienPhanCongNavigation.HoTen,
                    p.NgayBatDauDuKien,
                    p.NgayKetThucDuKien,
                    p.TrangThai,
                    p.NgayPhanCong,
                    MaHoSoBaoTri  = p.HoSoBaoTri != null ? p.HoSoBaoTri.MaHoSoBaoTri : (int?)null,
                    MaHoSoSuaChua = p.HoSoSuaChua != null ? p.HoSoSuaChua.MaHoSoSuaChua : (int?)null
                })
                .FirstOrDefaultAsync();

            if (item == null)
                return NotFound(new { Message = "Không tìm thấy phân công." });

            return Ok(item);
        }

        // POST: api/PhanCongCongViec  (Tổ trưởng phân công nhân viên cho hồ sơ đã duyệt)
        [Authorize(Roles = "Tổ trưởng kỹ thuật")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PhanCongCongViecCreateDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // đảm bảo đúng 1 trong 2 loại hồ sơ được chỉ định
            var soLuongHoSo = (dto.MaHoSoBaoTri.HasValue ? 1 : 0) + (dto.MaHoSoSuaChua.HasValue ? 1 : 0);
            if (soLuongHoSo != 1)
                return BadRequest(new { Message = "Phải chỉ định đúng 1 trong 2: MaHoSoBaoTri hoặc MaHoSoSuaChua." });

            var toTruong = await LayNhanVienHienTai();
            if (toTruong == null)
                return Unauthorized(new { Message = "Không xác định được nhân viên đang đăng nhập." });

            if (!await _context.NhanViens.AnyAsync(n => n.MaNhanVien == dto.MaNhanVienThucHien))
                return BadRequest(new { Message = "MaNhanVienThucHien không tồn tại." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (dto.MaHoSoBaoTri.HasValue)
                {
                    var hoSo = await _context.HoSoBaoTris.FindAsync(dto.MaHoSoBaoTri.Value);
                    if (hoSo == null)
                        return BadRequest(new { Message = "Không tìm thấy hồ sơ bảo trì." });
                    if (hoSo.TrangThai != "Đã duyệt")
                        return BadRequest(new { Message = "Chỉ phân công được cho hồ sơ đã duyệt." });
                    if (hoSo.MaPhanCong != null)
                        return BadRequest(new { Message = "Hồ sơ này đã được phân công trước đó." });

                    var phanCong = new PhanCongCongViec
                    {
                        MaNhanVienThucHien = dto.MaNhanVienThucHien,
                        MaNhanVienPhanCong = toTruong.MaNhanVien,
                        NgayBatDauDuKien   = dto.NgayBatDauDuKien,
                        NgayKetThucDuKien  = dto.NgayKetThucDuKien,
                        TrangThai          = "Đang thực hiện"
                    };
                    _context.PhanCongCongViecs.Add(phanCong);
                    await _context.SaveChangesAsync();

                    hoSo.MaPhanCong = phanCong.MaPhanCong;
                    hoSo.TrangThai  = "Đang thực hiện";
                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();
                    return CreatedAtAction(nameof(GetById), new { id = phanCong.MaPhanCong }, new
                    {
                        phanCong.MaPhanCong,
                        phanCong.MaNhanVienThucHien,
                        phanCong.TrangThai
                    });
                }
                else
                {
                    var hoSo = await _context.HoSoSuaChuas.FindAsync(dto.MaHoSoSuaChua!.Value);
                    if (hoSo == null)
                        return BadRequest(new { Message = "Không tìm thấy hồ sơ sửa chữa." });
                    if (hoSo.TrangThai != "Đã duyệt")
                        return BadRequest(new { Message = "Chỉ phân công được cho hồ sơ đã duyệt." });
                    if (hoSo.MaPhanCong != null)
                        return BadRequest(new { Message = "Hồ sơ này đã được phân công trước đó." });

                    var phanCong = new PhanCongCongViec
                    {
                        MaNhanVienThucHien = dto.MaNhanVienThucHien,
                        MaNhanVienPhanCong = toTruong.MaNhanVien,
                        NgayBatDauDuKien   = dto.NgayBatDauDuKien,
                        NgayKetThucDuKien  = dto.NgayKetThucDuKien,
                        TrangThai          = "Đang thực hiện"
                    };
                    _context.PhanCongCongViecs.Add(phanCong);
                    await _context.SaveChangesAsync();

                    hoSo.MaPhanCong = phanCong.MaPhanCong;
                    hoSo.TrangThai  = "Đang thực hiện";
                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();
                    return CreatedAtAction(nameof(GetById), new { id = phanCong.MaPhanCong }, new
                    {
                        phanCong.MaPhanCong,
                        phanCong.MaNhanVienThucHien,
                        phanCong.TrangThai
                    });
                }
            }
            catch
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { Message = "Phân công thất bại." });
            }
        }
    }
}