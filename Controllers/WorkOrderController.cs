using Microsoft.AspNetCore.Mvc;
using OPC.MaintenanceAPI.DTOs.WorkOrder;
using OPC.MaintenanceAPI.Services.Interfaces;
using OPC.MaintenanceAPI.DTOs.Common;
using Microsoft.AspNetCore.Authorization;
namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // mọi endpoint cần đăng nhập; các action cụ thể có thể siết thêm Roles
    public class WorkOrderController : ControllerBase
    {
        private readonly IWorkOrderService _service;
        private readonly ISystemService _systemService;

        public WorkOrderController(IWorkOrderService service, ISystemService systemService)
        {
            _service = service;
            _systemService = systemService;
        }

        /// Danh sách nhân viên (lọc theo vai trò) — dùng cho màn Phân công.
        /// Đặt ở WorkOrder để Tổ trưởng gọi được (không bị chặn bởi quyền Admin của SystemController).
        [HttpGet("nhan-vien")]
        public async Task<IActionResult> GetDanhSachNhanVien([FromQuery] string? vaiTro = null) =>
            Ok(await _systemService.GetDanhSachNhanVienAsync(vaiTro));

        [HttpGet("bao-tri")]
        public async Task<IActionResult> GetBaoTriTheoTrangThai([FromQuery] string? trangThai = null, [FromQuery] int? nam = null) =>
            Ok(await _service.GetHoSoBaoTriTheoTrangThaiAsync(trangThai, nam));

        [HttpGet("bao-tri/{id}")]
        public async Task<IActionResult> GetBaoTriById(int id)
        {
            var r = await _service.GetHoSoBaoTriByIdAsync(id);
            return r == null ? NotFound() : Ok(r);
        }
        [HttpGet("bao-tri/nam-co-du-lieu")]
        public async Task<IActionResult> GetCacNamCoHoSoBaoTri([FromQuery] string? trangThai = null) =>
            Ok(await _service.GetCacNamCoHoSoBaoTriAsync(trangThai));
        // Bảo trì
        [HttpPost("bao-tri")]
        public async Task<IActionResult> TaoBaoTri(TaoHoSoBaoTriDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();

            return Result(await _service.TaoHoSoBaoTriAsync(maNguoiDung, dto));
        }

        [Authorize(Roles = "Giám đốc,Phó giám đốc")]
        [HttpPut("bao-tri/{id}/duyet")]
        public async Task<IActionResult> DuyetBaoTri(int id, DuyetHoSoDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();

            return Result(await _service.DuyetHoSoBaoTriAsync(id, maNguoiDung, dto));
        }

        [HttpPost("bao-tri/{id}/phan-cong")]
        public async Task<IActionResult> PhanCongBaoTri(int id, PhanCongDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();

            return Result(await _service.PhanCongBaoTriAsync(id, maNguoiDung, dto));
        }

        [HttpPut("bao-tri/{id}/xac-nhan")]
        public async Task<IActionResult> XacNhanBaoTri(int id, XacNhanDto dto) => Result(await _service.XacNhanHoanThanhBaoTriAsync(id, dto));

        // Sửa chữa
        [HttpPost("sua-chua")]
        public async Task<IActionResult> TaoSuaChua(TaoHoSoSuaChuaDto dto) => Result(await _service.TaoHoSoSuaChuaAsync(dto));

        [HttpPut("sua-chua/{id}/duyet")]
        [Authorize(Roles = "Giám đốc,Phó giám đốc")]
        public async Task<IActionResult> DuyetSuaChua(int id, DuyetHoSoDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();

            return Result(await _service.DuyetHoSoSuaChuaAsync(id, maNguoiDung, dto));
        }

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