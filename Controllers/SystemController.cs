// Controllers/SystemController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OPC.MaintenanceAPI.DTOs.System;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Controllers
{
    /// Gộp VaiTro + PhanQuyenVaiTro + DanhMucChucNang + NhatKyHeThong
    /// Controller CHỈ nhận request và gọi Service - không if/else nghiệp vụ, không try/catch
    /// (lỗi được ném bằng NotFoundException/BusinessRuleException, ExceptionHandlingMiddleware xử lý)
    [ApiController]
    [Route("api/system")]
    [Authorize(Roles = "Admin hệ thống")]
    public class SystemController : ControllerBase
    {
        private readonly ISystemService _service;
        public SystemController(ISystemService service) => _service = service;

        // ---------- VAI TRÒ ----------
        [HttpGet("vaitro")]
        public async Task<IActionResult> GetAllVaiTro() => Ok(await _service.GetAllVaiTroAsync());

        [HttpPost("vaitro")]
        public async Task<IActionResult> TaoVaiTro([FromBody] VaiTroDto dto) =>
            Ok(await _service.TaoVaiTroAsync(dto));

        [HttpPut("vaitro/{maVaiTro}")]
        public async Task<IActionResult> CapNhatVaiTro(int maVaiTro, [FromBody] VaiTroDto dto) =>
            Ok(await _service.CapNhatVaiTroAsync(maVaiTro, dto));

        [HttpDelete("vaitro/{maVaiTro}")]
        public async Task<IActionResult> XoaVaiTro(int maVaiTro)
        {
            await _service.XoaVaiTroAsync(maVaiTro);
            return Ok(new { Message = "Đã xoá vai trò." });
        }

        // ---------- PHÂN QUYỀN ----------
        [HttpGet("phanquyen/{maVaiTro}")]
        public async Task<IActionResult> GetMaTranPhanQuyen(int maVaiTro) =>
            Ok(await _service.GetMaTranPhanQuyenAsync(maVaiTro));

        [HttpPut("phanquyen/{maVaiTro}")]
        public async Task<IActionResult> LuuPhanQuyen(int maVaiTro, [FromBody] CapNhatPhanQuyenDto dto)
        {
            await _service.LuuPhanQuyenAsync(maVaiTro, dto);
            return Ok(new { Message = "Đã lưu phân quyền." });
        }

        // ---------- NHẬT KÝ HỆ THỐNG ----------
        [HttpGet("nhatky")]
        public async Task<IActionResult> TimNhatKy([FromQuery] NhatKyFilterDto filter) =>
            Ok(await _service.TimNhatKyAsync(filter));
    }
}