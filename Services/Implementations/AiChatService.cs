using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    /// <summary>
    /// OPC Bot: Gemini + CONTEXT DB theo vai trò. Fallback rule nếu chưa có key / Gemini lỗi.
    /// Không dùng Ollama.
    /// </summary>
    public class AiChatService : IAiChatService
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
        }

        private string? GeminiKey => _config["Gemini:ApiKey"]?.Trim();
        private string GeminiModel =>
            string.IsNullOrWhiteSpace(_config["Gemini:Model"])
                ? "gemini-2.0-flash"
                : _config["Gemini:Model"]!.Trim();

        public Task<(bool online, string? model, string? loi)> KiemTraAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(GeminiKey))
                return Task.FromResult<(bool, string?, string?)>((true, "OPC-RuleFallback",
                    "Chưa cấu hình Gemini:ApiKey — đang dùng fallback rule."));
            return Task.FromResult<(bool, string?, string?)>((true, GeminiModel, null));
        }

        public async Task<(bool ok, string? traLoi, string? loi)> ChatAsync(
            int maNguoiDung,
            string vaiTro,
            string hoTen,
            string cauHoi,
            IReadOnlyList<(string role, string content)>? lichSu,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cauHoi))
                return (false, null, "Gửi giúp một câu hỏi nhé.");

            var ten = string.IsNullOrWhiteSpace(hoTen) ? "bạn" : hoTen.Trim();
            var vt = (vaiTro ?? "Người dùng").Trim();
            var q = ChuanHoa(cauHoi);

            if (LaNhayCam(q))
                return (true, "Dạ, mình không chia sẻ thông tin bảo mật hệ thống.", null);

            var tuChoi = TuChoiVuotVaiTro(vt, q);
            if (tuChoi != null)
                return (true, tuChoi, null);

            try
            {
                var context = await NapContextVaiTroAsync(maNguoiDung, vt, ct);

                if (!string.IsNullOrEmpty(GeminiKey))
                {
                    var (ok, text, err) = await GoiGeminiAsync(vt, ten, cauHoi.Trim(), context, lichSu, ct);
                    if (ok && !string.IsNullOrWhiteSpace(text))
                        return (true, text.Trim(), null);
                    _logger.LogWarning("Gemini lỗi, fallback: {Err}", err);
                }

                return (true, await FallbackRuleAsync(vt, q, ct), null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chatbot lỗi");
                return (false, null, "Lỗi khi xử lý câu hỏi. Thử lại giúp mình.");
            }
        }

        // ===================== CONTEXT =====================

        private async Task<string> NapContextVaiTroAsync(int maNd, string vt, CancellationToken ct)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Vai_tro: {vt}");
            sb.AppendLine($"Thoi_diem: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            var tbs = await _db.ThietBis.AsNoTracking()
                .Select(t => new
                {
                    t.MaThietBi,
                    t.TenThietBi,
                    t.LoaiThietBi,
                    t.ViTriLapDat,
                    t.TinhTrangHienTai,
                    t.NgayBaoTriGanNhat,
                    t.NgayBaoTriTiepTheo
                }).ToListAsync(ct);

            sb.AppendLine($"Tong_thiet_bi: {tbs.Count}");
            foreach (var g in tbs.GroupBy(x => x.TinhTrangHienTai ?? "(trống)"))
                sb.AppendLine($"  Tinh_trang[{g.Key}]={g.Count()}");
            foreach (var g in tbs.GroupBy(x => x.LoaiThietBi ?? "(chưa loại)"))
                sb.AppendLine($"  Danh_muc[{g.Key}]={g.Count()}");
            sb.AppendLine("DS_thiet_bi:");
            foreach (var t in tbs.Take(80))
            {
                sb.AppendLine(
                    $"  TB#{t.MaThietBi}|{t.TenThietBi}|loai={t.LoaiThietBi}|vitri={t.ViTriLapDat}"
                    + $"|tt={t.TinhTrangHienTai}"
                    + $"|bt_gan={(t.NgayBaoTriGanNhat.HasValue ? t.NgayBaoTriGanNhat.Value.ToString("dd/MM/yyyy") : "-")}"
                    + $"|bt_tiep={(t.NgayBaoTriTiepTheo.HasValue ? t.NgayBaoTriTiepTheo.Value.ToString("dd/MM/yyyy") : "-")}");
            }

            if (vt is "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" or "Xưởng" or "Tổ trưởng sản xuất" or "Giám đốc")
            {
                var bts = await _db.HoSoBaoTris.AsNoTracking()
                    .Select(h => new
                    {
                        h.MaHoSoBaoTri,
                        h.TrangThai,
                        NgayHt = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao,
                        h.NgayTao,
                        TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                    }).ToListAsync(ct);

                var mapKh = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                    .Where(c => c.MaHoSoBaoTri != null)
                    .Select(c => new { Ma = c.MaHoSoBaoTri!.Value, c.NgayDuKienBaoTri })
                    .ToListAsync(ct);
                var dictKh = mapKh.GroupBy(x => x.Ma).ToDictionary(g => g.Key, g => g.First().NgayDuKienBaoTri);

                sb.AppendLine($"Tong_HS_BT: {bts.Count}");
                foreach (var g in bts.GroupBy(x => x.TrangThai ?? "(trống)"))
                    sb.AppendLine($"  BT_TT[{g.Key}]={g.Count()}");
                sb.AppendLine("HS_BT:");
                foreach (var h in bts.OrderByDescending(x => x.NgayHt).Take(60))
                {
                    var ky = dictKh.TryGetValue(h.MaHoSoBaoTri, out var nk)
                        ? nk.ToString("dd/MM/yyyy")
                        : h.NgayHt.ToString("dd/MM/yyyy");
                    sb.AppendLine(
                        $"  BT#{h.MaHoSoBaoTri}|{h.TenTb}|tt={h.TrangThai}|ngay_kh_hoac_ht={ky}|ht={h.NgayHt:dd/MM/yyyy HH:mm}");
                }

                var scs = await _db.HoSoSuaChuas.AsNoTracking()
                    .Select(h => new
                    {
                        h.MaHoSoSuaChua,
                        h.TrangThai,
                        NgayHt = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao,
                        TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                    }).ToListAsync(ct);
                sb.AppendLine($"Tong_HS_SC: {scs.Count}");
                foreach (var g in scs.GroupBy(x => x.TrangThai ?? "(trống)"))
                    sb.AppendLine($"  SC_TT[{g.Key}]={g.Count()}");
                sb.AppendLine("HS_SC:");
                foreach (var h in scs.OrderByDescending(x => x.NgayHt).Take(40))
                    sb.AppendLine($"  SC#{h.MaHoSoSuaChua}|{h.TenTb}|tt={h.TrangThai}|ht={h.NgayHt:dd/MM/yyyy HH:mm}");

                var vts = await _db.HoSoSuDungVatTus.AsNoTracking()
                    .OrderByDescending(h => h.NgayThucHien).Take(40)
                    .Select(h => new
                    {
                        h.MaHoSoBaoTri,
                        h.MaHoSoSuaChua,
                        TenTb = h.TenThietBi ?? "",
                        h.TongTien,
                        h.TrangThai,
                        h.NgayThucHien,
                        SoLoai = h.ChiTietSuDungVatTus.Select(c => c.TenVatTu).Distinct().Count(),
                        TongSl = h.ChiTietSuDungVatTus.Sum(c => c.SoLuong)
                    }).ToListAsync(ct);
                sb.AppendLine("HS_vat_tu:");
                foreach (var v in vts)
                {
                    var hs = v.MaHoSoBaoTri != null ? $"BT#{v.MaHoSoBaoTri}" : $"SC#{v.MaHoSoSuaChua}";
                    sb.AppendLine($"  {hs}|{v.TenTb}|sl={v.TongSl}|loai={v.SoLoai}|tien={v.TongTien:0}|tt={v.TrangThai}|ngay={v.NgayThucHien:dd/MM/yyyy}");
                }

                var lich = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                    .Where(c => c.NgayDuKienBaoTri >= DateOnly.FromDateTime(DateTime.Now.AddMonths(-2)))
                    .OrderBy(c => c.NgayDuKienBaoTri).Take(40)
                    .Select(c => new
                    {
                        c.NgayDuKienBaoTri,
                        c.MaHoSoBaoTri,
                        Ten = c.MaThietBiNavigation != null ? c.MaThietBiNavigation.TenThietBi : ("TB#" + c.MaThietBi)
                    }).ToListAsync(ct);
                sb.AppendLine("Lich_BT:");
                foreach (var c in lich)
                    sb.AppendLine($"  {c.NgayDuKienBaoTri:dd/MM/yyyy}|{c.Ten}|hs={(c.MaHoSoBaoTri.HasValue ? "BT#" + c.MaHoSoBaoTri : "-")}");

                var buoc = await _db.TienDoBuocQuyTrinhs.AsNoTracking()
                    .OrderByDescending(t => t.ThoiDiemCapNhat).Take(120)
                    .Select(t => new { t.MaHoSoBaoTri, t.MaHoSoSuaChua, t.SoBuoc, t.MoTaBuoc, t.TrangThai })
                    .ToListAsync(ct);
                foreach (var g in buoc.GroupBy(t => t.MaHoSoBaoTri != null ? $"BT#{t.MaHoSoBaoTri}" : $"SC#{t.MaHoSoSuaChua}").Take(25))
                {
                    var uniq = g.GroupBy(x => x.SoBuoc).Select(x => x.First()).OrderBy(x => x.SoBuoc);
                    sb.AppendLine($"Buoc[{g.Key}]: {string.Join("; ", uniq.Select(x => $"B{x.SoBuoc}:{x.MoTaBuoc}({x.TrangThai})"))}");
                }
            }

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
                        .OrderByDescending(p => p.NgayPhanCong).Take(30)
                        .Select(p => new { p.MaPhanCong, p.MaHoSoBaoTri, p.MaHoSoSuaChua, p.TrangThai, p.NgayPhanCong })
                        .ToListAsync(ct);
                    sb.AppendLine($"Phan_cong: {pc.Count}");
                    foreach (var p in pc)
                    {
                        var hs = p.MaHoSoBaoTri != null ? $"BT#{p.MaHoSoBaoTri}" : $"SC#{p.MaHoSoSuaChua}";
                        sb.AppendLine($"  PC#{p.MaPhanCong}|{hs}|tt={p.TrangThai}|ngay={p.NgayPhanCong:dd/MM/yyyy}");
                    }

                    var maBt = pc.Where(x => x.MaHoSoBaoTri != null).Select(x => x.MaHoSoBaoTri!.Value).Distinct().ToList();
                    var maSc = pc.Where(x => x.MaHoSoSuaChua != null).Select(x => x.MaHoSoSuaChua!.Value).Distinct().ToList();

                    if (maBt.Count > 0)
                    {
                        var myBt = await _db.HoSoBaoTris.AsNoTracking()
                            .Where(h => maBt.Contains(h.MaHoSoBaoTri))
                            .Select(h => new
                            {
                                h.MaHoSoBaoTri,
                                h.TrangThai,
                                TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "",
                                h.ThoiDiemBatDauThucTe,
                                h.ThoiDiemKetThucThucTe
                            }).ToListAsync(ct);
                        sb.AppendLine("HS_BT_cua_toi:");
                        foreach (var h in myBt)
                            sb.AppendLine($"  BT#{h.MaHoSoBaoTri}|{h.TenTb}|tt={h.TrangThai}|bd={h.ThoiDiemBatDauThucTe}|ht={h.ThoiDiemKetThucThucTe}");
                    }
                    if (maSc.Count > 0)
                    {
                        var mySc = await _db.HoSoSuaChuas.AsNoTracking()
                            .Where(h => maSc.Contains(h.MaHoSoSuaChua))
                            .Select(h => new
                            {
                                h.MaHoSoSuaChua,
                                h.TrangThai,
                                TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                            }).ToListAsync(ct);
                        sb.AppendLine("HS_SC_cua_toi:");
                        foreach (var h in mySc)
                            sb.AppendLine($"  SC#{h.MaHoSoSuaChua}|{h.TenTb}|tt={h.TrangThai}");
                    }

                    var myVt = await _db.HoSoSuDungVatTus.AsNoTracking()
                        .Where(h => h.MaNhanVienTH == maNv)
                        .OrderByDescending(h => h.NgayThucHien).Take(20)
                        .Select(h => new
                        {
                            h.MaHoSoBaoTri,
                            h.MaHoSoSuaChua,
                            h.TongTien,
                            TongSl = h.ChiTietSuDungVatTus.Sum(c => c.SoLuong),
                            SoLoai = h.ChiTietSuDungVatTus.Select(c => c.TenVatTu).Distinct().Count()
                        }).ToListAsync(ct);
                    sb.AppendLine("Vat_tu_cua_toi:");
                    foreach (var v in myVt)
                    {
                        var hs = v.MaHoSoBaoTri != null ? $"BT#{v.MaHoSoBaoTri}" : $"SC#{v.MaHoSoSuaChua}";
                        sb.AppendLine($"  {hs}|sl={v.TongSl}|loai={v.SoLoai}|tien={v.TongTien:0}");
                    }

                    var buoc = await _db.TienDoBuocQuyTrinhs.AsNoTracking()
                        .Where(t =>
                            (t.MaHoSoBaoTri != null && maBt.Contains(t.MaHoSoBaoTri.Value)) ||
                            (t.MaHoSoSuaChua != null && maSc.Contains(t.MaHoSoSuaChua.Value)))
                        .OrderByDescending(t => t.ThoiDiemCapNhat).Take(100)
                        .Select(t => new { t.MaHoSoBaoTri, t.MaHoSoSuaChua, t.SoBuoc, t.MoTaBuoc, t.TrangThai })
                        .ToListAsync(ct);
                    foreach (var g in buoc.GroupBy(t => t.MaHoSoBaoTri != null ? $"BT#{t.MaHoSoBaoTri}" : $"SC#{t.MaHoSoSuaChua}").Take(15))
                    {
                        var uniq = g.GroupBy(x => x.SoBuoc).Select(x => x.First()).OrderBy(x => x.SoBuoc);
                        sb.AppendLine($"Buoc[{g.Key}]: {string.Join("; ", uniq.Select(x => $"B{x.SoBuoc}:{x.MoTaBuoc}({x.TrangThai})"))}");
                    }
                }
            }

            var text = sb.ToString();
            if (text.Length > 28000)
                text = text[..28000] + "\n...(truncated)";
            return text;
        }

        // ===================== GEMINI =====================

        private async Task<(bool ok, string? text, string? err)> GoiGeminiAsync(
            string vt,
            string ten,
            string cauHoi,
            string context,
            IReadOnlyList<(string role, string content)>? lichSu,
            CancellationToken ct)
        {
            var phamVi = MoTaPhamVi(vt);
            var system =
                "Bạn là OPC Bot — trợ lý dữ liệu app bảo trì-sửa chữa cơ điện (Dược OPC).\n" +
                $"NGƯỜI DÙNG: {ten}\n" +
                $"VAI TRÒ: «{vt}»\n" +
                "PHẠM VI ĐƯỢC TRẢ LỜI:\n" + phamVi + "\n\n" +
                "CẤM: trả lời hộ vai trò khác; bịa số liệu; lan man; lộ mật khẩu/code/API key.\n" +
                "BẮT BUỘC: phân tích kỹ câu hỏi (ngày/tháng/năm/trạng thái/BT|SC/tên TB); chỉ dùng CONTEXT; " +
                "trả lời đúng trọng tâm bắt đầu bằng \"Dạ,\"; thiếu dữ liệu thì nói không thấy — không đoán. Tiếng Việt.";

            var user = new StringBuilder();
            user.AppendLine("CONTEXT:");
            user.AppendLine(context);
            if (lichSu != null && lichSu.Count > 0)
            {
                user.AppendLine("LICH_SU:");
                foreach (var (role, content) in lichSu.TakeLast(6))
                    user.AppendLine($"- {role}: {content}");
            }
            user.AppendLine("CAU_HOI:");
            user.AppendLine(cauHoi);

            var body = new
            {
                system_instruction = new { parts = new[] { new { text = system } } },
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = user.ToString() } } }
                },
                generationConfig = new { temperature = 0.1, maxOutputTokens = 1024 }
            };

            var url =
                $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(GeminiModel)}:generateContent?key={Uri.EscapeDataString(GeminiKey!)}";

            try
            {
                var client = _httpFactory.CreateClient("Gemini");
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
                };
                using var res = await client.SendAsync(req, ct);
                var raw = await res.Content.ReadAsStringAsync(ct);
                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Gemini HTTP {Code}: {Body}", (int)res.StatusCode,
                        raw.Length > 400 ? raw[..400] : raw);
                    return (false, null, $"Gemini {(int)res.StatusCode}");
                }

                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0)
                    return (false, null, "no candidates");

                var parts = cands[0].GetProperty("content").GetProperty("parts");
                var outSb = new StringBuilder();
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var tx))
                        outSb.Append(tx.GetString());
                }
                var text = outSb.ToString().Trim();
                return string.IsNullOrEmpty(text) ? (false, null, "empty") : (true, text, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gemini call failed");
                return (false, null, ex.Message);
            }
        }

        // ===================== FALLBACK =====================

        private async Task<string> FallbackRuleAsync(string vt, string q, CancellationToken ct)
        {
            int? ngay = null, thang = null, nam = null;
            TrichNgayThangNam(q, ref ngay, ref thang, ref nam);

            if (Co(q, "thiet bi") && Co(q, "bao nhieu", "tong", "danh muc", "san xuat", "bao tri", "sua chua"))
            {
                var tbs = await _db.ThietBis.AsNoTracking().Select(t => t.TinhTrangHienTai).ToListAsync(ct);
                int Dem(string k) => tbs.Count(x => (x ?? "").Contains(k, StringComparison.OrdinalIgnoreCase));
                return $"Dạ, tổng **{tbs.Count}** thiết bị. Sản xuất: **{Dem("Sản xuất")}**, Bảo trì: **{Dem("Bảo trì")}**, Sửa chữa: **{Dem("Sửa chữa")}**.";
            }

            if (vt != "Nhân viên kỹ thuật" && Co(q, "ho so", "bao tri", "hoan thanh", "thang", "ngay"))
            {
                var canHt = Co(q, "hoan thanh");
                var raw = await _db.HoSoBaoTris.AsNoTracking()
                    .Select(h => new
                    {
                        h.MaHoSoBaoTri,
                        h.TrangThai,
                        NgayHt = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao,
                        TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                    }).ToListAsync(ct);
                var mapKh = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                    .Where(c => c.MaHoSoBaoTri != null)
                    .Select(c => new { Ma = c.MaHoSoBaoTri!.Value, c.NgayDuKienBaoTri })
                    .ToListAsync(ct);
                var dictKh = mapKh.GroupBy(x => x.Ma).ToDictionary(g => g.Key, g => g.First().NgayDuKienBaoTri);
                var y = nam ?? DateTime.Now.Year;

                var list = raw.Select(h =>
                {
                    int d, m, n;
                    if (dictKh.TryGetValue(h.MaHoSoBaoTri, out var nk))
                    { d = nk.Day; m = nk.Month; n = nk.Year; }
                    else
                    { d = h.NgayHt.Day; m = h.NgayHt.Month; n = h.NgayHt.Year; }
                    return new { h.MaHoSoBaoTri, h.TrangThai, h.TenTb, h.NgayHt, D = d, M = m, N = n };
                }).Where(x => x.N == y
                              && (thang == null || x.M == thang)
                              && (ngay == null || x.D == ngay)
                              && (!canHt || x.TrangThai == "Đã hoàn thành"))
                    .OrderByDescending(x => x.NgayHt).ToList();

                var ky = ngay != null && thang != null ? $"ngày {ngay:00}/{thang:00}/{y}"
                    : thang != null ? $"tháng {thang}/{y}"
                    : nam != null ? $"năm {y}" : "toàn hệ thống";
                var sb = new StringBuilder();
                sb.AppendLine($"Dạ, {ky} có **{list.Count}** hồ sơ bảo trì{(canHt ? " **Đã hoàn thành**" : "")}.");
                foreach (var h in list.Take(10))
                    sb.AppendLine($"• BT#{h.MaHoSoBaoTri} | {h.TenTb} | {h.TrangThai} | {h.NgayHt:dd/MM/yyyy}");
                return sb.ToString().TrimEnd();
            }

            return "Dạ, hiện đang dùng fallback rule (Gemini chưa cấu hình hoặc lỗi). "
                   + "Kiểm tra Gemini:ApiKey trong appsettings và restart API. "
                   + "Hỏi mẫu: «tháng 12/2026 có bao nhiêu hồ sơ bảo trì đã hoàn thành?»";
        }

        // ===================== ROLE + HELPERS =====================

        private static string MoTaPhamVi(string vt) => vt switch
        {
            "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" =>
                "- Thiết bị, lịch BT, KH/HS bảo trì & sửa chữa, phân công NVKT, vật tư/bước liên quan.\n"
                + "- KHÔNG duyệt hộ GĐ; KHÔNG chốt ngày hộ Xưởng.",
            "Xưởng" or "Tổ trưởng sản xuất" =>
                "- Chốt ngày BT, HS chờ xưởng, gửi GĐ, xác nhận/từ chối quy trình NVKT.\n"
                + "- KHÔNG phân công NVKT thay Tổ trưởng; KHÔNG duyệt cuối như GĐ.",
            "Giám đốc" =>
                "- Duyệt/từ chối HS BT-SC, thống kê, HS chờ duyệt.\n"
                + "- KHÔNG phân công NVKT; KHÔNG chốt ngày Xưởng.",
            "Nhân viên kỹ thuật" =>
                "- Chỉ việc/HS/vật tư/bước được phân công cho TÀI KHOẢN này.\n"
                + "- KHÔNG dữ liệu toàn công ty; KHÔNG duyệt; KHÔNG chốt ngày.",
            _ => "- Chỉ thông tin gắn tài khoản đang đăng nhập."
        };

        private static string? TuChoiVuotVaiTro(string vt, string q)
        {
            if (vt == "Nhân viên kỹ thuật")
            {
                if (Co(q, "toan bo", "tat ca nhan vien", "toan cong ty"))
                    return "Dạ, NVKT chỉ xem công việc được phân công trên tài khoản của bạn.";
                if (Co(q, "duyet ho so") && Co(q, "giam doc", "ho so"))
                    return "Dạ, NVKT không phê duyệt hồ sơ (thuộc Giám đốc).";
                if (Co(q, "chot ngay"))
                    return "Dạ, chốt ngày bảo trì thuộc Xưởng.";
            }
            if (vt is "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật")
            {
                if (Co(q, "thay giam doc") || (Co(q, "toi duyet", "duyet ho") && Co(q, "giam doc")))
                    return "Dạ, Tổ trưởng không duyệt hộ Giám đốc.";
                if (Co(q, "chot ngay ho", "chot ngay thay") && Co(q, "xuong"))
                    return "Dạ, chốt ngày thuộc Xưởng.";
            }
            if (vt is "Xưởng" or "Tổ trưởng sản xuất")
            {
                if (Co(q, "phan cong") && Co(q, "thay to truong", "nhan vien"))
                    return "Dạ, phân công NVKT thuộc Tổ trưởng cơ điện.";
            }
            if (vt == "Giám đốc")
            {
                if (Co(q, "phan cong nhan vien") && Co(q, "thay to truong"))
                    return "Dạ, phân công thuộc Tổ trưởng cơ điện.";
                if (Co(q, "chot ngay") && Co(q, "thay xuong"))
                    return "Dạ, chốt ngày thuộc Xưởng.";
            }
            return null;
        }

        private static void TrichNgayThangNam(string q, ref int? ngay, ref int? thang, ref int? nam)
        {
            var m = Regex.Match(q, @"\b(\d{1,2})\s*[/\-]\s*(\d{1,2})\s*[/\-]\s*(20\d{2})\b");
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out var d1)
                && int.TryParse(m.Groups[2].Value, out var mo1)
                && int.TryParse(m.Groups[3].Value, out var y1)
                && d1 is >= 1 and <= 31 && mo1 is >= 1 and <= 12)
            {
                ngay = d1; thang = mo1; nam = y1;
                return;
            }
            m = Regex.Match(q, @"\b(\d{1,2})\s*[/\-]\s*(20\d{2})\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var mo2)
                && int.TryParse(m.Groups[2].Value, out var y2) && mo2 is >= 1 and <= 12)
            { thang = mo2; nam = y2; }

            m = Regex.Match(q, @"ngay\s*(\d{1,2})\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var d3) && d3 is >= 1 and <= 31)
                ngay = d3;
            m = Regex.Match(q, @"thang\s*(\d{1,2})\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var mo4) && mo4 is >= 1 and <= 12)
                thang = mo4;
            m = Regex.Match(q, @"(?:nam\s*)?(20\d{2})\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var y5))
                nam = y5;

            if (ngay != null && thang == null) thang = DateTime.Now.Month;
            if ((ngay != null || thang != null) && nam == null) nam = DateTime.Now.Year;
        }

        private static string ChuanHoa(string input)
        {
            var s = input.Trim().ToLowerInvariant();
            s = Regex.Replace(s, @"\s+", " ");
            var norm = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in norm)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC).Replace('đ', 'd');
        }

        private static bool Co(string q, params string[] keys) => keys.Any(k => q.Contains(k));

        private static bool LaNhayCam(string q)
        {
            string[] cam =
            {
                "mat khau", "password", "connection string", "jwt", "secret",
                "api key", "source code", "ma nguon", "appsettings"
            };
            return cam.Any(k => q.Contains(k));
        }
    }
}
