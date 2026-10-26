using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Controllers
{
    /// <summary>
    /// OPC Bot — Rule-based / Intent-driven, khóa theo vai trò JWT.
    /// Không dùng Ollama local.
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
            public string? HoTen { get; set; }
            public List<ChatTurnDto>? LichSu { get; set; }
        }

        public class ChatTurnDto
        {
            public string? Role { get; set; }
            public string? Content { get; set; }
        }

        private string LayVaiTroTuJwt()
        {
            return User.FindFirst(ClaimTypes.Role)?.Value
                   ?? User.FindFirst("VaiTro")?.Value
                   ?? "Người dùng";
        }

        /// <summary>Intent engine luôn online khi API chạy.</summary>
        [HttpGet("trang-thai")]
        public async Task<IActionResult> TrangThai(CancellationToken ct)
        {
            var (online, model, loi) = await _ai.KiemTraAsync(ct);
            return Ok(new
            {
                online,
                model,
                message = online
                    ? $"OPC Bot sẵn sàng ({model}). Gemini+DB context khi có ApiKey; fallback rule nếu chưa cấu hình."
                    : loi
            });
        }

        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatRequest req, CancellationToken ct)
        {
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
                engine = "gemini+db-context",
                tokenCloud = true
            });
        }
    }
}
