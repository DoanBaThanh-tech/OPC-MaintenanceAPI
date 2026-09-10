using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using OPC.MaintenanceAPI.DTOs.Inventory;
using OPC.MaintenanceAPI.Services.Interfaces;
using OPC.MaintenanceAPI.DTOs.Common;
using Microsoft.AspNetCore.Authorization;
namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InventoryController : ControllerBase
    {
        private readonly IInventoryService _service;
        public InventoryController(IInventoryService service) => _service = service;

        [HttpPost("kiem-tra-ton-kho")]
        public async Task<IActionResult> KiemTra(List<KiemTraVatTuDto> danhSach)
        {
            var (ok, loi, du) = await _service.KiemTraTonKhoAsync(danhSach);
            return ok ? Ok(new { duVatTu = du }) : BadRequest(new { loi });
        }

        [HttpPost("yeu-cau")]
        public async Task<IActionResult> TaoYeuCau(TaoYeuCauVatTuDto dto) => Result(await _service.TaoYeuCauVatTuAsync(dto));

        [Authorize(Roles = "Giám đốc,Phó giám đốc")]
        [HttpPut("yeu-cau-vat-tu/{id}/duyet")]
        public async Task<IActionResult> DuyetYeuCauVatTu(int id, DuyetHoSoDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();

            var (ok, loi) = await _service.DuyetYeuCauVatTuAsync(id, maNguoiDung, dto);
            return ok ? Ok(new { message = loi ?? "Đã xử lý." }) : BadRequest(new { loi });
        }

        [HttpPost("nhap-kho")]
        public async Task<IActionResult> NhapKho(NhapKhoDto dto) => Result(await _service.NhapKhoAsync(dto));

        [HttpPost("yeu-cau/{id}/xuat-kho")]
        public async Task<IActionResult> XuatKho(int id)
        {
            var maNhanVien = User.FindFirst("MaNguoiDung")?.Value;
            if (!int.TryParse(maNhanVien, out var maNhanVienGiaoDich))
                return BadRequest(new { loi = "Không thể xác định nhân viên thực hiện." });
            return Result(await _service.XuatKhoAsync(id, maNhanVienGiaoDich));
        }

        private IActionResult Result((bool ok, string? loi) r) => r.ok ? Ok(new { canhBao = r.loi }) : BadRequest(new { loi = r.loi });
    }
}