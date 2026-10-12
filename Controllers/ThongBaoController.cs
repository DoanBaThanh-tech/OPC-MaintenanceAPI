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
            var ketQua = await _service.DanhDauDaDocAsync(ma, id);
            return ketQua.ok
                ? Ok(new { Message = "Đã đánh dấu đã đọc." })
                : BadRequest(new { Message = ketQua.loi });
        }

        [HttpPut("da-doc-tat-ca")]
        public async Task<IActionResult> DanhDauTatCa()
        {
            if (!TryMaNguoiDung(out var ma)) return Unauthorized();
            var ketQua = await _service.DanhDauTatCaDaDocAsync(ma);
            return ketQua.ok
                ? Ok(new { Message = "Đã đánh dấu tất cả." })
                : BadRequest(new { Message = ketQua.loi });
        }

        /// <summary>Đánh dấu đã xử lý mọi TB gắn hồ sơ BT/SC (sau khi NVKT bấm Tiến hành).</summary>
        [HttpPut("da-doc-theo-ho-so")]
        public async Task<IActionResult> DanhDauTheoHoSo(
            [FromQuery] int? maHoSoBaoTri,
            [FromQuery] int? maHoSoSuaChua)
        {
            if (!TryMaNguoiDung(out var ma)) return Unauthorized();
            var ketQua = await _service.DanhDauTheoHoSoAsync(ma, maHoSoBaoTri, maHoSoSuaChua);
            return ketQua.ok
                ? Ok(new { Message = "Đã đánh dấu." })
                : BadRequest(new { Message = ketQua.loi });
        }
    }
}
