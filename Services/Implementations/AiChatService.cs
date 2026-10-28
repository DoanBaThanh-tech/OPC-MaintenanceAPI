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

            // Log truy vết: mỗi câu trả lời đi qua NHÁNH nào (để biết vì sao bot trả lời nẻo / lặp).
            (bool, string?, string?) Tra(string nhanh, string text)
            {
                _logger.LogInformation("OPCBot vt={Vt} nhanh={Nhanh} q={Q}",
                    vt, nhanh, q.Length > 80 ? q[..80] : q);
                return (true, text, null);
            }

            if (LaNhayCam(q))
                return Tra("nhay_cam", "Dạ, mình không chia sẻ thông tin bảo mật của app.");

            try
            {
                // 1) Người dùng bấm «Báo sai» → bỏ đáp án cũ, đối chiếu lại từ dữ liệu app.
                if (baoSai || LaBaoCauTraLoiSai(q, cauHienTai))
                    return Tra("bao_sai", await XuLyBaoSaiAsync(maNguoiDung, vt, ten, cauHoiGoc, lichSu, ct));

                // 2) Người dùng chỉnh lại cách hiểu / con số → đối chiếu DB rồi mới ghi nhớ.
                var hoc = await XuLyDayTuUserAsync(maNguoiDung, vt, ten, q, lichSu, ct);
                if (!string.IsNullOrWhiteSpace(hoc))
                    return Tra("hoc_tu_user", hoc);

                // 3) Ranh giới vai trò (chỉ chặn thao tác hộ vai trò khác).
                var chan = TuChoiVuotVaiTro(vt, q);
                if (!string.IsNullOrWhiteSpace(chan))
                    return Tra("chan_vai_tro", chan);

                if (LaChaoHoi(q))
                    return Tra("chao", MauXoayVong("chao",
                        $"Dạ, chào {ten}! Mình là OPC Bot. Bạn muốn xem hồ sơ, thiết bị, phân công hay tiến độ công việc?",
                        $"Dạ, chào {ten}! Hôm nay bạn cần mình tra gì — hồ sơ, thiết bị hay phân công?",
                        $"Dạ, mình đây {ten}. Bạn cần xem gì cứ nói, mình tra liền."));
                if (LaCamOn(q))
                    return Tra("cam_on", MauXoayVong("cam_on",
                        "Dạ, có gì bạn cứ hỏi mình nhé.",
                        "Dạ, không có chi! Cần gì thêm bạn cứ nhắn mình.",
                        "Dạ, vui là giúp được bạn. Cần tra gì nữa cứ hỏi mình nhé."));

                // 4) Định tuyến ý định + dữ liệu thật trong app.
                string? ruleTraLoi = null;
                string ruleChuDe = "";
                var kq = await XuLyYDinhAsync(maNguoiDung, vt, ten, q, cauHienTai, ct);
                if (kq.DaXuLy && !string.IsNullOrWhiteSpace(kq.TraLoi))
                {
                    // Chống lặp: câu hỏi đã khác mà đáp án y hệt lượt trước → thử LLM trước.
                    if (!LaLapLaiDapAnCu(kq.TraLoi!, cauHienTai, lichSu))
                        return Tra("rule:" + kq.ChuDe, ThemGiongDieu(kq.TraLoi!, q, kq.SoLuong));

                    ruleTraLoi = kq.TraLoi;
                    ruleChuDe = kq.ChuDe;
                    _logger.LogWarning("Rule '{ChuDe}' trả đáp án trùng lượt trước dù câu hỏi khác → thử LLM.", kq.ChuDe);
                }

                // 5) LLM nhận CONTEXT + lịch sử hội thoại thật (đáp án cũ chỉ ở dạng tóm tắt).
                if (!string.IsNullOrEmpty(GroqKey))
                {
                    var context = await NapContextVaiTroAsync(maNguoiDung, vt, ct);
                    var (ok, text, err) = await GoiGroqAsync(vt, ten, cauHienTai, context, lichSu, ct);
                    if (ok && !string.IsNullOrWhiteSpace(text))
                    {
                        var s = LamSachTraLoi(text!);
                        if (!LaKhongBiet(s) && !LaLapLaiDapAnCu(s, cauHienTai, lichSu))
                            return Tra("llm", s);
                        _logger.LogWarning("Groq trả lời không dùng được (không biết hoặc lặp đáp án cũ).");
                    }
                    else
                    {
                        _logger.LogWarning("Groq lỗi: {Err}", err);
                    }
                }

                // Rule đã tra đúng dữ liệu app nhưng trùng lượt trước: vẫn trả (số liệu khớp app), nói rõ để không giống lặp máy móc.
                if (ruleTraLoi != null)
                    return Tra("rule_trung_luot_truoc:" + ruleChuDe, MauXoayVong("lap",
                            "Dạ, mình đối chiếu lại thì kết quả vẫn như lượt trước:\n",
                            "Dạ, mình tra lại một lần nữa, dữ liệu trên app vẫn là:\n",
                            "Dạ, mình kiểm tra lại rồi, kết quả không đổi:\n") + BoChuDa(ruleTraLoi));

                // 6) Câu hỏi quy trình → kiến thức nghiệp vụ; còn lại tự diễn đạt, không đoán.
                var kt = TraLoiKienThuc(vt, q);
                if (!string.IsNullOrWhiteSpace(kt))
                    return Tra("kien_thuc", kt);

                return Tra("khong_biet", await DienDatKhongBietAsync(vt, ten, cauHienTai, lichSu, ct));
            }
            catch (OperationCanceledException) { throw; }
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
            sb.AppendLine("Quy_uoc_ngay: mỗi hồ sơ có ngày tạo, ngày duyệt, bắt đầu, hoàn thành; "
                          + "riêng BT có thêm ngày kế hoạch. Hỏi «ngày cụ thể» là đối chiếu TẤT CẢ các mốc này, "
                          + "không tự đổi sang tháng và không bỏ qua hồ sơ chỉ vì mốc khác tháng.");
            sb.AppendLine("Quy_uoc_trang_thai: BT có Chờ duyệt/Chờ xưởng/Chờ gửi/Chờ GĐ duyệt, Đã duyệt, "
                          + "Chờ phân công, Đang thực hiện/Chờ xác nhận, Đã hoàn thành, Từ chối. "
                          + "SC tương tự; «Chờ xác nhận» của SC hiển thị là Đang thực hiện trên app.");

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
                    h.NgayTao,
                    h.NgayDuyet,
                    h.ThoiDiemBatDauThucTe,
                    h.ThoiDiemKetThucThucTe,
                    TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                }).ToListAsync(ct))
                .Where(h => !LaTrangThaiAnTrenApp(h.TrangThai)).ToList();
            sb.AppendLine($"Tong_HS_BT: {bts.Count}");
            foreach (var g in bts.GroupBy(x => x.TrangThai).OrderBy(g => g.Key))
                sb.AppendLine($"  BT_TT[{g.Key}]={g.Count()}");
            foreach (var h in bts.OrderByDescending(x => x.Ngay).Take(40))
                sb.AppendLine($"  BT#{h.MaHoSoBaoTri}|{h.TenTb}|tt={h.TrangThai}"
                              + $"|tao={h.NgayTao:dd/MM/yyyy}"
                              + $"|duyet={(h.NgayDuyet != null ? h.NgayDuyet.Value.ToString("dd/MM/yyyy") : "-")}"
                              + $"|bd={(h.ThoiDiemBatDauThucTe != null ? h.ThoiDiemBatDauThucTe.Value.ToString("dd/MM/yyyy") : "-")}"
                              + $"|kt={(h.ThoiDiemKetThucThucTe != null ? h.ThoiDiemKetThucThucTe.Value.ToString("dd/MM/yyyy") : "-")}");

            var scs = (await _db.HoSoSuaChuas.AsNoTracking()
                .Select(h => new
                {
                    h.MaHoSoSuaChua,
                    h.TrangThai,
                    Ngay = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao,
                    h.NgayTao,
                    h.NgayDuyet,
                    h.ThoiDiemBatDauThucTe,
                    h.ThoiDiemKetThucThucTe,
                    TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                }).ToListAsync(ct))
                .Where(h => !LaTrangThaiAnTrenApp(h.TrangThai)).ToList();
            sb.AppendLine($"Tong_HS_SC: {scs.Count}");
            foreach (var g in scs.GroupBy(x => x.TrangThai).OrderBy(g => g.Key))
                sb.AppendLine($"  SC_TT[{g.Key}]={g.Count()}");
            foreach (var h in scs.OrderByDescending(x => x.Ngay).Take(30))
                sb.AppendLine($"  SC#{h.MaHoSoSuaChua}|{h.TenTb}|tt={h.TrangThai}"
                              + $"|tao={h.NgayTao:dd/MM/yyyy}"
                              + $"|duyet={(h.NgayDuyet != null ? h.NgayDuyet.Value.ToString("dd/MM/yyyy") : "-")}"
                              + $"|bd={(h.ThoiDiemBatDauThucTe != null ? h.ThoiDiemBatDauThucTe.Value.ToString("dd/MM/yyyy") : "-")}"
                              + $"|kt={(h.ThoiDiemKetThucThucTe != null ? h.ThoiDiemKetThucThucTe.Value.ToString("dd/MM/yyyy") : "-")}");

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
                "10) Xưng «mình», gọi người dùng «bạn». Có thể dùng **đậm** và gạch đầu dòng •, không dùng bảng.\n" +
                "11) Hỏi «có hồ sơ nào ở trạng thái X» → đếm đúng hồ sơ đang ở trạng thái X trong CONTEXT, không đổi sang lịch sử duyệt.\n" +
                "12) Hỏi «ngày cụ thể» → đối chiếu ngày tạo, ngày duyệt, bắt đầu, hoàn thành và ngày kế hoạch; nêu rõ mốc khớp, không tự quy về tháng.\n" +
                "13) Bạn nhận LỊCH SỬ hội thoại thật (câu hỏi người dùng + «tóm tắt câu trả lời trước»). Dùng để hiểu câu nối tiếp («còn tháng trước?», «cái đó»), " +
                "nhưng KHÔNG chép lại câu trả lời cũ; mỗi câu trả lời viết mới theo câu hỏi hiện tại.\n" +
                "14) Người dùng đã sửa/dạy trong GHI_NHO_NGUOI_DUNG hoặc trong lịch sử → áp dụng ngay, không lặp lại lỗi cũ.\n\n" +
                "KIẾN THỨC NGHIỆP VỤ:\n" + XayDungKienThucHeThong(vt) + "\n" +
                "PHẠM VI VAI TRÒ:\n" + MoTaPhamVi(vt) +
                (suaSai ? "\n[CHE_DO_SUA_SAI: câu trả lời trước sai — cấm lặp lại số liệu cũ]" : "");

            var messages = new List<object> { new { role = "system", content = system } };

            // Gửi lượt hội thoại thật (tối đa 6): câu hỏi giữ nguyên, đáp án cũ chỉ ở dạng tóm tắt.
            foreach (var (r, c) in (lichSu ?? Array.Empty<(string role, string content)>()).TakeLast(6))
            {
                var nd = (c ?? "").Trim();
                if (nd.Length == 0) continue;
                if (string.Equals(r, "assistant", StringComparison.OrdinalIgnoreCase))
                    messages.Add(new { role = "assistant", content = RutGonDapAn(nd) });
                else
                    messages.Add(new { role = "user", content = nd.Length > 300 ? nd[..300] : nd });
            }

            messages.Add(new
            {
                role = "user",
                content = "CONTEXT:\n" + context
                          + "\n\nCÂU HỎI HIỆN TẠI (chỉ trả lời câu này):\n" + cauHoi
                          + "\nNếu CONTEXT không đủ, trả đúng: KHONG_BIET"
            });

            return await PostGroqAsync(messages, suaSai ? 0.05 : 0.1, 800, ct);
        }

        // ===================== TIỆN ÍCH TRẢ LỜI =====================

        private static bool LaKhongBiet(string s) =>
            string.IsNullOrWhiteSpace(s)
            || s.Replace(" ", "").Contains("KHONG_BIET", StringComparison.OrdinalIgnoreCase)
            || s.Trim().Equals("KHONG_BIET", StringComparison.OrdinalIgnoreCase);

        private static string LamSachTraLoi(string s)
        {
            var t = BoSuyNghi(s);
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