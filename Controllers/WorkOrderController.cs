using Microsoft.AspNetCore.Mvc;
using OPC.MaintenanceAPI.DTOs.WorkOrder;
using OPC.MaintenanceAPI.Services.Interfaces;
using OPC.MaintenanceAPI.DTOs.Common;
namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkOrderController : ControllerBase
    {
        private readonly IWorkOrderService _service;
        public WorkOrderController(IWorkOrderService service) => _service = service;

        // Bảo trì
        [HttpPost("bao-tri")]
        public async Task<IActionResult> TaoBaoTri(TaoHoSoBaoTriDto dto) => Result(await _service.TaoHoSoBaoTriAsync(dto));

        [HttpPut("bao-tri/{id}/duyet")]
        public async Task<IActionResult> DuyetBaoTri(int id, DuyetHoSoDto dto) => Result(await _service.DuyetHoSoBaoTriAsync(id, dto));

        [HttpPost("bao-tri/{id}/phan-cong")]
        public async Task<IActionResult> PhanCongBaoTri(int id, PhanCongDto dto) => Result(await _service.PhanCongBaoTriAsync(id, dto));

        [HttpPut("bao-tri/{id}/xac-nhan")]
        public async Task<IActionResult> XacNhanBaoTri(int id, XacNhanDto dto) => Result(await _service.XacNhanHoanThanhBaoTriAsync(id, dto));

        // Sửa chữa
        [HttpPost("sua-chua")]
        public async Task<IActionResult> TaoSuaChua(TaoHoSoSuaChuaDto dto) => Result(await _service.TaoHoSoSuaChuaAsync(dto));

        [HttpPut("sua-chua/{id}/duyet")]
        public async Task<IActionResult> DuyetSuaChua(int id, DuyetHoSoDto dto) => Result(await _service.DuyetHoSoSuaChuaAsync(id, dto));

        [HttpPost("sua-chua/{id}/phan-cong")]
        public async Task<IActionResult> PhanCongSuaChua(int id, PhanCongDto dto) => Result(await _service.PhanCongSuaChuaAsync(id, dto));

        [HttpPut("sua-chua/{id}/xac-nhan")]
        public async Task<IActionResult> XacNhanSuaChua(int id, XacNhanDto dto) => Result(await _service.XacNhanHoanThanhSuaChuaAsync(id, dto));

        // Dùng chung
        [HttpPost("phan-cong/{maPhanCong}/ket-qua")]
        public async Task<IActionResult> GhiNhanKetQua(int maPhanCong, GhiNhanKetQuaDto dto) => Result(await _service.GhiNhanKetQuaAsync(maPhanCong, dto));

        private IActionResult Result((bool ok, string? loi) r) => r.ok ? Ok(new { thongBao = r.loi }) : BadRequest(new { loi = r.loi });
    }
}