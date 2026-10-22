using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    /// <summary>
    /// Chatbot AI local qua Ollama — không tốn token cloud, không giới hạn ngày.
    /// System prompt khóa theo vai trò: ngoài phạm vi → từ chối lịch sự.
    /// </summary>
    public class AiChatService : IAiChatService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<AiChatService> _logger;

        public AiChatService(
            IHttpClientFactory httpFactory,
            IConfiguration config,
            ILogger<AiChatService> logger)
        {
            _httpFactory = httpFactory;
            _config = config;
            _logger = logger;
        }

        private string BaseUrl =>
            (_config["Ollama:BaseUrl"] ?? "http://127.0.0.1:11434").TrimEnd('/');

        private string Model =>
            _config["Ollama:Model"] ?? "qwen2.5:7b";

        public async Task<(bool online, string? model, string? loi)> KiemTraAsync(
            CancellationToken ct = default)
        {
            try
            {
                var client = _httpFactory.CreateClient("Ollama");
                using var res = await client.GetAsync($"{BaseUrl}/api/tags", ct);
                if (!res.IsSuccessStatusCode)
                    return (false, null, $"Ollama trả {(int)res.StatusCode}. Hãy mở ứng dụng Ollama trên máy.");
                return (true, Model, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ollama offline");
                return (false, null,
                    "Không kết nối được Ollama. Hãy cài và chạy Ollama trên máy (http://127.0.0.1:11434).");
            }
        }

        public async Task<(bool ok, string? traLoi, string? loi)> ChatAsync(
            string vaiTro,
            string hoTen,
            string cauHoi,
            IReadOnlyList<(string role, string content)>? lichSu,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cauHoi))
                return (false, null, "Vui lòng nhập câu hỏi.");

            var system = BuildSystemPrompt(vaiTro, hoTen);
            var messages = new List<object>
            {
                new { role = "system", content = system }
            };

            if (lichSu != null)
            {
                foreach (var (role, content) in lichSu.TakeLast(12))
                {
                    if (string.IsNullOrWhiteSpace(content)) continue;
                    var r = role is "assistant" or "user" ? role : "user";
                    messages.Add(new { role = r, content = content.Trim() });
                }
            }

            messages.Add(new { role = "user", content = cauHoi.Trim() });

            try
            {
                var client = _httpFactory.CreateClient("Ollama");
                var payload = new
                {
                    model = Model,
                    stream = false,
                    messages,
                    options = new
                    {
                        temperature = 0.4,
                        num_predict = 512
                    }
                };

                using var res = await client.PostAsJsonAsync($"{BaseUrl}/api/chat", payload, ct);
                var raw = await res.Content.ReadAsStringAsync(ct);
                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Ollama chat fail {Code}: {Body}", (int)res.StatusCode, raw);
                    return (false, null,
                        "AI local chưa sẵn sàng. Kiểm tra Ollama đang chạy và đã tải model.");
                }

                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                string? text = null;
                if (root.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var contentEl))
                {
                    text = contentEl.GetString();
                }

                if (string.IsNullOrWhiteSpace(text))
                    return (false, null, "AI không trả lời được. Thử lại sau.");

                return (true, text.Trim(), null);
            }
            catch (TaskCanceledException)
            {
                return (false, null, "AI trả lời quá lâu. Thử câu hỏi ngắn hơn hoặc đợi model tải xong.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi gọi Ollama");
                return (false, null,
                    "Không gọi được AI local. Mở Ollama trên máy rồi thử lại.");
            }
        }

        /// <summary>System prompt khóa phạm vi theo đúng menu/vai trò app OPC.</summary>
        internal static string BuildSystemPrompt(string vaiTro, string hoTen)
        {
            var ten = string.IsNullOrWhiteSpace(hoTen) ? "bạn" : hoTen.Trim();
            var vt = (vaiTro ?? "").Trim();
            if (string.IsNullOrEmpty(vt)) vt = "Người dùng";

            var sb = new StringBuilder();
            sb.AppendLine("Bạn là OPC Bot — trợ lý AI nội bộ của hệ thống quản lý bảo trì-sửa chữa thiết bị cơ điện công ty dược OPC.");
            sb.AppendLine($"Người dùng hiện tại: {ten}.");
            sb.AppendLine($"Vai trò duy nhất được hỗ trợ trong phiên này: «{vt}».");
            sb.AppendLine("Trả lời bằng tiếng Việt, ngắn gọn, thân thiện, chuyên nghiệp.");
            sb.AppendLine("CHỈ trả lời các câu hỏi liên quan đến chức năng và nhiệm vụ của đúng vai trò này trong app.");
            sb.AppendLine("Nếu người dùng hỏi về vai trò khác, chức năng không thuộc phạm vi, hoặc chủ đề ngoài app (chính trị, giải trí, code không liên quan…):");
            sb.AppendLine("→ Từ chối lịch sự và nhắc họ chỉ hỏi việc thuộc vai trò «" + vt + "».");
            sb.AppendLine("Không bịa số liệu hồ sơ thật. Hướng dẫn thao tác trên app khi phù hợp.");
            sb.AppendLine();

            switch (vt)
            {
                case "Tổ trưởng cơ điện":
                case "Tổ trưởng kỹ thuật":
                    sb.AppendLine("Phạm vi được trả lời:");
                    sb.AppendLine("- Danh sách thiết bị, chi tiết thiết bị, quy trình bảo trì/sửa chữa trên thiết bị");
                    sb.AppendLine("- Kế hoạch bảo trì theo năm/tháng, chu kỳ");
                    sb.AppendLine("- Tạo hồ sơ bảo trì, gửi xưởng chốt ngày, chờ giám đốc duyệt");
                    sb.AppendLine("- Phân công nhân viên kỹ thuật sau khi GĐ duyệt; cập nhật khi NV bận");
                    sb.AppendLine("- Hồ sơ sửa chữa (xem/theo dõi theo quyền), lịch sử phân công, hồ sơ vật tư liên quan");
                    sb.AppendLine("Ràng buộc nghiệp vụ quan trọng:");
                    sb.AppendLine("- 1 thiết bị chỉ bảo trì 1 lần/tháng");
                    sb.AppendLine("- Đang bảo trì chưa xong → không tạo SC cho thiết bị đó; BT xong mới tạo SC được");
                    sb.AppendLine("- SC không giới hạn số lần/tháng nhưng không tạo 2 SC cùng thiết bị trong cùng 1 ngày");
                    break;

                case "Xưởng":
                case "Tổ trưởng sản xuất":
                    sb.AppendLine("Phạm vi được trả lời:");
                    sb.AppendLine("- Hồ sơ bảo trì: chốt ngày bảo trì trong tháng, gửi giám đốc duyệt");
                    sb.AppendLine("- Khi GĐ từ chối: chỉnh sửa hồ sơ theo yêu cầu");
                    sb.AppendLine("- Xem quy trình NVKT đã gửi, xác nhận/từ chối quy trình & vật tư");
                    sb.AppendLine("- Hồ sơ sửa chữa: tạo/theo dõi theo quy trình xưởng");
                    sb.AppendLine("Không hướng dẫn phân công NVKT (việc của tổ trưởng cơ điện) hay duyệt cuối của giám đốc.");
                    break;

                case "Giám đốc":
                    sb.AppendLine("Phạm vi được trả lời:");
                    sb.AppendLine("- Duyệt / từ chối hồ sơ bảo trì do xưởng gửi");
                    sb.AppendLine("- Xem thống kê, báo cáo tổng quan bảo trì-sửa chữa");
                    sb.AppendLine("- Theo dõi tình trạng hồ sơ ở mức quản lý");
                    sb.AppendLine("Không hướng dẫn tạo hồ sơ, phân công NVKT hay thao tác kỹ thuật chi tiết trên hiện trường.");
                    break;

                case "Nhân viên kỹ thuật":
                    sb.AppendLine("Phạm vi được trả lời:");
                    sb.AppendLine("- Xem yêu cầu công việc được phân công (bảo trì / sửa chữa)");
                    sb.AppendLine("- Thực hiện quy trình: chọn bước, chọn vật tư + số lượng, hoàn thành bước");
                    sb.AppendLine("- Gửi kết quả cho xưởng xác nhận; cập nhật khi xưởng từ chối");
                    sb.AppendLine("- Xem tổng tiền vật tư theo bước / toàn quy trình trên màn hình của mình");
                    sb.AppendLine("Không hướng dẫn tạo hồ sơ, duyệt GĐ, hay phân công người khác.");
                    break;

                case "Admin hệ thống":
                    sb.AppendLine("Phạm vi được trả lời:");
                    sb.AppendLine("- Quản lý người dùng, vai trò");
                    sb.AppendLine("- Nhật ký hệ thống, tài khoản");
                    sb.AppendLine("Không hướng dẫn thao tác nghiệp vụ BT/SC trên hiện trường trừ khi liên quan cấu hình hệ thống.");
                    break;

                default:
                    sb.AppendLine("Phạm vi: chỉ giải thích chức năng chung của app OPC Maintenance theo vai trò đã đăng nhập.");
                    break;
            }

            return sb.ToString();
        }
    }
}
