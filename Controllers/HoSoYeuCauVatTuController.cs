using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.HoSoYeuCauVatTu;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class HoSoYeuCauVatTuController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public HoSoYeuCauVatTuController(OPCDbContext context) => _context = context;

        private async Task<NhanVien?> LayNhanVienHienTai()
        {
            var maNguoiDungClaim = User.FindFirstValue("MaNguoiDung");
            if (maNguoiDungClaim == null) return null;
            return await _context.NhanViens
                .FirstOrDefaultAsync(n => n.MaNguoiDung == int.Parse(maNguoiDungClaim));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var hoSo = await _context.HoSoYeuCauVatTus
                .AsNoTracking()
                .Where(h => h.MaYeuCauVatTu == id)
                .Select(h => new
                {
                    h.MaYeuCauVatTu,
                    h.MaHoSoSuaChua,
                    NguoiTao = h.MaNhanVienTaoNavigation.HoTen,
                    NguoiDuyet = h.MaNhanVienDuyetNavigation != null ? h.MaNhanVienDuyetNavigation.HoTen : null,
                    h.TrangThai,
                    h.NgayTao,
                    ChiTiet = h.ChiTietYeuCauVatTus.Select(c => new
                    {
                        c.MaChiTietYeuCauVatTu,
                        TenVatTu = c.MaVatTuNavigation.TenVatTu,
                        c.SoLuongYeuCau
                    })
                })
                .FirstOrDefaultAsync();

            if (hoSo == null) return NotFound(new { Message = "Không tìm thấy hồ sơ yêu cầu vật tư." });
            return Ok(hoSo);
        }

        [Authorize(Roles = "Tổ trưởng kỹ thuật")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] HoSoYeuCauVatTuCreateDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (dto.ChiTiet == null || dto.ChiTiet.Count == 0)
                return BadRequest(new { Message = "Phải có ít nhất 1 loại vật tư." });

            var nhanVien = await LayNhanVienHienTai();
            if (nhanVien == null) return Unauthorized(new { Message = "Không xác định được nhân viên." });

            var hoSoSC = await _context.HoSoSuaChuas.FindAsync(dto.MaHoSoSuaChua);
            if (hoSoSC == null) return BadRequest(new { Message = "Không tìm thấy hồ sơ sửa chữa." });

            if (await _context.HoSoYeuCauVatTus.AnyAsync(h => h.MaHoSoSuaChua == dto.MaHoSoSuaChua))
                return BadRequest(new { Message = "Hồ sơ sửa chữa này đã có yêu cầu vật tư." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var hoSo = new HoSoYeuCauVatTu
                {
                    MaHoSoSuaChua = dto.MaHoSoSuaChua,
                    MaNhanVienTao = nhanVien.MaNhanVien,
                    TrangThai = "Chờ duyệt"
                };
                _context.HoSoYeuCauVatTus.Add(hoSo);
                await _context.SaveChangesAsync();

                foreach (var ct in dto.ChiTiet)
                {
                    if (!await _context.VatTus.AnyAsync(v => v.MaVatTu == ct.MaVatTu))
                        throw new Exception($"MaVatTu {ct.MaVatTu} không tồn tại.");

                    _context.ChiTietYeuCauVatTus.Add(new ChiTietYeuCauVatTu
                    {
                        MaYeuCauVatTu = hoSo.MaYeuCauVatTu,
                        MaVatTu = ct.MaVatTu,
                        SoLuongYeuCau = ct.SoLuongYeuCau
                    });
                }
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return CreatedAtAction(nameof(GetById), new { id = hoSo.MaYeuCauVatTu }, new
                {
                    hoSo.MaYeuCauVatTu, hoSo.TrangThai
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { Message = ex.Message });
            }
        }

        [Authorize(Roles = "Giám đốc/Phó giám đốc")]
        [HttpPut("{id}/duyet")]
        public async Task<IActionResult> DuyetHoSo(int id, [FromBody] HoSoYeuCauVatTuApproveDto dto)
        {
            if (dto.QuyetDinh != "Duyệt" && dto.QuyetDinh != "Từ chối")
                return BadRequest(new { Message = "QuyetDinh chỉ nhận giá trị 'Duyệt' hoặc 'Từ chối'." });

            var nhanVien = await LayNhanVienHienTai();
            if (nhanVien == null) return Unauthorized(new { Message = "Không xác định được nhân viên." });

            var hoSo = await _context.HoSoYeuCauVatTus.FindAsync(id);
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
                    MaYeuCauVatTu = hoSo.MaYeuCauVatTu,
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