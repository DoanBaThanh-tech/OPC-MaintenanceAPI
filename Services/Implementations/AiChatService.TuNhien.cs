using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    /// <summary>
    /// Giai đoạn 1: gọi Groq dùng chung (có retry), lọc phần «suy nghĩ» của model,
    /// diễn đạt tự nhiên khi không biết / nhận lỗi (không dùng một câu hằng duy nhất),
    /// tóm tắt đáp án cũ để gửi cho LLM mà không bị chép lại.
    /// </summary>
    public partial class AiChatService
    {
        // ===================== GỌI GROQ DÙNG CHUNG =====================

        private async Task<(bool ok, string? text, string? err)> PostGroqAsync(
            IEnumerable<object> messages, double temperature, int maxTokens, CancellationToken ct)
        {
            var body = new Dictionary<string, object?>
            {
                ["model"] = GroqModel,
                ["temperature"] = temperature,
                ["max_tokens"] = maxTokens,
                ["messages"] = messages.ToArray()
            };
            var effort = _config["Groq:ReasoningEffort"]?.Trim();
            if (!string.IsNullOrWhiteSpace(effort)) body["reasoning_effort"] = effort;

            const string url = "https://api.groq.com/openai/v1/chat/completions";
            var json = JsonSerializer.Serialize(body);

            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var client = _httpFactory.CreateClient("Groq");
                    using var req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + GroqKey);
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var res = await client.SendAsync(req, ct);
                    var raw = await res.Content.ReadAsStringAsync(ct);
                    if (!res.IsSuccessStatusCode)
                    {
                        var code = (int)res.StatusCode;
                        _logger.LogWarning("Groq HTTP {Code} model={Model}: {Body}",
                            code, GroqModel, raw.Length > 500 ? raw[..500] : raw);
                        if (attempt == 0 && (code == 429 || code >= 500))
                        {
                            var wait = res.Headers.RetryAfter?.Delta is { } d ? Math.Min(d.TotalSeconds, 5) : 2;
                            await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                            continue;
                        }
                        return (false, null, $"Groq {code}");
                    }

                    using var doc = JsonDocument.Parse(raw);
                    if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                        return (false, null, "no choices");

                    var msg = choices[0].GetProperty("message");
                    // KHÔNG lấy "reasoning" làm đáp án (đó là phần suy nghĩ nháp của model).
                    var text = msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString() : null;
                    text = BoSuyNghi(text);
                    return string.IsNullOrWhiteSpace(text) ? (false, null, "empty") : (true, text, null);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Groq call failed model={Model}", GroqModel);
                    if (attempt == 0) { await Task.Delay(1000, ct); continue; }
                    return (false, null, ex.Message);
                }
            }
            return (false, null, "retry exhausted");
        }

        private static string BoSuyNghi(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var t = Regex.Replace(s, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"^[\s\S]*?</think>", "", RegexOptions.IgnoreCase); // thẻ mở bị cắt
            return t.Trim();
        }

        // ===================== TÓM TẮT LỊCH SỬ CHO LLM =====================

        /// <summary>Rút gọn đáp án cũ (1 dòng, tối đa ~180 ký tự) để LLM hiểu mạch nhưng không chép lại.</summary>
        private static string RutGonDapAn(string s)
        {
            var t = Regex.Replace(s ?? "", @"\*\*|`", "");
            t = Regex.Replace(t, @"\s+", " ").Trim();
            if (t.Length > 180) t = t[..180] + "…";
            return "[tóm tắt câu trả lời trước] " + t;
        }

        private static string BoChuDa(string s)
        {
            var b = (s ?? "").TrimStart();
            if (b.StartsWith("Dạ,", StringComparison.OrdinalIgnoreCase)) b = b[3..].TrimStart();
            else if (b.StartsWith("Dạ ", StringComparison.OrdinalIgnoreCase)) b = b[3..].TrimStart();
            return b.Length > 0 ? char.ToUpper(b[0]) + b[1..] : b;
        }

        // ===================== DIỄN ĐẠT TỰ NHIÊN (KHÔNG LẶP VĂN MẪU) =====================

        private static readonly ConcurrentDictionary<string, int> _mauCuoi = new();

        /// <summary>Chọn 1 trong nhiều cách nói, không bao giờ chọn lại đúng cách vừa dùng.</summary>
        private static string MauXoayVong(string khoa, params string[] ds)
        {
            if (ds.Length == 1) return ds[0];
            var cuoi = _mauCuoi.TryGetValue(khoa, out var l) ? l : -1;
            int i;
            do { i = Random.Shared.Next(ds.Length); } while (i == cuoi);
            _mauCuoi[khoa] = i;
            return ds[i];
        }

        /// <summary>
        /// Khi chưa đủ chắc để trả lời: LLM tự diễn đạt (nói thẳng phần chưa chắc, nêu đã tra gì,
        /// hỏi lại đúng 1 câu / gợi ý hướng gần nhất). Groq lỗi hoặc câu sinh ra không an toàn → dùng mẫu xoay vòng.
        /// </summary>
        private async Task<string> DienDatKhongBietAsync(
            string vt, string ten, string cauHoi,
            IReadOnlyList<(string role, string content)>? lichSu,
            CancellationToken ct, bool daDoiChieu = false)
        {
            if (!string.IsNullOrEmpty(GroqKey))
            {
                var gan = (lichSu ?? Array.Empty<(string role, string content)>())
                    .Where(x => string.Equals(x.role, "assistant", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(x.content))
                    .Select(x => x.content.Trim())
                    .TakeLast(3).ToList();

                var sys =
                    "Bạn là OPC Bot — trợ lý trong app bảo trì/sửa chữa thiết bị của công ty dược OPC.\n"
                    + $"Người dùng tên {ten}, vai trò «{vt}».\n"
                    + "Tình huống: bạn CHƯA đủ chắc để trả lời câu hỏi bằng dữ liệu app"
                    + (daDoiChieu ? " dù đã đối chiếu lại." : ".") + "\n"
                    + "Hãy viết hai hoặc ba câu tiếng Việt tự nhiên, thân thiện như một đồng nghiệp:\n"
                    + "- nói thẳng phần mình chưa chắc, nêu đã đối chiếu những gì (hồ sơ bảo trì/sửa chữa, thiết bị, phân công theo vai trò);\n"
                    + "- rồi hỏi lại ĐÚNG MỘT câu cụ thể, hoặc gợi ý hai-ba hướng gần nhất (hồ sơ nào, tháng nào, trạng thái nào).\n"
                    + "CẤM: bịa số liệu, nêu bất kỳ con số nào, nhắc «hệ thống/cập nhật», văn mẫu cứng nhắc, "
                    + "lặp lại cách nói trong các câu đã trả lời gần đây. Xưng «mình», gọi «bạn», mở đầu bằng «Dạ,».";

                var user = new StringBuilder();
                user.AppendLine("Câu hỏi của người dùng: " + cauHoi);
                if (gan.Count > 0)
                {
                    user.AppendLine("Các câu bot đã trả lời gần đây (đừng nói giống):");
                    foreach (var g in gan) user.AppendLine("- " + RutGonDapAn(g));
                }

                var (ok, text, _) = await PostGroqAsync(
                    new object[] { new { role = "system", content = sys }, new { role = "user", content = user.ToString() } },
                    0.7, 250, ct);

                if (ok && !string.IsNullOrWhiteSpace(text))
                {
                    var s = LamSachTraLoi(text!);
                    if (!string.IsNullOrWhiteSpace(s) && !LaKhongBiet(s) && s.Length <= 600
                        && !CoSoLaLam(s, cauHoi)
                        && !gan.Any(g => DoGiongNhau(ChuanHoa(s), ChuanHoa(g), 0.6)))
                        return s;
                }
            }

            return daDoiChieu
                ? MauXoayVong("kb_doi_chieu",
                    "Dạ, mình đã đếm lại từ dữ liệu app nhưng vẫn chưa xác định được con số khớp với câu này nên không dám đoán. Bạn cho mình thêm một mốc — tháng nào, trạng thái nào hoặc mã hồ sơ — mình tra lại liền nhé.",
                    "Dạ, mình đối chiếu lại rồi mà vẫn chưa chắc con số nào là đúng ý bạn, nên mình chưa nói bừa. Bạn đang xem màn hình nào, lọc theo tháng hay trạng thái nào để mình soi cùng nhé?",
                    "Dạ, mình đã tra lại theo dữ liệu trên app nhưng chưa ra kết quả đủ chắc. Bạn nói rõ giúp mình hồ sơ hoặc khoảng thời gian cần xem, mình kiểm tra lại ngay.")
                : MauXoayVong("kb",
                    "Dạ, câu này mình đã thử đối chiếu hồ sơ và thiết bị theo vai trò của bạn nhưng chưa bắt đúng ý, nên mình không dám đoán số. Bạn đang muốn xem hồ sơ nào, tháng nào hay trạng thái nào vậy?",
                    "Dạ, mình chưa hiểu rõ bạn đang hỏi phần nào. Bạn cho mình thêm một mốc — mã hồ sơ, tên thiết bị hoặc tháng cần xem — mình tra lại ngay nhé.",
                    "Dạ, mình tra thử mà chưa thấy dữ liệu nào khớp với ý bạn. Có phải bạn muốn hỏi về hồ sơ bảo trì, sửa chữa hay thiết bị không? Nói rõ giúp mình để mình trả lời đúng.",
                    "Dạ, phần này mình chưa chắc nên chưa dám trả lời bừa. Bạn nói cụ thể hơn một chút được không — hồ sơ nào, thời gian nào hoặc trạng thái nào?");
        }

        /// <summary>true nếu câu sinh ra có con số không xuất hiện trong câu hỏi (nghi bịa số).</summary>
        private static bool CoSoLaLam(string s, string cauHoi)
        {
            var cho = Regex.Matches(cauHoi ?? "", @"\d+").Select(m => m.Value).ToHashSet();
            return Regex.Matches(s, @"\d+").Any(m => !cho.Contains(m.Value));
        }
    }
}