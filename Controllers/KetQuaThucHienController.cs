using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.KetQuaThucHien;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class KetQuaThucHienController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public KetQuaThucHienController(OPCDbContext context) => _context = context;

        private async Task<NhanVien?> LayNhanVienHienTai()
        {
            var maNguoiDungClaim = User.FindFirstValue("MaNguoiDung");
            if (maNguoiDungClaim == null) return null;
            return await _context.NhanViens
                .FirstOrDefaultAsync(n => n.MaNguoiDung == int.Parse(maNguoiDungClaim));
        }

        // GET: api/KetQuaThucHien/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.KetQuaThucHiens
                .AsNoTracking()
                .Where(k => k.MaKetQua == id)
                .Select(k => new
                {
                    k.MaKetQua,
                    k.MaPhanCong,
                    NguoiGhiNhan = k.MaNhanVienGhiNhanNavigation.HoTen,
                    k.SoLieuGhiNhan,
                    k.HinhAnh,
                    k.GhiChu,
                    k.NgayGhiNhan,
                    k.XacNhanHoanThanh
                })
                .FirstOrDefaultAsync();

            if (item == null)
                return NotFound(new { Message = "Không tìm thấy kết quả thực hiện." });

            return Ok(item);
        }

        // POST: api/KetQuaThucHien  (Nhân viên kỹ thuật ghi nhận kết quả, đóng hồ sơ)
        [Authorize(Roles = "Nhân viên kỹ thuật")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] KetQuaThucHienCreateDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var nhanVien = await LayNhanVienHienTai();
            if (nhanVien == null)
                return Unauthorized(new { Message = "Không xác định được nhân viên đang đăng nhập." });

            var phanCong = await _context.PhanCongCongViecs.FindAsync(dto.MaPhanCong);
            if (phanCong == null)
                return BadRequest(new { Message = "Không tìm thấy phân công." });

            if (phanCong.MaNhanVienThucHien != nhanVien.MaNhanVien)
                return Forbid();

            if (await _context.KetQuaThucHiens.AnyAsync(k => k.MaPhanCong == dto.MaPhanCong))
                return BadRequest(new { Message = "Phân công này đã có kết quả, không ghi nhận trùng." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var ketQua = new KetQuaThucHien
                {
                    MaPhanCong        = dto.MaPhanCong,
                    MaNhanVienGhiNhan = nhanVien.MaNhanVien,
                    SoLieuGhiNhan     = dto.SoLieuGhiNhan,
                    HinhAnh           = dto.HinhAnh,
                    GhiChu            = dto.GhiChu,
                    XacNhanHoanThanh  = dto.XacNhanHoanThanh
                };
                _context.KetQuaThucHiens.Add(ketQua);
                await _context.SaveChangesAsync();

                if (dto.XacNhanHoanThanh)
                {
                    phanCong.TrangThai = "Đã hoàn thành";
                    await _context.SaveChangesAsync();

                    // Tìm hồ sơ bảo trì hoặc sửa chữa gắn với phân công này
                    var hoSoBaoTri = await _context.HoSoBaoTris
                        .Include(h => h.MaThieBiNavigation)
                        .FirstOrDefaultAsync(h => h.MaPhanCong == dto.MaPhanCong);

                    var hoSoSuaChua = await _context.HoSoSuaChuas
                        .FirstOrDefaultAsync(h => h.MaPhanCong == dto.MaPhanCong);

                    if (hoSoBaoTri != null)
                    {
                        hoSoBaoTri.TrangThai = "Đã hoàn thành";
                        await _context.SaveChangesAsync();

                        // Cập nhật lại chu kỳ đề xuất cho thiết bị (theo đúng nghiệp vụ đã thống nhất)
                        var thietBi = hoSoBaoTri.MaThieBiNavigation;
                        thietBi.NgayBaoTriGanNhat = DateOnly.FromDateTime(DateTime.Now);
                        if (dto.SoThangDeXuatTiepTheo.HasValue)
                            thietBi.SoThangDeXuat = dto.SoThangDeXuatTiepTheo.Value;

                        thietBi.NgayBaoTriTiepTheo = thietBi.SoThangDeXuat.HasValue
                            ? thietBi.NgayBaoTriGanNhat.Value.AddMonths(thietBi.SoThangDeXuat.Value)
                            : null;
                        await _context.SaveChangesAsync();

                        // Ghi lịch sử thiết bị
                        _context.LichSuThietBis.Add(new LichSuThietBi
                        {
                            MaThietBi     = thietBi.MaThietBi,
                            MaHoSoBaoTri  = hoSoBaoTri.MaHoSoBaoTri,
                            NgayHoanThanh = DateTime.Now,
                            KetQua        = dto.SoLieuGhiNhan,
                            GhiChu        = dto.GhiChu
                        });
                        await _context.SaveChangesAsync();

                        // cập nhật dòng kế hoạch tương ứng (nếu có)
                        var chiTiet = await _context.ChiTietKeHoachBaoTris
                            .FirstOrDefaultAsync(c => c.MaHoSoBaoTri == hoSoBaoTri.MaHoSoBaoTri);
                        if (chiTiet != null)
                        {
                            chiTiet.TrangThai = "Đã hoàn thành";
                            await _context.SaveChangesAsync();
                        }
                    }
                    else if (hoSoSuaChua != null)
                    {
                        hoSoSuaChua.TrangThai = "Đã hoàn thành";
                        await _context.SaveChangesAsync();

                        _context.LichSuThietBis.Add(new LichSuThietBi
                        {
                            MaThietBi      = hoSoSuaChua.MaThieBi,
                            MaHoSoSuaChua  = hoSoSuaChua.MaHoSoSuaChua,
                            NgayHoanThanh  = DateTime.Now,
                            KetQua         = dto.SoLieuGhiNhan,
                            GhiChu         = dto.GhiChu
                        });
                        await _context.SaveChangesAsync();
                    }
                }

                await transaction.CommitAsync();

                return CreatedAtAction(nameof(GetById), new { id = ketQua.MaKetQua }, new
                {
                    ketQua.MaKetQua,
                    ketQua.MaPhanCong,
                    ketQua.XacNhanHoanThanh
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { Message = "Ghi nhận kết quả thất bại." });
            }
        }
    }
}