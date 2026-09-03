using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.NhapXuatVatTu;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NhapXuatVatTuController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public NhapXuatVatTuController(OPCDbContext context) => _context = context;

        private async Task<NhanVien?> LayNhanVienHienTai()
        {
            var maNguoiDungClaim = User.FindFirstValue("MaNguoiDung");
            if (maNguoiDungClaim == null) return null;
            return await _context.NhanViens
                .FirstOrDefaultAsync(n => n.MaNguoiDung == int.Parse(maNguoiDungClaim));
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.NhapXuatVatTus
                .AsNoTracking()
                .OrderByDescending(n => n.MaGiaoDich)
                .Select(n => new
                {
                    n.MaGiaoDich,
                    TenVatTu = n.MaVatTuNavigation.TenVatTu,
                    NguoiThucHien = n.MaNhanVienGiaoDichNavigation.HoTen,
                    n.LoaiGiaoDich,
                    n.SoLuong,
                    n.NgayGiaoDich
                })
                .ToListAsync();
            return Ok(data);
        }

        [Authorize(Roles = "Quản lý vật tư")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] NhapXuatVatTuCreateDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (dto.LoaiGiaoDich != "Nhập" && dto.LoaiGiaoDich != "Xuất")
                return BadRequest(new { Message = "LoaiGiaoDich chỉ nhận 'Nhập' hoặc 'Xuất'." });

            if (dto.LoaiGiaoDich == "Xuất" && dto.MaYeuCauVatTu == null)
                return BadRequest(new { Message = "Xuất kho bắt buộc phải có MaYeuCauVatTu." });

            var nhanVien = await LayNhanVienHienTai();
            if (nhanVien == null) return Unauthorized(new { Message = "Không xác định được nhân viên." });

            var vatTu = await _context.VatTus.FindAsync(dto.MaVatTu);
            if (vatTu == null) return BadRequest(new { Message = "MaVatTu không tồn tại." });

            if (dto.LoaiGiaoDich == "Xuất" && vatTu.SoLuongTonKho < dto.SoLuong)
                return BadRequest(new { Message = "Số lượng tồn kho không đủ để xuất." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var giaoDich = new NhapXuatVatTu
                {
                    MaVatTu = dto.MaVatTu,
                    MaNhanVienGiaoDich = nhanVien.MaNhanVien,
                    MaYeuCauVatTu = dto.MaYeuCauVatTu,
                    LoaiGiaoDich = dto.LoaiGiaoDich,
                    SoLuong = dto.SoLuong,
                    GhiChu = dto.GhiChu
                };
                _context.NhapXuatVatTus.Add(giaoDich);

                vatTu.SoLuongTonKho += dto.LoaiGiaoDich == "Nhập" ? dto.SoLuong : -dto.SoLuong;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return CreatedAtAction(nameof(GetAll), null, new
                {
                    giaoDich.MaGiaoDich, giaoDich.LoaiGiaoDich, giaoDich.SoLuong,
                    TonKhoMoi = vatTu.SoLuongTonKho
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { Message = "Giao dịch thất bại." });
            }
        }
    }
}