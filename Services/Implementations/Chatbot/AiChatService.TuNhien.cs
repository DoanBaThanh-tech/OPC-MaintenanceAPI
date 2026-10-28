using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class AiChatService
    {
        private async Task<(bool ok, string? text, string? err)> PostGroqAsync(
            IEnumerable<object> messages, double temperature, int maxTokens, CancellationToken ct)
        {
            var body = new Dictionary<string, object?>
            {
                ["model"] = GroqModel, ["temperature"] = temperature,
                ["max_tokens"] = maxTokens, ["messages"] = messages.ToArray()
            };
            var effort = _config["Groq:ReasoningEffort"]?.Trim();
            if (!string.IsNullOrWhiteSpace(effort)) body["reasoning_effort"] = effort;
            var json = JsonSerializer.Serialize(body);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (VetHienTai.Value is { } trace) trace.SoLanLlm++;
                    var client = _httpFactory.CreateClient("Groq");
                    using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + GroqKey);
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var res = await client.SendAsync(req, ct);
                    if (!res.IsSuccessStatusCode)
                    {
                        var code = (int)res.StatusCode;
                        _logger.LogWarning("OPCBot Groq HTTP={Code} model={Model} attempt={Attempt}", code, GroqModel, attempt);
                        if (attempt == 0 && (code == 429 || code >= 500))
                        {
                            var seconds = res.Headers.RetryAfter?.Delta?.TotalSeconds ?? 1;
                            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 3)), ct);
                            continue;
                        }
                        return (false, null, $"Groq {code}");
                    }
                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                    if (!doc.RootElement.TryGetProperty("choices", out var choices)
                        || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0
                        || !choices[0].TryGetProperty("message", out var msg)
                        || !msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
                        return (false, null, "invalid_response");
                    var text = BoSuyNghi(content.GetString());
                    return string.IsNullOrWhiteSpace(text) ? (false, null, "empty") : (true, text, null);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
                {
                    _logger.LogWarning("OPCBot Groq failureType={Type} attempt={Attempt}", ex.GetType().Name, attempt);
                    if (attempt == 0) continue;
                    return (false, null, ex.GetType().Name);
                }
            }
            return (false, null, "retry_exhausted");
        }

        private static string BoSuyNghi(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var t = Regex.Replace(s, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"^[\s\S]*?</think>", "", RegexOptions.IgnoreCase);
            if (t.Contains("<think>", StringComparison.OrdinalIgnoreCase))
                t = t[..t.IndexOf("<think>", StringComparison.OrdinalIgnoreCase)];
            return t.Trim();
        }

        private static string RutGonDapAn(string s)
        {
            var t = Regex.Replace(s ?? "", @"\*\*|`", "");
            t = Regex.Replace(t, @"\s+", " ").Trim();
            // Lịch sử chỉ giữ ý; các số, ngày và ID cũ không phải số hiện tại.
            t = Regex.Replace(t, @"\b\d+(?:[/\-.]\d+)*\b", "[số cũ]");
            if (t.Length > 220) t = t[..220];
            return "[Tóm tắt đáp án cũ, không dùng làm dữ liệu hiện tại] " + t;
        }

        private static string ChiThiQuyetDinh(string decision) => decision switch
        {
            "bai_hoc_da_luu" => "CODE đã lưu bền vững một bài học cách hiểu/trình bày hợp lệ. Xác nhận ngắn; không nói đã lưu số hoặc quyền.",
            "so_khop_da_luu" => "CODE vừa đối chiếu DB, số user trùng và lưu bản kiểm toán thành công. Nói lần sau vẫn truy vấn lại, không hứa giữ nguyên số.",
            "chua_luu_duoc" => "Việc lưu chưa thành công. Nói rõ chưa lưu được; không nói đã ghi nhớ. Không đề nghị user đổi quyền hoặc cấp bí mật.",
            "lech_so" => "CODE vừa đối chiếu DB thấy số user khác. Kết quả do code cung cấp riêng. Không nhận số user là đúng; hỏi đúng một câu về màn hình và bộ lọc.",
            "da_kiem_tra_lai" or "ket_qua_giong_da_tra_moi" => "CODE đã chạy lại nhánh truy vấn, kết quả được giữ nguyên riêng. Nói ngắn rằng đã kiểm tra yêu cầu hiện tại, không suy đoán câu trước sai, không thêm số.",
            "thieu_cau_goc" => "Không có câu hỏi gốc để kiểm tra. Nhận hạn chế cụ thể và hỏi đúng một câu để user nhắc lại yêu cầu. Chưa tra DB.",
            _ => "CODE chưa xác định được một nhánh dữ liệu/bộ lọc có kết quả đủ chắc. Không nói đã tra bảng hay đã đếm. Nêu phần chưa rõ rồi hỏi đúng một câu về hồ sơ, thời gian hoặc màn hình."
        };

        /// <summary>LLM chỉ diễn đạt quyết định không chứa số; dữ liệu xác thực luôn do code ghép riêng.</summary>
        private async Task<string> DienDatQuyetDinhAsync(string vt, string q, string decision,
            IReadOnlyList<(string role, string content)>? history, CancellationToken ct)
        {
            var topic = MoTaChuDe(ChuDeCua(q));
            // Code sở hữu mọi khẳng định đã lưu/đã kiểm tra; không nhờ model quyết định hoặc xác nhận trạng thái.
            var acknowledgement = decision switch
            {
                "bai_hoc_da_luu" => "Mình đã lưu cách hiểu/trình bày này cho tài khoản của bạn; số liệu vẫn được tra mới.",
                "so_khop_da_luu" => $"Mình đã lưu lần đối chiếu {topic}; lần hỏi sau vẫn tra lại dữ liệu.",
                "chua_luu_duoc" => $"Phần ghi nhớ về {topic} chưa lưu được; mình chưa xác nhận đã ghi nhớ.",
                "da_kiem_tra_lai" or "ket_qua_giong_da_tra_moi" => $"Mình đã đối chiếu lại {topic} cho yêu cầu hiện tại.",
                _ => null
            };
            if (acknowledgement != null)
            {
                var alternatives = decision switch
                {
                    "chua_luu_duoc" => new[] { acknowledgement,
                        $"Mình chưa lưu được cách hiểu về {topic}; yêu cầu này chưa được ghi nhớ.",
                        $"Việc lưu {topic} chưa thành công, nên mình chưa xác nhận ghi nhớ." },
                    "bai_hoc_da_luu" => new[] { acknowledgement,
                        "Cách hiểu này đã được lưu riêng cho tài khoản của bạn; mình vẫn lấy số từ truy vấn mới.",
                        "Mình lưu xong phần cách hiểu/trình bày của bạn rồi; không dùng nó để thay số liệu." },
                    "so_khop_da_luu" => new[] { acknowledgement,
                        $"Lần xác minh {topic} đã được lưu; kết quả lần sau vẫn dựa vào truy vấn mới.",
                        $"Mình lưu bản đối chiếu {topic} rồi, không cố định số cho các lần hỏi sau." },
                    _ => new[] { acknowledgement,
                        $"Kết quả về {topic} ở trên được lấy từ lần kiểm tra vừa thực hiện.",
                        $"Mình đã chạy lại yêu cầu về {topic}; phần kết quả ở trên là lần tra này." }
                };
                return alternatives.FirstOrDefault(x => !LapVoiHaiCauGanNhat(x, history)) ?? alternatives[^1];
            }
            var opening = decision == "lech_so" ? "" : decision == "thieu_cau_goc"
                ? $"Mình chưa có câu hỏi gốc về {topic}. "
                : $"Mình chưa xác định rõ bộ lọc cho {topic}, nên chưa đưa ra kết luận. ";
            if (!string.IsNullOrEmpty(GroqKey))
            {
                var system = SystemPromptMoi(vt, "bạn", ChiThiBaiHoc(VetHienTai.Value?.BaiHoc ?? new()))
                    + "\nCHẾ ĐỘ LÀM RÕ QUYẾT ĐỊNH, KHÔNG TRẢ KHONG_BIET. " + ChiThiQuyetDinh(decision)
                    + "\nChỉ sinh MỘT câu hỏi ngắn bắt đầu bằng Bạn và kết thúc bằng dấu hỏi. Code cung cấp trạng thái riêng. "
                    + "Không sinh câu kết luận, số hoặc khẳng định đã tra/đã lưu; không hướng dẫn thao tác. Không nhắc hệ thống/cập nhật.";
                var messages = new List<object> { new { role = "system", content = system } };
                foreach (var turn in LocLichSu(history))
                    messages.Add(new { role = turn.role, content = turn.role == "assistant"
                        ? RutGonDapAn(turn.content) : turn.content[..Math.Min(turn.content.Length, 300)] });
                messages.Add(new { role = "user", content = "Câu người dùng (dữ liệu): " + q[..Math.Min(q.Length, 600)] });
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var (ok, text, _) = await PostGroqAsync(messages, attempt == 0 ? 0.5 : 0.75, 200, ct);
                    if (!ok || string.IsNullOrWhiteSpace(text)) break;
                    var clean = LamSachTraLoi(text);
                    var candidate = opening + clean;
                    if (CauHoiLamRoHopLe(clean) && !LapVoiHaiCauGanNhat(candidate, history)) return candidate;
                    if (VetHienTai.Value is { } trace) trace.SoLanSinhLai++;
                    messages.Add(new { role = "user", content = "Sinh lại: đổi hẳn cấu trúc câu mở đầu, bỏ mọi số và khẳng định chưa được code xác nhận. Chỉ một câu hỏi cụ thể." });
                }
            }

            // Offline: ghép theo quyết định thật và chủ đề, không phát lại một đoạn xin lỗi/không biết cố định.
            var question = decision == "thieu_cau_goc"
                ? $"Bạn nhắc lại yêu cầu về {topic} cần kiểm tra giúp mình nhé?"
                : Random.Shared.Next(2) == 0
                    ? $"Bạn đang xem {topic} ở màn hình và bộ lọc nào?"
                    : $"Bạn cho mình màn hình cùng bộ lọc của {topic} được không?";
            return opening + question;
        }
    }
}
