using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OPC.MaintenanceAPI.DTOs.MaintenancePlan;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MaintenancePlanController : ControllerBase
    {
        private readonly IMaintenancePlanService _service;
        public MaintenancePlanController(IMaintenancePlanService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetAllKeHoach() => Ok(await _service.GetAllKeHoachAsync());

        [HttpGet("{maKeHoach}/chi-tiet")]
        public async Task<IActionResult> GetChiTiet(int maKeHoach) =>
            Ok(await _service.GetChiTietTheoKeHoachAsync(maKeHoach));

        [HttpGet("cho-tao-ho-so")]
        public async Task<IActionResult> GetChoTaoHoSo() => Ok(await _service.GetChiTietChuaCoHoSoAsync());

        [HttpGet("chu-ky")]
        public async Task<IActionResult> GetAllChuKy() => Ok(await _service.GetAllChuKyAsync());

        // Thay thế 2 endpoint cũ (POST yeu-cau-ngay + POST LapKeHoach).
        // Khớp đúng với body Flutter đang gửi: { maChuKy, nam, thietBiDuocChon: [...] }
        [Authorize(Roles = "Tổ trưởng kỹ thuật")]
        [HttpPost]
        public async Task<IActionResult> TaoKeHoach(TaoKeHoachDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();

            var (ok, loi) = await _service.TaoKeHoachAsync(maNguoiDung, dto);
            return ok ? Ok(new { message = "Đã lập kế hoạch bảo trì thành công." }) : BadRequest(new { loi });
        }

        [Authorize(Roles = "Tổ trưởng kỹ thuật")]
        [HttpPost("{maKeHoach}/them-lan-bao-tri")]
        public async Task<IActionResult> ThemLanBaoTri(int maKeHoach, ThemLanBaoTriDto dto)
        {
            var (ok, loi) = await _service.ThemLanBaoTriAsync(maKeHoach, dto);
            return ok ? Ok(new { message = "Đã thêm lần bảo trì mới." }) : BadRequest(new { loi });
        }
        [HttpPost("nam-moi")]
        public async Task<IActionResult> TaoNamMoi(TaoNamMoiDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung)) return Unauthorized();

            var (ok, loi) = await _service.TaoNamMoiAsync(maNguoiDung, dto);
            return ok
                ? Ok(new { Message = "Tạo kế hoạch năm mới thành công." })
                : BadRequest(new { Message = loi, loi });
        }


        [HttpPost("them-thiet-bi")]
        public async Task<IActionResult> ThemThietBi(ThemThietBiVaoNamDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();

            var (ok, loi) = await _service.ThemThietBiVaoNamAsync(maNguoiDung, dto);
            // Luôn trả JSON để client mobile parse ổn định (tránh Ok() body rỗng)
            return ok
                ? Ok(new { Message = "Đã tạo kế hoạch bảo trì và hồ sơ bảo trì (Chờ duyệt)." })
                : BadRequest(new { Message = loi, loi });
        }

        [HttpGet("nam-da-lap")]
        public async Task<IActionResult> GetNamDaLap() =>
            Ok((await _service.GetAllKeHoachAsync()).Select(k => k.Nam).Distinct().OrderByDescending(n => n));
    }
}