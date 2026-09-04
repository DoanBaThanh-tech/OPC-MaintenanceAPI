using Microsoft.AspNetCore.Mvc;
using OPC.MaintenanceAPI.DTOs.Inventory;
using OPC.MaintenanceAPI.Services.Interfaces;
using OPC.MaintenanceAPI.DTOs.Common;
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

        [HttpPut("yeu-cau/{id}/duyet")]
        public async Task<IActionResult> DuyetYeuCau(int id, DuyetHoSoDto dto) => Result(await _service.DuyetYeuCauVatTuAsync(id, dto));

        [HttpPost("nhap-kho")]
        public async Task<IActionResult> NhapKho(NhapKhoDto dto) => Result(await _service.NhapKhoAsync(dto));

        [HttpPost("yeu-cau/{id}/xuat-kho")]
        public async Task<IActionResult> XuatKho(int id) => Result(await _service.XuatKhoAsync(id));

        private IActionResult Result((bool ok, string? loi) r) => r.ok ? Ok(new { canhBao = r.loi }) : BadRequest(new { loi = r.loi });
    }
}