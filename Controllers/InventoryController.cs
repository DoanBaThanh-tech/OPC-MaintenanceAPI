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

        /// <summary>Danh sách vật tư kho.</summary>
        [HttpGet("vat-tu")]
        public async Task<IActionResult> DanhSachVatTu() => Ok(await _service.GetDanhSachVatTuAsync());

        /// <summary>NVKT tạo hồ sơ vật tư khi hoàn thành quy trình bảo trì/sửa chữa.</summary>
        [HttpPost("ho-so-vat-tu")]
        public async Task<IActionResult> TaoHoSoVatTu(TaoHoSoVatTuDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            var (ok, loi, data) = await _service.TaoHoSoSuDungVatTuAsync(dto, maNguoiDung);
            return ok ? Ok(data) : BadRequest(new { loi });
        }

        /// <summary>Tổ trưởng cơ điện / Giám đốc xem danh sách hồ sơ vật tư.</summary>
        [HttpGet("ho-so-vat-tu")]
        public async Task<IActionResult> DanhSachHoSoVatTu([FromQuery] string? trangThai)
            => Ok(await _service.GetDanhSachHoSoVatTuAsync(trangThai));

        [HttpGet("ho-so-vat-tu/{id}")]
        public async Task<IActionResult> ChiTietHoSoVatTu(int id)
        {
            var data = await _service.GetHoSoVatTuByIdAsync(id);
            return data == null ? NotFound(new { loi = "Không tìm thấy hồ sơ vật tư." }) : Ok(data);
        }

        /// <summary>Tổ trưởng gửi hồ sơ vật tư cho Giám đốc.</summary>
        [HttpPut("ho-so-vat-tu/{id}/gui-giam-doc")]
        public async Task<IActionResult> GuiGiamDoc(int id)
        {
            var (ok, loi) = await _service.GuiHoSoVatTuChoGiamDocAsync(id);
            return ok ? Ok(new { message = "Đã gửi hồ sơ vật tư cho Giám đốc." }) : BadRequest(new { loi });
        }


        /// <summary>Quy trình bảo trì/sửa chữa theo từng thiết bị (tối đa 4 bước).</summary>
        [HttpGet("quy-trinh")]
        public async Task<IActionResult> QuyTrinhThietBi([FromQuery] int maThietBi, [FromQuery] string loaiCongViec)
        {
            if (maThietBi <= 0) return BadRequest(new { loi = "maThietBi không hợp lệ." });
            if (string.IsNullOrWhiteSpace(loaiCongViec)) return BadRequest(new { loi = "loaiCongViec bắt buộc." });
            var data = await _service.GetQuyTrinhThietBiAsync(maThietBi, loaiCongViec);
            return Ok(data);
        }

        private IActionResult Result((bool ok, string? loi) r) => r.ok ? Ok(new { canhBao = r.loi }) : BadRequest(new { loi = r.loi });
    }
}
