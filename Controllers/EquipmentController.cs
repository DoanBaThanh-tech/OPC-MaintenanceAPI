using Microsoft.AspNetCore.Mvc;
using OPC.MaintenanceAPI.DTOs.Equipment;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EquipmentController : ControllerBase
    {
        private readonly IEquipmentService _service;
        public EquipmentController(IEquipmentService service) => _service = service;

        /// <summary>
        /// GET /api/Equipment
        /// Query: maChuKy, trangThai (Sản xuất | Bảo trì | Sửa chữa)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] int? maChuKy = null,
            [FromQuery] string? trangThai = null) =>
            Ok(await _service.GetAllAsync(maChuKy, trangThai));

        /// <summary>
        /// GET /api/Equipment/theo-danh-muc
        /// Trả về danh sách đã nhóm theo danh mục (Loại thiết bị / Chu kỳ)
        /// </summary>
        [HttpGet("theo-danh-muc")]
        public async Task<IActionResult> GetTheoDanhMuc([FromQuery] string? trangThai = null) =>
            Ok(await _service.GetTheoDanhMucAsync(trangThai));

        /// <summary>GET /api/Equipment/thong-ke</summary>
        [HttpGet("thong-ke")]
        public async Task<IActionResult> GetThongKe() =>
            Ok(await _service.GetThongKeAsync());

        /// <summary>
        /// POST /api/Equipment/dong-bo-trang-thai
        /// Đồng bộ TinhTrangHienTai theo hồ sơ BT/SC (Chờ duyệt / Đã duyệt / Đang thực hiện).
        /// </summary>
        [HttpPost("dong-bo-trang-thai")]
        public async Task<IActionResult> DongBoTrangThai()
        {
            var soDoi = await _service.DongBoTrangThaiTuHoSoAsync();
            return Ok(new
            {
                Message = $"Đã đồng bộ trạng thái thiết bị. Số thiết bị cập nhật: {soDoi}.",
                soThietBiCapNhat = soDoi
            });
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var r = await _service.GetByIdAsync(id);
            return r == null ? NotFound(new { Message = "Không tìm thấy thiết bị." }) : Ok(r);
        }


        [HttpPost]
        public async Task<IActionResult> Create(TaoThietBiDto dto)
        {
            var (ok, loi) = await _service.TaoMoiAsync(dto);
            return ok ? Ok(new { Message = "Tạo thiết bị thành công." }) : BadRequest(new { Message = loi });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, CapNhatThietBiDto dto)
        {
            var (ok, loi) = await _service.CapNhatAsync(id, dto);
            return ok
                ? Ok(new { Message = "Cập nhật thành công.", canhBao = loi })
                : BadRequest(new { Message = loi });
        }

        [HttpGet("{id:int}/lich-su")]
        public async Task<IActionResult> GetLichSu(int id) =>
            Ok(await _service.GetLichSuAsync(id));
    }
}