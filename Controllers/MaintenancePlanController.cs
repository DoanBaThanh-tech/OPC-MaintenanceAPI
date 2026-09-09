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

        private int? MaNguoiDungHienTai()
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            return int.TryParse(claim, out var id) ? id : null;
        }

        // ===== BƯỚC 1: Tổ trưởng đăng ký ngày muốn bảo trì cho 1 thiết bị =====
        [HttpPost("yeu-cau-ngay")]
        public async Task<IActionResult> TaoYeuCauNgayBaoTri(TaoYeuCauNgayBaoTriDto dto)
        {
            var maNguoiDung = MaNguoiDungHienTai();
            if (maNguoiDung == null) return Unauthorized();

            var (ok, loi) = await _service.TaoYeuCauNgayBaoTriAsync(maNguoiDung.Value, dto);
            return ok ? Ok(new { message = "Đã đăng ký ngày bảo trì." }) : BadRequest(new { loi });
        }

        // Danh sách yêu cầu đang "Chờ lập kế hoạch" - để Tổ trưởng chọn ở BƯỚC 2
        [HttpGet("yeu-cau-ngay/cho-lap-ke-hoach")]
        public async Task<IActionResult> GetYeuCauChoLapKeHoach() =>
            Ok(await _service.GetYeuCauChoLapKeHoachAsync());

        // ===== BƯỚC 2: Lập kế hoạch từ 1 yêu cầu đã đăng ký =====
        [HttpPost]
        public async Task<IActionResult> LapKeHoach(LapKeHoachDto dto)
        {
            var maNguoiDung = MaNguoiDungHienTai();
            if (maNguoiDung == null) return Unauthorized();

            var (ok, loi) = await _service.LapKeHoachTuYeuCauAsync(maNguoiDung.Value, dto);
            return ok ? Ok(new { message = "Đã lập kế hoạch bảo trì." }) : BadRequest(new { loi });
        }

        [HttpGet]
        public async Task<IActionResult> GetAllKeHoach() => Ok(await _service.GetAllKeHoachAsync());

        [HttpGet("{maKeHoach}/chi-tiet")]
        public async Task<IActionResult> GetChiTiet(int maKeHoach) =>
            Ok(await _service.GetChiTietTheoKeHoachAsync(maKeHoach));

        [HttpGet("cho-tao-ho-so")]
        public async Task<IActionResult> GetChoTaoHoSo() => Ok(await _service.GetChiTietChuaCoHoSoAsync());

        [HttpGet("chu-ky")]
        public async Task<IActionResult> GetAllChuKy() => Ok(await _service.GetAllChuKyAsync());
    }
}