using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    /// <summary>
    /// OPC Bot: phân tích từng câu hỏi → tra DB theo đúng ý định → chỉ dùng LLM cho câu chưa rõ.
    /// Không lưu và không phát lại câu trả lời cũ. Không biết thì xin lỗi thân thiện, không đoán bừa.
    /// </summary>
    public partial class AiChatService : IAiChatService
    {
        private readonly OPCDbContext _db;
        private readonly ILogger<AiChatService> _logger;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;

        public AiChatService(
            OPCDbContext db,
            ILogger<AiChatService> logger,
            IHttpClientFactory httpFactory,
            IConfiguration config)
        {
            _db = db;
            _logger = logger;
            _httpFactory = httpFactory;
            _config = config;
            ChatbotTriNhoStore.DatDuongDan(_config["Chatbot:TriNhoPath"]);
        }

        private string? GroqKey => _config["Groq:ApiKey"]?.Trim();

        /// <summary>Model Groq đang dùng (free tier).</summary>
        private string GroqModel =>
            string.IsNullOrWhiteSpace(_config["Groq:Model"])
                ? "qwen/qwen3.8-27b"
                : _config["Groq:Model"]!.Trim();

        public Task<(bool online, string? model, string? loi)> KiemTraAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(GroqKey))
                return Task.FromResult<(bool, string?, string?)>((true, "OPC-RuleFallback",
                    "Chưa cấu hình Groq:ApiKey — vẫn trả lời bằng dữ liệu app."));
            return Task.FromResult<(bool, string?, string?)>((true, GroqModel, null));
        }

        public async Task<(bool ok, string? traLoi, string? loi)> ChatAsync(
            int maNguoiDung,
            string vaiTro,
            string hoTen,
            string cauHoi,
            IReadOnlyList<(string role, string content)>? lichSu,
            CancellationToken ct = default,
            bool baoSai = false,
            string? cauHoiGoc = null,
            string? traLoiSai = null)
        {
            if (string.IsNullOrWhiteSpace(cauHoi) && !baoSai)
                return (false, null, "Gửi giúp mình một câu hỏi nhé.");

            var ten = string.IsNullOrWhiteSpace(hoTen) ? "bạn" : hoTen.Trim();
            var vt = (vaiTro ?? "Người dùng").Trim();
            var cauHienTai = string.IsNullOrWhiteSpace(cauHoi) ? "" : cauHoi.Trim();
            var q = ChuanHoa(cauHienTai);

            if (LaNhayCam(q))
                return (true, "Dạ, mình không chia sẻ thông tin bảo mật của app.", null);

            try
            {
                // 1) Người dùng bấm «Báo sai» → bỏ đáp án cũ, đối chiếu lại từ dữ liệu app.
                if (baoSai || LaBaoCauTraLoiSai(q, cauHienTai))
                    return (true, await XuLyBaoSaiAsync(maNguoiDung, vt, ten, cauHoiGoc, lichSu, ct), null);

                // 2) Người dùng chỉnh lại cách hiểu / con số → ghi nhớ rồi trả lời lại.
                var hoc = await XuLyDayTuUserAsync(maNguoiDung, vt, ten, q, lichSu, ct);
                if (!string.IsNullOrWhiteSpace(hoc))
                    return (true, hoc, null);

                // 3) Ranh giới vai trò (chỉ chặn thao tác hộ vai trò khác).
                var chan = TuChoiVuotVaiTro(vt, q);
                if (!string.IsNullOrWhiteSpace(chan))
                    return (true, chan, null);

                if (LaChaoHoi(q))
                    return (true,
                        $"Dạ, chào {ten}! Mình là OPC Bot. Bạn muốn xem hồ sơ, thiết bị, phân công hay tiến độ công việc?",
                        null);
                if (LaCamOn(q))
                    return (true, "Dạ, có gì bạn cứ hỏi mình nhé.", null);

                // 4) Định tuyến ý định + dữ liệu thật trong app.
                var kq = await XuLyYDinhAsync(maNguoiDung, vt, ten, q, cauHienTai, ct);
                if (kq.DaXuLy && !string.IsNullOrWhiteSpace(kq.TraLoi))
                    return (true, ThemGiongDieu(kq.TraLoi!, q, kq.SoLuong), null);

                // 5) LLM chỉ nhận CONTEXT + câu hỏi hiện tại (không nhận đáp án cũ để khỏi copy).
                if (!string.IsNullOrEmpty(GroqKey))
                {
                    var context = await NapContextVaiTroAsync(maNguoiDung, vt, ct);
                    var (ok, text, err) = await GoiGroqAsync(vt, ten, cauHienTai, context, lichSu, ct);
                    if (ok && !string.IsNullOrWhiteSpace(text))
                    {
                        var s = LamSachTraLoi(text!);
                        if (!LaKhongBiet(s) && !LaLapLaiDapAnCu(s, cauHienTai, lichSu))
                            return (true, s, null);
                        _logger.LogWarning("Groq trả lời không dùng được (không biết hoặc lặp đáp án cũ).");
                    }
                    else
                    {
                        _logger.LogWarning("Groq lỗi: {Err}", err);
                    }
                }

                // 6) Câu hỏi quy trình → kiến thức nghiệp vụ; còn lại xin lỗi, không đoán.
                var kt = TraLoiKienThuc(vt, q);
                if (!string.IsNullOrWhiteSpace(kt))
                    return (true, kt, null);

                return (true, CauTraLoiKhongBiet(), null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chatbot lỗi khi xử lý câu hỏi");
                return (false, null, "Mình gặp lỗi khi xử lý câu hỏi. Bạn thử lại giúp mình nhé.");
            }
        }

        // ===================== CONTEXT CHO LLM =====================

        private async Task<string> NapContextVaiTroAsync(int maNd, string vt, CancellationToken ct)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Vai_tro: {vt}");
            sb.AppendLine($"Hom_nay: {DateTime.Now:dd/MM/yyyy HH:mm}");
            sb.AppendLine("Pham_vi: chỉ dữ liệu phục vụ vai trò này.");

            var tbs = await _db.ThietBis.AsNoTracking()
                .Select(t => new { t.MaThietBi, t.TenThietBi, t.LoaiThietBi, t.ViTriLapDat, t.TinhTrangHienTai })
                .ToListAsync(ct);
            sb.AppendLine($"Tong_thiet_bi: {tbs.Count}");
            foreach (var g in tbs.GroupBy(x => x.TinhTrangHienTai ?? "(trống)").OrderBy(g => g.Key))
                sb.AppendLine($"  TB_TT[{g.Key}]={g.Count()}");
            foreach (var t in tbs.OrderBy(x => x.TenThietBi).Take(40))
                sb.AppendLine($"  TB#{t.MaThietBi}|{t.TenThietBi}|loai={t.LoaiThietBi}|vitri={t.ViTriLapDat}|tt={t.TinhTrangHienTai}");

            var bts = (await _db.HoSoBaoTris.AsNoTracking()
                .Select(h => new
                {
                    h.MaHoSoBaoTri,
                    h.TrangThai,
                    Ngay = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao,
                    TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                }).ToListAsync(ct))
                .Where(h => !LaTrangThaiAnTrenApp(h.TrangThai)).ToList();
            sb.AppendLine($"Tong_HS_BT: {bts.Count}");
            foreach (var g in bts.GroupBy(x => x.TrangThai).OrderBy(g => g.Key))
                sb.AppendLine($"  BT_TT[{g.Key}]={g.Count()}");
            foreach (var h in bts.OrderByDescending(x => x.Ngay).Take(40))
                sb.AppendLine($"  BT#{h.MaHoSoBaoTri}|{h.TenTb}|tt={h.TrangThai}|ngay={h.Ngay:dd/MM/yyyy}");

            var scs = (await _db.HoSoSuaChuas.AsNoTracking()
                .Select(h => new
                {
                    h.MaHoSoSuaChua,
                    h.TrangThai,
                    Ngay = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao,
                    TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                }).ToListAsync(ct))
                .Where(h => !LaTrangThaiAnTrenApp(h.TrangThai)).ToList();
            sb.AppendLine($"Tong_HS_SC: {scs.Count}");
            foreach (var g in scs.GroupBy(x => x.TrangThai).OrderBy(g => g.Key))
                sb.AppendLine($"  SC_TT[{g.Key}]={g.Count()}");
            foreach (var h in scs.OrderByDescending(x => x.Ngay).Take(30))
                sb.AppendLine($"  SC#{h.MaHoSoSuaChua}|{h.TenTb}|tt={h.TrangThai}|ngay={h.Ngay:dd/MM/yyyy}");

            var kh = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.NgayDuKienBaoTri >= DateOnly.FromDateTime(DateTime.Today.AddMonths(-1)))
                .OrderBy(c => c.NgayDuKienBaoTri).Take(40)
                .Select(c => new
                {
                    c.NgayDuKienBaoTri,
                    c.MaHoSoBaoTri,
                    Ten = c.MaThietBiNavigation != null ? c.MaThietBiNavigation.TenThietBi : ("TB#" + c.MaThietBi)
                }).ToListAsync(ct);
            sb.AppendLine("Lich_ke_hoach_BT:");
            foreach (var c in kh)
                sb.AppendLine($"  {c.NgayDuKienBaoTri:dd/MM/yyyy}|{c.Ten}|hs={(c.MaHoSoBaoTri.HasValue ? "BT#" + c.MaHoSoBaoTri : "-")}");

            if (vt == "Nhân viên kỹ thuật")
            {
                var maNv = await _db.NhanViens.AsNoTracking()
                    .Where(n => n.MaNguoiDung == maNd)
                    .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
                sb.AppendLine($"Ma_NV: {maNv}");
                if (maNv != null)
                {
                    var pc = await _db.PhanCongCongViecs.AsNoTracking()
                        .Where(p => p.MaNhanVienThucHien == maNv)
                        .OrderByDescending(p => p.NgayPhanCong).Take(20)
                        .Select(p => new { p.MaPhanCong, p.MaHoSoBaoTri, p.MaHoSoSuaChua, p.TrangThai })
                        .ToListAsync(ct);
                    foreach (var p in pc)
                    {
                        var hs = p.MaHoSoBaoTri != null ? $"BT#{p.MaHoSoBaoTri}" : $"SC#{p.MaHoSoSuaChua}";
                        sb.AppendLine($"  PC#{p.MaPhanCong}|{hs}|tt={p.TrangThai}");
                    }

                    var maBts = pc.Where(x => x.MaHoSoBaoTri != null).Select(x => x.MaHoSoBaoTri!.Value).Distinct().ToList();
                    var maScs = pc.Where(x => x.MaHoSoSuaChua != null).Select(x => x.MaHoSoSuaChua!.Value).Distinct().ToList();
                    var buoc = await _db.TienDoBuocQuyTrinhs.AsNoTracking()
                        .Where(t => (t.MaHoSoBaoTri != null && maBts.Contains(t.MaHoSoBaoTri.Value))
                                    || (t.MaHoSoSuaChua != null && maScs.Contains(t.MaHoSoSuaChua.Value)))
                        .OrderByDescending(t => t.ThoiDiemCapNhat).Take(100)
                        .Select(t => new { t.MaHoSoBaoTri, t.MaHoSoSuaChua, t.SoBuoc, t.MoTaBuoc, t.TrangThai })
                        .ToListAsync(ct);
                    foreach (var g in buoc.GroupBy(t => t.MaHoSoBaoTri != null ? $"BT#{t.MaHoSoBaoTri}" : $"SC#{t.MaHoSoSuaChua}"))
                    {
                        var uniq = g.GroupBy(x => x.SoBuoc).Select(x => x.First()).OrderBy(x => x.SoBuoc);
                        sb.AppendLine($"  Buoc[{g.Key}]: {string.Join("; ", uniq.Select(x => $"B{x.SoBuoc}:{x.MoTaBuoc}({x.TrangThai})"))}");
                    }
                }
            }
            else
            {
                var pcs = await _db.PhanCongCongViecs.AsNoTracking()
                    .OrderByDescending(p => p.NgayPhanCong).Take(40)
                    .Select(p => new
                    {
                        p.MaPhanCong,
                        p.MaHoSoBaoTri,
                        p.MaHoSoSuaChua,
                        p.TrangThai,
                        p.NgayPhanCong,
                        TenNv = p.MaNhanVienThucHienNavigation != null
                            ? p.MaNhanVienThucHienNavigation.HoTen
                            : ("NV#" + p.MaNhanVienThucHien)
                    }).ToListAsync(ct);
                sb.AppendLine($"Tong_phan_cong_gan_day: {pcs.Count}");
                foreach (var p in pcs)
                {
                    var hs = p.MaHoSoBaoTri != null ? $"BT#{p.MaHoSoBaoTri}" : $"SC#{p.MaHoSoSuaChua}";
                    sb.AppendLine($"  PC#{p.MaPhanCong}|{hs}|nv={p.TenNv}|tt={p.TrangThai}|ngay={p.NgayPhanCong:dd/MM/yyyy}");
                }
            }

            var ghiChu = GhiChuDaHoc(vt);
            if (!string.IsNullOrWhiteSpace(ghiChu))
            {
                sb.AppendLine("GHI_NHO_NGUOI_DUNG (cách hiểu đã được chỉnh — ưu tiên):");
                sb.AppendLine(ghiChu);
            }

            var text = sb.ToString();
            if (text.Length > 24000) text = text[..24000] + "\n...(rút gọn)";
            return text;
        }

        // ===================== GROQ =====================

        private async Task<(bool ok, string? text, string? err)> GoiGroqAsync(
            string vt,
            string ten,
            string cauHoi,
            string context,
            IReadOnlyList<(string role, string content)>? lichSu,
            CancellationToken ct,
            bool suaSai = false)
        {
            var system =
                "Bạn là OPC Bot — trợ lý thân thiện trong app bảo trì/sửa chữa thiết bị của công ty dược OPC.\n" +
                $"Vai trò người đang chat: «{vt}» | Tên: {ten}.\n\n" +
                "QUY TẮC BẮT BUỘC:\n" +
                "1) Mỗi câu hỏi là một yêu cầu riêng. Chỉ trả lời đúng câu đang hỏi. Cấm lặp lại đáp án của câu trước nếu không cùng ý.\n" +
                "2) Chỉ dùng CONTEXT được cung cấp. Không bịa số liệu, không tự thêm hồ sơ/thiết bị không có trong CONTEXT.\n" +
                "3) «Hồ sơ chờ phân công» là HỒ SƠ BẢO TRÌ ở trạng thái Đã duyệt/Chờ phân công, KHÔNG phải bản ghi phân công (PC#).\n" +
                "4) Hỏi số lượng → chỉ nêu số. Hỏi liệt kê → tối đa 8 dòng, thêm «… còn N» nếu còn.\n" +
                "5) Không đủ dữ liệu để chắc chắn → chỉ trả đúng một dòng: KHONG_BIET. Không giải thích, không nhắc «cập nhật», không nhắc «hệ thống».\n" +
                "6) Câu nhiều vế → tách 1) 2) rõ ràng.\n" +
                "7) Câu không dấu → hiểu theo nghĩa tiếng Việt; nếu cần thì nhắc lại ý bằng tiếng Việt có dấu trong câu trả lời.\n" +
                "8) Người dùng gấp/khó chịu → một câu trấn an ngắn rồi trả đúng việc, không giảng dài.\n" +
                "9) Không mô tả thao tác thuộc vai trò khác; chỉ nói vai trò nào phụ trách bước đó.\n" +
                "10) Xưng «mình», gọi người dùng «bạn». Có thể dùng **đậm** và gạch đầu dòng •, không dùng bảng.\n\n" +
                "KIẾN THỨC NGHIỆP VỤ:\n" + XayDungKienThucHeThong(vt) + "\n" +
                "PHẠM VI VAI TRÒ:\n" + MoTaPhamVi(vt) +
                (suaSai ? "\n[CHE_DO_SUA_SAI: câu trả lời trước sai — cấm lặp lại số liệu cũ]" : "");

            var user = new StringBuilder();
            user.AppendLine("CONTEXT:");
            user.AppendLine(context);

            // Chỉ gửi câu hỏi trước của người dùng — tuyệt đối không gửi đáp án cũ để tránh copy.
            var cauHoiTruoc = (lichSu ?? Array.Empty<(string role, string content)>())
                .Where(x => string.Equals(x.role, "user", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.content?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .TakeLast(3)
                .ToList();
            if (cauHoiTruoc.Count > 0)
            {
                user.AppendLine("CÂU HỎI TRƯỚC (chỉ để hiểu mạch hội thoại, KHÔNG phải đáp án mẫu):");
                foreach (var c in cauHoiTruoc)
                    user.AppendLine("- " + c);
            }

            user.AppendLine("CÂU HỎI HIỆN TẠI (chỉ trả lời câu này):");
            user.AppendLine(cauHoi);
            user.AppendLine("Nếu CONTEXT không đủ, trả đúng: KHONG_BIET");

            var body = new
            {
                model = GroqModel,
                temperature = suaSai ? 0.05 : 0.1,
                max_tokens = 800,
                messages = new object[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user.ToString() }
                }
            };

            const string url = "https://api.groq.com/openai/v1/chat/completions";
            try
            {
                var client = _httpFactory.CreateClient("Groq");
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + GroqKey);
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using var res = await client.SendAsync(req, ct);
                var raw = await res.Content.ReadAsStringAsync(ct);
                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Groq HTTP {Code} model={Model}: {Body}",
                        (int)res.StatusCode, GroqModel, raw.Length > 500 ? raw[..500] : raw);
                    return (false, null, $"Groq {(int)res.StatusCode}");
                }

                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    return (false, null, "no choices");

                var msg = choices[0].GetProperty("message");
                var text = msg.TryGetProperty("content", out var c) ? c.GetString()?.Trim() : null;
                if (string.IsNullOrEmpty(text) && msg.TryGetProperty("reasoning", out var r))
                    text = r.GetString()?.Trim();
                return string.IsNullOrEmpty(text) ? (false, null, "empty") : (true, text, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Groq call failed model={Model}", GroqModel);
                return (false, null, ex.Message);
            }
        }

        // ===================== TIỆN ÍCH TRẢ LỜI =====================

        private static string CauTraLoiKhongBiet() =>
            "Dạ, mình xin lỗi — câu này mình chưa hiểu đủ để trả lời chính xác nên không dám đoán bừa. "
            + "Bạn nói rõ hơn giúp mình nhé: muốn xem hồ sơ nào, tháng nào, hay trạng thái nào?";

        private static bool LaKhongBiet(string s) =>
            string.IsNullOrWhiteSpace(s)
            || s.Replace(" ", "").Contains("KHONG_BIET", StringComparison.OrdinalIgnoreCase)
            || s.Trim().Equals("KHONG_BIET", StringComparison.OrdinalIgnoreCase);

        private static string LamSachTraLoi(string s)
        {
            var t = s.Trim();
            if (t.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNl = t.IndexOf('\n');
                if (firstNl > 0) t = t[(firstNl + 1)..];
                if (t.EndsWith("```", StringComparison.Ordinal)) t = t[..^3];
            }
            return t.Trim();
        }

        private static bool LaGap(string q) =>
            Co(q, "gap", "gap lam", "can gap", "nhanh len", "nhanh giup", "cham qua", "lau qua",
                "doi qua", "sot ruot", "kho chiu", "buc minh", "ngay lap tuc", "can ngay", "lien");

        /// <summary>Thêm đúng một câu trấn an ngắn khi người dùng đang gấp.</summary>
        private static string ThemGiongDieu(string traLoi, string q, int? soLuong)
        {
            if (!LaGap(q)) return traLoi;
            var body = traLoi.TrimStart();
            if (body.StartsWith("Dạ,", StringComparison.OrdinalIgnoreCase))
                body = body[3..].TrimStart();
            else if (body.StartsWith("Dạ ", StringComparison.OrdinalIgnoreCase))
                body = body[3..].TrimStart();
            if (body.Length > 0)
                body = char.ToUpper(body[0]) + body[1..];
            return "Dạ, mình tra ngay đây — bạn đừng sốt ruột nhé.\n" + body;
        }

        private static bool LaChaoHoi(string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length > 40) return false;
            return Regex.IsMatch(q, @"^(chao|xin chao|hello|hi|alo)\b");
        }

        private static bool LaCamOn(string q) =>
            q.Length <= 40 && Co(q, "cam on", "thank", "thanks", "tks", "ok ban", "duoc roi");

        /// <summary>Chặn LLM lặp lại y nguyên đáp án của lượt trước khi câu hỏi đã khác.</summary>
        private static bool LaLapLaiDapAnCu(
            string moi, string cauHienTai, IReadOnlyList<(string role, string content)>? lichSu)
        {
            if (lichSu == null || lichSu.Count == 0) return false;
            var dapAnCu = lichSu
                .Where(x => string.Equals(x.role, "assistant", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.content?.Trim())
                .LastOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (string.IsNullOrWhiteSpace(dapAnCu)) return false;

            var cauTruoc = lichSu
                .Where(x => string.Equals(x.role, "user", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.content?.Trim())
                .LastOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(cauTruoc)
                && DoGiongNhau(ChuanHoa(cauTruoc), ChuanHoa(cauHienTai), 0.72))
                return false;

            return DoGiongNhau(ChuanHoa(moi), ChuanHoa(dapAnCu!), 0.82);
        }

        private static bool DoGiongNhau(string a, string b, double nguong)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            var ta = Tokens(a);
            var tb = Tokens(b);
            if (ta.Count == 0 || tb.Count == 0) return false;
            var giao = ta.Intersect(tb, StringComparer.OrdinalIgnoreCase).Count();
            var hop = ta.Union(tb, StringComparer.OrdinalIgnoreCase).Count();
            return hop > 0 && (double)giao / hop >= nguong;
        }

        private static HashSet<string> Tokens(string q) =>
            q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 2)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
