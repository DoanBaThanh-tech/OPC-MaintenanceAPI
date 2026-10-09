using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ThongBaoController : ControllerBase
    {
        private readonly IThongBaoService _service;

        public ThongBaoController(IThongBaoService service) => _service = service;

        private bool TryMaNguoiDung(out int ma)
        {
            ma = 0;
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            return claim != null && int.TryParse(claim, out ma);
        }

        /// <summary>Danh sách thông báo của user đang đăng nhập (chỉ của chính họ).</summary>
        [HttpGet]
        public async Task<IActionResult> LayDanhSach([FromQuery] bool? chiChuaDoc)
        {
            if (!TryMaNguoiDung(out var ma)) return Unauthorized();
            return Ok(await _service.LayDanhSachAsync(ma, chiChuaDoc));
        }

        [HttpPut("{id:int}/da-doc")]
        public async Task<IActionResult> DanhDauDaDoc(int id)
        {
            if (!TryMaNguoiDung(out var ma)) return Unauthorized();
            var (ok, loi) = await _service.DanhDauDaDocAsync(ma, id);
            return ok ? Ok(new { Message = "Đã đánh dấu đã đọc." }) : BadRequest(new { Message = loi });
        }

        [HttpPut("da-doc-tat-ca")]
        public async Task<IActionResult> DanhDauTatCa()
        {
            if (!TryMaNguoiDung(out var ma)) return Unauthorized();
            var (ok, loi) = await _service.DanhDauTatCaDaDocAsync(ma);
            return ok ? Ok(new { Message = "Đã đánh dấu tất cả." }) : BadRequest(new { Message = loi });
        }
    }
}
