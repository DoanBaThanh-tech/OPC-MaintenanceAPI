using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OPC.MaintenanceAPI.DTOs.Auth;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _service;
        public AuthController(IAuthService service) => _service = service;

        [Authorize(Roles = "Admin hệ thống")]
        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [Authorize]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var r = await _service.GetByIdAsync(id);
            return r.ThanhCong ? Ok(r.Data) : NotFound(new { r.Message });
        }

        [HttpPost("dang-nhap")]
        public async Task<IActionResult> DangNhap([FromBody] DangNhapDto dto)
        {
            var r = await _service.DangNhapAsync(dto);
            return r.ThanhCong ? Ok(r.Data) : Unauthorized(new { r.Message });
        }

        [Authorize(Roles = "Admin hệ thống")]
        [HttpPost("tao-tai-khoan")]
        public async Task<IActionResult> TaoTaiKhoan([FromBody] TaoTaiKhoanDto dto)
        {
            var r = await _service.TaoTaiKhoanAsync(dto);
            return r.ThanhCong ? StatusCode(201, r.Data) : BadRequest(new { r.Message });
        }

        [Authorize(Roles = "Admin hệ thống")]
        [HttpPut("{id}/kich-hoat")]
        public async Task<IActionResult> KichHoat(int id)
        {
            var r = await _service.KichHoatAsync(id);
            return r.ThanhCong ? Ok(new { r.Message }) : NotFound(new { r.Message });
        }

        [Authorize(Roles = "Admin hệ thống")]
        [HttpPut("{id}/khoa")]
        public async Task<IActionResult> Khoa(int id)
        {
            var r = await _service.KhoaAsync(id);
            return r.ThanhCong ? Ok(new { r.Message }) : NotFound(new { r.Message });
        }

        [HttpPost("quen-mat-khau/yeu-cau")]
        public async Task<IActionResult> YeuCauOtp([FromBody] QuenMatKhauRequestDto dto)
        {
            var r = await _service.YeuCauOtpAsync(dto);
            return r.ThanhCong ? Ok(new { r.Message, r.Data }) : NotFound(new { r.Message });
        }

        [HttpPost("quen-mat-khau/xac-nhan")]
        public async Task<IActionResult> XacNhanOtp([FromBody] XacNhanOtpDto dto)
        {
            var r = await _service.XacNhanOtpAsync(dto);
            return r.ThanhCong ? Ok(new { r.Message }) : BadRequest(new { r.Message });
        }

        [HttpPost("quen-mat-khau/dat-lai")]
        public async Task<IActionResult> DatLaiMatKhau([FromBody] DatLaiMatKhauDto dto)
        {
            var r = await _service.DatLaiMatKhauAsync(dto);
            return r.ThanhCong ? Ok(new { r.Message }) : BadRequest(new { r.Message });
        }
    }
}