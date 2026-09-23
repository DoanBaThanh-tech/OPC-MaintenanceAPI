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

        /// Lịch sử phân công công việc
        [HttpGet("phan-cong")]
        public async Task<IActionResult> GetLichSuPhanCong() =>
            Ok(await _service.GetLichSuPhanCongAsync());

        /// Lịch sử phê duyệt (Bảo trì / Sửa chữa) — Giám đốc
        [HttpGet("lich-su-phe-duyet")]
        public async Task<IActionResult> GetLichSuPheDuyet([FromQuery] string? loai = null, [FromQuery] int? nam = null) =>
            Ok(await _service.GetLichSuPheDuyetAsync(loai, nam));

        [HttpGet("lich-su-phe-duyet/nam")]
        public async Task<IActionResult> GetNamLichSuPheDuyet([FromQuery] string? loai = null) =>
            Ok(await _service.GetCacNamCoLichSuPheDuyetAsync(loai));

        /// Tổ trưởng hủy phân công (Chờ xác nhận / Từ chối) → hồ sơ phân công lại được
        [HttpDelete("phan-cong/{maPhanCong}")]
        public async Task<IActionResult> HuyPhanCong(int maPhanCong)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.HuyPhanCongAsync(maPhanCong, maNguoiDung));
        }


        // ===== Yêu cầu bảo trì: Tổ trưởng cơ điện tạo → Xưởng (TTSX) xác nhận/từ chối =====
        [HttpPost("yeu-cau-bao-tri")]
        public async Task<IActionResult> TaoYeuCauBaoTri(TaoYeuCauBaoTriDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.TaoYeuCauBaoTriAsync(maNguoiDung, dto));
        }

        [HttpGet("yeu-cau-bao-tri")]
        public async Task<IActionResult> GetYeuCauBaoTri([FromQuery] string? trangThai = null, [FromQuery] int? nam = null, [FromQuery] int? thang = null) =>
            Ok(await _service.GetYeuCauBaoTriAsync(trangThai, nam, thang));

        [HttpGet("yeu-cau-bao-tri/de-tao-ho-so")]
        public async Task<IActionResult> GetYeuCauDeTaoHoSo([FromQuery] int? nam = null, [FromQuery] int? thang = null) =>
            Ok(await _service.GetYeuCauDaXacNhanDeTaoHoSoAsync(nam, thang));

        [HttpPut("yeu-cau-bao-tri/{id}/xac-nhan")]
        public async Task<IActionResult> XacNhanYeuCauBaoTri(int id, XacNhanYeuCauBaoTriDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.XacNhanYeuCauBaoTriAsync(id, maNguoiDung, dto));
        }

        /// <summary>Tổ trưởng cơ điện sửa yêu cầu bị từ chối rồi gửi lại xưởng.</summary>
        [HttpPut("yeu-cau-bao-tri/{id}")]
        public async Task<IActionResult> SuaYeuCauBaoTri(int id, SuaYeuCauBaoTriDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.SuaYeuCauBaoTriAsync(id, maNguoiDung, dto));
        }

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

        /// <summary>Xưởng lưu chỉnh sửa hồ sơ (Chờ duyệt).</summary>
        [Authorize(Roles = "Xưởng,Tổ trưởng sản xuất")]
        [HttpPut("bao-tri/{id}/xuong-luu")]
        public async Task<IActionResult> XuongLuuHoSo(int id, CapNhatHoSoBaoTriDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.XuongLuuHoSoAsync(id, maNguoiDung, dto));
        }

        /// <summary>Xưởng xác nhận lịch — hồ sơ vẫn Chờ duyệt để Giám đốc duyệt.</summary>
        [Authorize(Roles = "Xưởng,Tổ trưởng sản xuất")]
        [HttpPut("bao-tri/{id}/xuong-gui-giam-doc")]
        public async Task<IActionResult> XuongGuiGiamDoc(int id, XuongGuiGiamDocDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.XuongGuiGiamDocAsync(id, maNguoiDung, dto));
        }

        /// <summary>NVKT bấm Hoàn thành bảo trì — đồng bộ trạng thái tất cả NV cùng hồ sơ.</summary>
        [Authorize(Roles = "Nhân viên kỹ thuật")]
        [HttpPut("bao-tri/{id}/nhan-vien-hoan-thanh")]
        public async Task<IActionResult> NhanVienHoanThanhBaoTri(int id)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.NhanVienHoanThanhBaoTriAsync(id, maNguoiDung));
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

        /// Nhân viên kỹ thuật xác nhận nhận việc → hồ sơ Đang thực hiện, phân công Xác nhận
        [HttpPut("bao-tri/{id}/nhan-viec")]
        public async Task<IActionResult> NhanVienXacNhanBaoTri(int id)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.NhanVienXacNhanBaoTriAsync(id, maNguoiDung));
        }

        /// NVKT từ chối nhận việc bảo trì (kèm lý do) — hồ sơ vẫn Đã duyệt
        [HttpPut("bao-tri/{id}/tu-choi-nhan-viec")]
        public async Task<IActionResult> NhanVienTuChoiBaoTri(int id, TuChoiNhanViecDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.NhanVienTuChoiBaoTriAsync(id, maNguoiDung, dto));
        }

        /// Xưởng chỉnh sửa hồ sơ bị Giám đốc từ chối rồi gửi lại duyệt (Chờ GĐ duyệt).
        /// Tổ trưởng cơ điện không được chỉnh sửa hồ sơ từ chối — chỉ phân công khi đã duyệt.
        [HttpPut("bao-tri/{id}/sua-tu-choi")]
        [Authorize(Roles = "Xưởng,Tổ trưởng sản xuất")]
        public async Task<IActionResult> CapNhatHoSoBiTuChoi(int id, CapNhatHoSoBaoTriDto dto)
        {
            return Result(await _service.CapNhatHoSoBaoTriBiTuChoiAsync(id, dto));
        }

        // Sửa chữa
        /// <summary>Danh sách hồ sơ SC (trangThai null = tất cả).</summary>
        [HttpGet("sua-chua")]
        public async Task<IActionResult> GetSuaChua([FromQuery] string? trangThai = null) =>
            Ok(await _service.GetHoSoSuaChuaTheoTrangThaiAsync(trangThai));

        [HttpGet("sua-chua/{id}")]
        public async Task<IActionResult> GetChiTietSuaChua(int id)
        {
            var r = await _service.GetChiTietHoSoSuaChuaAsync(id);
            return r == null ? NotFound(new { message = "Không tìm thấy hồ sơ." }) : Ok(r);
        }

        /// <summary>Xưởng tạo hồ sơ sửa chữa → gửi Tổ trưởng phân công; thiết bị → Sửa chữa.</summary>
        [HttpPost("sua-chua")]
        [Authorize(Roles = "Xưởng,Tổ trưởng sản xuất")]
        public async Task<IActionResult> TaoSuaChua(TaoHoSoSuaChuaDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.TaoHoSoSuaChuaAsync(maNguoiDung, dto));
        }

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
        [Authorize(Roles = "Tổ trưởng cơ điện,Tổ trưởng kỹ thuật,Tổ trưởng")]
        public async Task<IActionResult> PhanCongSuaChua(int id, PhanCongDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.PhanCongSuaChuaAsync(id, maNguoiDung, dto));
        }

        [HttpPut("sua-chua/{id}/hoan-thanh")]
        [Authorize(Roles = "Nhân viên kỹ thuật")]
        public async Task<IActionResult> HoanThanhSuaChua(int id)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.NhanVienHoanThanhSuaChuaAsync(id, maNguoiDung));
        }

        [HttpPut("sua-chua/{id}/xac-nhan")]
        public async Task<IActionResult> XacNhanSuaChua(int id, XacNhanDto dto) => Result(await _service.XacNhanHoanThanhSuaChuaAsync(id, dto));

        [HttpPut("sua-chua/{id}/nhan-viec")]
        public async Task<IActionResult> NhanVienXacNhanSuaChua(int id)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.NhanVienXacNhanSuaChuaAsync(id, maNguoiDung));
        }

        [HttpPut("sua-chua/{id}/tu-choi-nhan-viec")]
        public async Task<IActionResult> NhanVienTuChoiSuaChua(int id, TuChoiNhanViecDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Result(await _service.NhanVienTuChoiSuaChuaAsync(id, maNguoiDung, dto));
        }

        /// Yêu cầu được phân công cho NVKT đang đăng nhập
        [HttpGet("yeu-cau-cua-toi")]
        public async Task<IActionResult> GetYeuCauCuaToi([FromQuery] string? loai = null, [FromQuery] string? trangThai = null)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Ok(await _service.GetYeuCauCuaNhanVienAsync(maNguoiDung, loai, trangThai));
        }

        /// Yêu cầu đã xác nhận — dùng cho combobox Kết quả thực hiện
        [HttpGet("yeu-cau-da-xac-nhan")]
        public async Task<IActionResult> GetYeuCauDaXacNhan([FromQuery] string? loai = null)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
                return Unauthorized();
            return Ok(await _service.GetYeuCauDaXacNhanAsync(maNguoiDung, loai));
        }

        // Dùng chung
        [HttpPost("phan-cong/{maPhanCong}/ket-qua")]
        public async Task<IActionResult> GhiNhanKetQua(int maPhanCong, GhiNhanKetQuaDto dto)
        {
            var claim = User.FindFirst("MaNguoiDung")?.Value;
            if (claim != null && int.TryParse(claim, out var maNguoiDung) && dto.MaNhanVienGhiNhan <= 0)
            {
                // Service sẽ fallback MaNhanVienThucHien nếu cần; giữ nguyên dto
            }
            return Result(await _service.GhiNhanKetQuaAsync(maPhanCong, dto));
        }

        private IActionResult Result((bool ok, string? loi) r) => r.ok ? Ok(new { thongBao = r.loi }) : BadRequest(new { loi = r.loi });
    }
}