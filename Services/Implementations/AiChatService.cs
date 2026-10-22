using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    /// <summary>
    /// Chatbot AI local (Ollama) — giọng thân mật, kèm số liệu thật từ DB.
    /// </summary>
    public class AiChatService : IAiChatService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<AiChatService> _logger;
        private readonly OPCDbContext _db;

        public AiChatService(
            IHttpClientFactory httpFactory,
            IConfiguration config,
            ILogger<AiChatService> logger,
            OPCDbContext db)
        {
            _httpFactory = httpFactory;
            _config = config;
            _logger = logger;
            _db = db;
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
                return (false, null, "Bạn ơi, gửi giúp mình một câu hỏi nhé.");

            var soLieu = await LaySoLieuThatAsync(vaiTro, ct);
            var system = BuildSystemPrompt(vaiTro, hoTen, soLieu);

            var messages = new List<object>
            {
                new { role = "system", content = system }
            };

            if (lichSu != null)
            {
                foreach (var (role, content) in lichSu.TakeLast(10))
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
                        temperature = 0.55,
                        num_predict = 600
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
                    return (false, null, "Mình chưa trả lời được lúc này, bạn thử hỏi lại giúp nhé.");

                return (true, text.Trim(), null);
            }
            catch (TaskCanceledException)
            {
                return (false, null, "Câu trả lời hơi lâu. Bạn thử hỏi ngắn hơn giúp mình nhé.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi gọi Ollama");
                return (false, null,
                    "Không gọi được AI local. Mở Ollama trên máy rồi thử lại.");
            }
        }

        /// <summary>Số liệu thật từ DB — gắn vào prompt để bot trả lời số ngay.</summary>
        private async Task<string> LaySoLieuThatAsync(string vaiTro, CancellationToken ct)
        {
            try
            {
                var now = DateTime.Now;
                var nam = now.Year;
                var thang = now.Month;
                var sb = new StringBuilder();
                sb.AppendLine($"Ngày giờ hệ thống: {now:dd/MM/yyyy HH:mm}.");
                sb.AppendLine($"Năm {nam}, tháng {thang}.");

                // Đếm hồ sơ BT theo trạng thái
                var btGroups = await _db.HoSoBaoTris.AsNoTracking()
                    .GroupBy(h => h.TrangThai ?? "")
                    .Select(g => new { TrangThai = g.Key, So = g.Count() })
                    .ToListAsync(ct);
                sb.AppendLine("Hồ sơ bảo trì (toàn hệ thống):");
                foreach (var g in btGroups.OrderByDescending(x => x.So))
                    sb.AppendLine($"- {g.TrangThai}: {g.So}");

                var scGroups = await _db.HoSoSuaChuas.AsNoTracking()
                    .GroupBy(h => h.TrangThai ?? "")
                    .Select(g => new { TrangThai = g.Key, So = g.Count() })
                    .ToListAsync(ct);
                sb.AppendLine("Hồ sơ sửa chữa (toàn hệ thống):");
                foreach (var g in scGroups.OrderByDescending(x => x.So))
                    sb.AppendLine($"- {g.TrangThai}: {g.So}");

                // Ngày bảo trì dự kiến sắp tới (từ chi tiết kế hoạch)
                var sapToi = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                    .Where(c => c.NgayDuKienBaoTri >= DateOnly.FromDateTime(now))
                    .OrderBy(c => c.NgayDuKienBaoTri)
                    .Take(8)
                    .Select(c => new
                    {
                        c.NgayDuKienBaoTri,
                        TenTb = c.MaThietBiNavigation != null
                            ? c.MaThietBiNavigation.TenThietBi
                            : ("TB#" + c.MaThietBi)
                    })
                    .ToListAsync(ct);
                if (sapToi.Count > 0)
                {
                    sb.AppendLine("Lịch bảo trì dự kiến sắp tới:");
                    foreach (var x in sapToi)
                        sb.AppendLine($"- {x.NgayDuKienBaoTri:dd/MM/yyyy}: {x.TenTb}");
                }
                else
                    sb.AppendLine("Chưa có ngày bảo trì dự kiến sắp tới trong kế hoạch.");

                // Tổng tiền vật tư đã xác nhận
                var vtXacNhan = await _db.HoSoSuDungVatTus.AsNoTracking()
                    .Where(h => h.TrangThai == "Xác nhận" || h.TrangThai == "Đã duyệt")
                    .Select(h => new { h.TongTien, h.NgayThucHien })
                    .ToListAsync(ct);
                var tongAll = vtXacNhan.Sum(x => x.TongTien);
                var tongThang = vtXacNhan
                    .Where(x => x.NgayThucHien.Year == nam && x.NgayThucHien.Month == thang)
                    .Sum(x => x.TongTien);
                var tongNam = vtXacNhan.Where(x => x.NgayThucHien.Year == nam).Sum(x => x.TongTien);
                sb.AppendLine($"Tổng tiền vật tư đã xác nhận (toàn bộ): {tongAll:N0} ₫");
                sb.AppendLine($"Tổng tiền vật tư tháng {thang}/{nam}: {tongThang:N0} ₫");
                sb.AppendLine($"Tổng tiền vật tư năm {nam}: {tongNam:N0} ₫");

                // Số thiết bị
                var soTb = await _db.ThietBis.AsNoTracking().CountAsync(ct);
                sb.AppendLine($"Số thiết bị trong hệ thống: {soTb}");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không lấy được số liệu cho AI");
                return "Không tải được số liệu realtime lúc này.";
            }
        }

        internal static string BuildSystemPrompt(string vaiTro, string hoTen, string soLieu)
        {
            var ten = string.IsNullOrWhiteSpace(hoTen) ? "bạn" : hoTen.Trim();
            var vt = string.IsNullOrWhiteSpace(vaiTro) ? "Người dùng" : vaiTro.Trim();

            var sb = new StringBuilder();
            sb.AppendLine("Bạn là OPC Bot — trợ lý thân thiện của app quản lý bảo trì & sửa chữa thiết bị cơ điện, công ty cổ phần dược phẩm OPC.");
            sb.AppendLine($"Đang trò chuyện với: {ten} (gọi tên một cách gần gũi, tự nhiên).");
            sb.AppendLine($"Vai trò của họ trong app: «{vt}».");
            sb.AppendLine();
            sb.AppendLine("PHONG CÁCH TRẢ LỜI:");
            sb.AppendLine("- Nói chuyện như đồng nghiệp hỗ trợ, ấm áp, dễ hiểu — không cứng nhắc, không máy móc.");
            sb.AppendLine("- Xưng «mình», gọi người dùng bằng tên hoặc «bạn».");
            sb.AppendLine("- Trả lời ngắn gọn, rõ ý; có thể dùng emoji nhẹ khi phù hợp.");
            sb.AppendLine("- Khi hỏi số lượng, ngày bảo trì, tổng tiền, trạng thái hồ sơ: **ưu tiên dùng đúng số liệu thật** trong khối DỮ LIỆU HỆ THỐNG bên dưới, nêu số cụ thể ngay, đừng vòng vo.");
            sb.AppendLine("- Không bịa số. Nếu dữ liệu không có, nói thật là chưa có trên hệ thống.");
            sb.AppendLine("- Chỉ hỗ trợ công việc thuộc vai trò «" + vt + "». Hỏi việc vai trò khác / ngoài app → từ chối nhẹ nhàng và gợi ý hỏi đúng việc của họ.");
            sb.AppendLine();
            sb.AppendLine("DỮ LIỆU HỆ THỐNG (cập nhật realtime — dùng khi trả lời số liệu):");
            sb.AppendLine(soLieu);
            sb.AppendLine();

            switch (vt)
            {
                case "Tổ trưởng cơ điện":
                case "Tổ trưởng kỹ thuật":
                    sb.AppendLine("Bạn hỗ trợ: thiết bị, kế hoạch BT, tạo hồ sơ BT, phân công NVKT, theo dõi SC, ràng buộc (1 BT/tháng/thiết bị; đang BT chưa xong thì chưa tạo SC; không 2 SC cùng thiết bị trong 1 ngày).");
                    break;
                case "Xưởng":
                case "Tổ trưởng sản xuất":
                    sb.AppendLine("Bạn hỗ trợ: chốt ngày BT, gửi GĐ duyệt, xử lý khi GĐ từ chối, xác nhận/từ chối quy trình & vật tư NVKT, hồ sơ SC phía xưởng.");
                    break;
                case "Giám đốc":
                    sb.AppendLine("Bạn hỗ trợ: duyệt/từ chối hồ sơ BT, xem thống kê số lượng & chi phí vật tư, góc nhìn quản lý.");
                    break;
                case "Nhân viên kỹ thuật":
                    sb.AppendLine("Bạn hỗ trợ: việc được phân công, chọn bước, vật tư, hoàn thành gửi xưởng, xem tổng tiền trên quy trình của mình.");
                    break;
                case "Admin hệ thống":
                    sb.AppendLine("Bạn hỗ trợ: tài khoản, vai trò, nhật ký hệ thống.");
                    break;
                default:
                    sb.AppendLine("Bạn hỗ trợ giải thích chức năng app theo vai trò đã đăng nhập.");
                    break;
            }

            return sb.ToString();
        }
    }
}
