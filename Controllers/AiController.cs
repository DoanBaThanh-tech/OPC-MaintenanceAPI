using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OPC.MaintenanceAPI.Services.Implementations;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Controllers
{
    /// <summary>
    /// OPC Bot — số liệu tra trực tiếp DB (theo công thức app), Groq cho câu hỏi giải thích; khóa theo vai trò JWT.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AiController : ControllerBase
    {
        private const int MaxCauHoi = 1000;
        private const int MaxLichSu = 8;
        private const int MaxNoiDungLichSu = 800;
        private const int MaxReqPerMinute = 15;

        // Giới hạn tần suất theo người dùng
        private static readonly ConcurrentDictionary<int, (DateTime start, int count)> _rate = new();

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

        private static bool VuotGioiHan(int maNd)
        {
            var now = DateTime.UtcNow;
            var entry = _rate.AddOrUpdate(
                maNd,
                _ => (now, 1),
                (_, old) => now - old.start > TimeSpan.FromMinutes(1) ? (now, 1) : (old.start, old.count + 1));
            return entry.count > MaxReqPerMinute;
        }

        [HttpGet("trang-thai")]
        public async Task<IActionResult> TrangThai(CancellationToken ct)
        {
            var (online, model, loi) = await _ai.KiemTraAsync(ct);
            return Ok(new
            {
                online,
                model,
                message = online
                    ? $"OPC Bot sẵn sàng ({model}). Số liệu tra trực tiếp DB; Groq dùng cho câu hỏi giải thích."
                      + (string.IsNullOrEmpty(loi) ? "" : " " + loi)
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

            var cauHoi = (req.CauHoi ?? "").Trim();
            if (cauHoi.Length == 0)
                return BadRequest(new { Message = "Gửi giúp một câu hỏi nhé." });
            if (cauHoi.Length > MaxCauHoi)
                return BadRequest(new { Message = $"Câu hỏi quá dài (tối đa {MaxCauHoi} ký tự)." });

            if (VuotGioiHan(maNguoiDung))
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { Message = "Bạn hỏi hơi nhanh, thử lại sau ít giây nhé." });

            var hoTen = string.IsNullOrWhiteSpace(req.HoTen)
                ? (User.FindFirst(ClaimTypes.Email)?.Value ?? "bạn")
                : req.HoTen!.Trim();

            // Giữ nguyên thứ tự hội thoại; cắt độ dài để tránh prompt quá lớn
            var lichSu = req.LichSu?
                .Where(x => !string.IsNullOrWhiteSpace(x.Content))
                .TakeLast(MaxLichSu)
                .Select(x =>
                {
                    var c = x.Content!.Trim();
                    if (c.Length > MaxNoiDungLichSu) c = c[..MaxNoiDungLichSu];
                    var role = string.Equals(x.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                        ? "assistant" : "user";
                    return (role, c);
                })
                .ToList();

            var (ok, traLoi, loi) = await _ai.ChatAsync(
                maNguoiDung,
                vaiTro,
                hoTen,
                cauHoi,
                lichSu,
                ct);

            if (!ok)
                return BadRequest(new { Message = loi ?? "Không chat được." });

            return Ok(new
            {
                traLoi,
                vaiTro,
                hoTen,
                engine = "db+groq"
            });
        }

        /// <summary>
        /// Xem các bài học sửa sai chatbot đã ghi nhận (để rà soát/huấn luyện).
        /// Chỉ Giám đốc / Admin. daGiaiQuyet=false là các lần báo sai chưa tìm ra cách hiểu đúng.
        /// </summary>
        [HttpGet("bai-hoc")]
        public IActionResult BaiHoc([FromQuery] bool? daGiaiQuyet = null)
        {
            var vaiTro = LayVaiTroTuJwt();
            var duocXem = vaiTro.Contains("Giám đốc", StringComparison.OrdinalIgnoreCase)
                          || vaiTro.Contains("Admin", StringComparison.OrdinalIgnoreCase);
            if (!duocXem)
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { Message = "Chỉ Giám đốc hoặc Admin xem được danh sách bài học." });

            var ds = ChatbotBaiHocStore.TatCa()
                .Where(b => daGiaiQuyet == null || b.DaGiaiQuyet == daGiaiQuyet)
                .OrderByDescending(b => b.Luc)
                .Take(200)
                .ToList();
            return Ok(new { tong = ds.Count, baiHoc = ds });
        }
    }
}