using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Controllers
{
    /// <summary>
    /// Chatbot AI local (Ollama) — khóa theo vai trò JWT của user đăng nhập.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AiController : ControllerBase
    {
        private readonly IAiChatService _ai;

        public AiController(IAiChatService ai) => _ai = ai;

        public class ChatRequest
        {
            public string? CauHoi { get; set; }
            /// <summary>Họ tên hiển thị (JWT không chứa HoTen) — chỉ dùng để chào.</summary>
            public string? HoTen { get; set; }
            /// <summary>[{ "role": "user"|"assistant", "content": "..." }]</summary>
            public List<ChatTurnDto>? LichSu { get; set; }
        }

        public class ChatTurnDto
        {
            public string? Role { get; set; }
            public string? Content { get; set; }
        }

        /// <summary>Vai trò luôn lấy từ JWT — không tin client.</summary>
        private string LayVaiTroTuJwt()
        {
            return User.FindFirst(ClaimTypes.Role)?.Value
                   ?? User.FindFirst("VaiTro")?.Value
                   ?? "Người dùng";
        }

        /// <summary>Kiểm tra Ollama local có chạy không.</summary>
        [HttpGet("trang-thai")]
        public async Task<IActionResult> TrangThai(CancellationToken ct)
        {
            var (online, model, loi) = await _ai.KiemTraAsync(ct);
            return Ok(new
            {
                online,
                model,
                message = online
                    ? $"AI local sẵn sàng (model: {model}). Không tốn token cloud."
                    : loi
            });
        }

        /// <summary>
        /// Chat — tự lấy vai trò + họ tên từ JWT.
        /// AI chỉ trả lời trong phạm vi vai trò đó.
        /// </summary>
        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatRequest req, CancellationToken ct)
        {
            // Vai trò + user khóa từ JWT — không tin client
            var vaiTro = LayVaiTroTuJwt();
            var maNdClaim = User.FindFirst("MaNguoiDung")?.Value;
            if (string.IsNullOrEmpty(maNdClaim) || !int.TryParse(maNdClaim, out var maNguoiDung))
                return Unauthorized(new { Message = "Phiên đăng nhập không hợp lệ." });

            var hoTen = string.IsNullOrWhiteSpace(req.HoTen)
                ? (User.FindFirst(ClaimTypes.Email)?.Value ?? "bạn")
                : req.HoTen!.Trim();
            var lichSu = req.LichSu?
                .Where(x => !string.IsNullOrWhiteSpace(x.Content))
                .Select(x => (x.Role ?? "user", x.Content!.Trim()))
                .ToList();

            var (ok, traLoi, loi) = await _ai.ChatAsync(
                maNguoiDung,
                vaiTro,
                hoTen,
                req.CauHoi ?? "",
                lichSu,
                ct);

            if (!ok)
                return BadRequest(new { Message = loi ?? "Không chat được." });

            return Ok(new
            {
                traLoi,
                vaiTro,
                hoTen,
                local = true,
                tokenCloud = false
            });
        }
    }
}
