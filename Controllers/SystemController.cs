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
    public class SystemController : ControllerBase
    {
        private readonly ISystemService _service;
        public SystemController(ISystemService service) => _service = service;

        // ---------- NHÂN VIÊN (dùng cho phân công — Tổ trưởng / Giám đốc / Admin đều cần) ----------
        [Authorize]
        [HttpGet("nhan-vien")]
        public async Task<IActionResult> GetDanhSachNhanVien([FromQuery] string? vaiTro = null)
        {
            var list = await _service.GetDanhSachNhanVienAsync(vaiTro);
            return Ok(list);
        }

        // ---------- VAI TRÒ (chỉ Admin) ----------
        [Authorize(Roles = "Admin hệ thống")]
        [HttpGet("vaitro")]
        public async Task<IActionResult> GetAllVaiTro() => Ok(await _service.GetAllVaiTroAsync());

        [Authorize(Roles = "Admin hệ thống")]
        [HttpPost("vaitro")]
        public async Task<IActionResult> TaoVaiTro([FromBody] VaiTroDto dto) =>
            Ok(await _service.TaoVaiTroAsync(dto));

        [Authorize(Roles = "Admin hệ thống")]
        [HttpPut("vaitro/{maVaiTro}")]
        public async Task<IActionResult> CapNhatVaiTro(int maVaiTro, [FromBody] VaiTroDto dto) =>
            Ok(await _service.CapNhatVaiTroAsync(maVaiTro, dto));

        [Authorize(Roles = "Admin hệ thống")]
        [HttpDelete("vaitro/{maVaiTro}")]
        public async Task<IActionResult> XoaVaiTro(int maVaiTro)
        {
            await _service.XoaVaiTroAsync(maVaiTro);
            return Ok(new { Message = "Đã xoá vai trò." });
        }

        // ---------- PHÂN QUYỀN (chỉ Admin) ----------
        [Authorize(Roles = "Admin hệ thống")]
        [HttpGet("phanquyen/{maVaiTro}")]
        public async Task<IActionResult> GetMaTranPhanQuyen(int maVaiTro) =>
            Ok(await _service.GetMaTranPhanQuyenAsync(maVaiTro));

        [Authorize(Roles = "Admin hệ thống")]
        [HttpPut("phanquyen/{maVaiTro}")]
        public async Task<IActionResult> LuuPhanQuyen(int maVaiTro, [FromBody] CapNhatPhanQuyenDto dto)
        {
            await _service.LuuPhanQuyenAsync(maVaiTro, dto);
            return Ok(new { Message = "Đã lưu phân quyền." });
        }

        // ---------- NHẬT KÝ HỆ THỐNG (chỉ Admin) ----------
        [Authorize(Roles = "Admin hệ thống")]
        [HttpGet("nhatky")]
        public async Task<IActionResult> TimNhatKy([FromQuery] NhatKyFilterDto filter) =>
            Ok(await _service.TimNhatKyAsync(filter));
    }
}