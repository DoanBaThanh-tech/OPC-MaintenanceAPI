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
    /// OPC Bot: Groq API + CONTEXT DB theo vai trò. Số liệu cứng DB trước; fallback rule nếu chưa có key.
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

        private string? GroqKey => _config["Groq:ApiKey"]?.Trim();
        /// <summary>
        /// Model Groq hiện dùng. llama-3.1-8b-instant / llama-3.3-70b-versatile đã hết free-tier (404 model_not_found).
        /// Model ổn định free (Oct 2026): qwen/qwen3.8-27b, openai/gpt-oss-20b, openai/gpt-oss-120b.
        /// </summary>
        private string GroqModel =>
            string.IsNullOrWhiteSpace(_config["Groq:Model"])
                ? "qwen/qwen3.8-27b"
                : _config["Groq:Model"]!.Trim();

        public Task<(bool online, string? model, string? loi)> KiemTraAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(GroqKey))
                return Task.FromResult<(bool, string?, string?)>((true, "OPC-RuleFallback",
                    "Chưa cấu hình Groq:ApiKey — đang dùng fallback rule (số liệu DB vẫn chạy)."));
            return Task.FromResult<(bool, string?, string?)>((true, GroqModel, null));
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
                // 1) Số liệu định lượng: tính cứng từ DB trước (tháng/ngày/TT/TB...) — đúng như app
                var soLieu = await TraLoiSoLieuChinhXacAsync(maNguoiDung, vt, q, cauHoi.Trim(), ct);
                if (soLieu != null)
                    return (true, soLieu, null);

                // 2) Context + thống kê theo tháng → Groq (câu mở / nhiều ý)
                var context = await NapContextVaiTroAsync(maNguoiDung, vt, ct);
                context = await GanThongKeTheoThangAsync(context, vt, ct);

                if (!string.IsNullOrEmpty(GroqKey))
                {
                    var (ok, text, err) = await GoiGroqAsync(vt, ten, cauHoi.Trim(), context, lichSu, ct);
                    if (ok && !string.IsNullOrWhiteSpace(text))
                        return (true, text.Trim(), null);
                    _logger.LogWarning("Groq lỗi, fallback: {Err}", err);
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

                // --- Bổ sung cho Tổ trưởng: NVKT + phân công (hỗ trợ hỏi trên mọi trang role Tổ trưởng) ---
                if (vt is "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật")
                {
                    var nvs = await _db.NhanViens.AsNoTracking()
                        .Where(n => n.ChucVu != null
                                    && (n.ChucVu.Contains("kỹ thuật") || n.ChucVu.Contains("Kỹ thuật")
                                        || n.ChucVu.Contains("NVKT") || n.ChucVu.Contains("nhân viên")))
                        .Select(n => new { n.MaNhanVien, n.HoTen, n.ChucVu, n.TrangThai })
                        .ToListAsync(ct);
                    // Fallback: lấy NV không phải GĐ/Xưởng nếu filter quá hẹp
                    if (nvs.Count == 0)
                    {
                        nvs = await _db.NhanViens.AsNoTracking()
                            .OrderBy(n => n.MaNhanVien).Take(40)
                            .Select(n => new { n.MaNhanVien, n.HoTen, n.ChucVu, n.TrangThai })
                            .ToListAsync(ct);
                    }
                    sb.AppendLine($"DS_NVKT: {nvs.Count}");
                    foreach (var n in nvs)
                        sb.AppendLine($"  NV#{n.MaNhanVien}|{n.HoTen}|cv={n.ChucVu}|tt={n.TrangThai ?? "Hoạt động"}");

                    var pcs = await _db.PhanCongCongViecs.AsNoTracking()
                        .OrderByDescending(p => p.NgayPhanCong).Take(50)
                        .Select(p => new
                        {
                            p.MaPhanCong,
                            p.MaHoSoBaoTri,
                            p.MaHoSoSuaChua,
                            p.TrangThai,
                            p.NgayPhanCong,
                            p.LaNguoiGhiChep,
                            TenNv = p.MaNhanVienThucHienNavigation != null
                                ? p.MaNhanVienThucHienNavigation.HoTen : ("NV#" + p.MaNhanVienThucHien)
                        }).ToListAsync(ct);
                    sb.AppendLine($"Tong_phan_cong: {pcs.Count}");
                    foreach (var g in pcs.GroupBy(x => x.TrangThai ?? "(trống)"))
                        sb.AppendLine($"  PC_TT[{g.Key}]={g.Count()}");
                    sb.AppendLine("Phan_cong:");
                    foreach (var p in pcs.Take(40))
                    {
                        var hs = p.MaHoSoBaoTri != null ? $"BT#{p.MaHoSoBaoTri}" : $"SC#{p.MaHoSoSuaChua}";
                        sb.AppendLine(
                            $"  PC#{p.MaPhanCong}|{hs}|nv={p.TenNv}|tt={p.TrangThai}"
                            + $"|ghi_chep={(p.LaNguoiGhiChep ? "1" : "0")}|ngay={p.NgayPhanCong:dd/MM/yyyy}");
                    }

                    // HS chờ phân công (GĐ đã duyệt → Tổ trưởng mở khóa phân công)
                    var choPc = bts.Where(h =>
                        string.Equals(h.TrangThai, "Đã duyệt", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(h.TrangThai, "Chờ phân công", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(h.TrangThai, "Đã phê duyệt", StringComparison.OrdinalIgnoreCase)).ToList();
                    sb.AppendLine($"HS_BT_cho_phan_cong: {choPc.Count}");
                    foreach (var h in choPc.Take(20))
                        sb.AppendLine($"  BT#{h.MaHoSoBaoTri}|{h.TenTb}|tt={h.TrangThai}");
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

        private async Task<(bool ok, string? text, string? err)> GoiGroqAsync(
            string vt,
            string ten,
            string cauHoi,
            string context,
            IReadOnlyList<(string role, string content)>? lichSu,
            CancellationToken ct)
        {
            var phamVi = MoTaPhamVi(vt);
            var system =
                "Bạn là OPC Bot — trợ lý nội bộ app quản lý hồ sơ bảo trì/sửa chữa thiết bị cơ điện (Công ty Dược OPC).\n" +
                $"NGƯỜI DÙNG: {ten}\n" +
                $"VAI TRÒ HIỆN TẠI: «{vt}» — chỉ trả lời trong phạm vi công việc của vai trò này.\n" +
                "PHẠM VI:\n" + phamVi + "\n\n" +
                "QUY TẮC BẮT BUỘC:\n" +
                "1) Chỉ dùng dữ liệu trong CONTEXT (và LICH_SU nếu có). CẤM bịa số, mã hồ sơ, tên thiết bị, tên NV.\n" +
                "2) Phân tích đầy đủ câu hỏi tiếng Việt: tách từng ý (số lượng / liệt kê / trạng thái / tháng-ngày / nhân viên / so sánh).\n" +
                "3) Câu hỏi chi tiết hoặc nhiều ý → trả lời có cấu trúc: mở đầu bằng \"Dạ,\", nêu tổng số, rồi bullet từng mục (mã HS, thiết bị, trạng thái, ngày). Không lan man ngoài dữ liệu.\n" +
                "4) Câu hỏi đơn giản (1 con số) → trả lời ngắn 1–3 câu.\n" +
                "5) Hỏi theo tháng/năm: ưu tiên THONG_KE_BT_THEO_THANG và HS_BT (ngay_kh_hoac_ht / ht).\n" +
                "6) Hỏi phân công / NV rảnh-bận: dùng DS_NVKT, Phan_cong, HS_BT_cho_phan_cong.\n" +
                "7) Thiếu dữ liệu trong CONTEXT → nói rõ \"không thấy trong dữ liệu hiện có\", không đoán.\n" +
                "8) CẤM: trả lời hộ vai trò khác; lộ mật khẩu/API key/code; giải thích dài dòng không liên quan.\n" +
                "9) Luôn tiếng Việt, giọng trợ lý chuyên nghiệp, dễ đọc trên mobile.";

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

            // Groq OpenAI-compatible Chat Completions
            var body = new
            {
                model = GroqModel,
                temperature = 0.1,
                max_tokens = 1200,
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
                    // Log đầy đủ body lỗi (model_not_found, rate_limit, invalid key...)
                    _logger.LogWarning("Groq HTTP {Code} model={Model}: {Body}",
                        (int)res.StatusCode, GroqModel,
                        raw.Length > 600 ? raw[..600] : raw);
                    string? groqMsg = null;
                    try
                    {
                        using var errDoc = JsonDocument.Parse(raw);
                        if (errDoc.RootElement.TryGetProperty("error", out var errEl)
                            && errEl.TryGetProperty("message", out var msgEl))
                            groqMsg = msgEl.GetString();
                    }
                    catch { /* ignore parse */ }
                    return (false, null,
                        string.IsNullOrEmpty(groqMsg)
                            ? $"Groq {(int)res.StatusCode}"
                            : $"Groq {(int)res.StatusCode}: {groqMsg}");
                }

                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    return (false, null, "no choices");

                var msg = choices[0].GetProperty("message");
                // Ưu tiên content; một số model (gpt-oss) để câu trả lời trong reasoning
                var text = msg.TryGetProperty("content", out var c) ? c.GetString()?.Trim() : null;
                if (string.IsNullOrEmpty(text)
                    && msg.TryGetProperty("reasoning", out var r))
                    text = r.GetString()?.Trim();
                return string.IsNullOrEmpty(text) ? (false, null, "empty") : (true, text, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Groq call failed model={Model}", GroqModel);
                return (false, null, ex.Message);
            }
        }

        // ===================== SỐ LIỆU CHÍNH XÁC (DB) =====================

        /// <summary>
        /// Chỉ trả lời SỐ LIỆU khi ý định rõ ràng (tránh cướp câu hỏi → mẫu sai).
        /// Ưu tiên: vật tư → phân công → thiết bị → lịch BT → HS BT/SC.
        /// Câu mơ hồ / nhiều ý → trả null để Groq + CONTEXT phân tích.
        /// </summary>
        private async Task<string?> TraLoiSoLieuChinhXacAsync(
            int maNd, string vt, string q, string cauGoc, CancellationToken ct)
        {
            int? ngay = null, thang = null, nam = null;
            TrichNgayThangNam(q, ref ngay, ref thang, ref nam);
            var maBt = TrichMa(q, "bt");
            var maSc = TrichMa(q, "sc");

            // 1) VẬT TƯ — phải trước "hồ sơ" (tránh "hồ sơ vật tư" bị nhầm HS bảo trì)
            if (LaHoiVatTu(q))
            {
                string? ttVt = null;
                if (Co(q, "xac nhan", "da xac nhan")) ttVt = "Xác nhận";
                else if (Co(q, "cho gui")) ttVt = "Chờ gửi";
                else if (Co(q, "tu choi")) ttVt = "Từ chối";
                else if (Co(q, "cho xac nhan")) ttVt = "Chờ xác nhận";
                return await TraLoiVatTuAsync(maNd, vt, maBt, maSc, ttVt, ct);
            }

            // 2) PHÂN CÔNG — "lịch sử phân công" ≠ "lịch bảo trì"
            if (LaHoiPhanCong(q))
            {
                if (vt == "Nhân viên kỹ thuật")
                    return await TraLoiPhanCongNvktAsync(maNd, ct);
                if (vt is "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" or "Giám đốc" or "Xưởng" or "Tổ trưởng sản xuất")
                    return await TraLoiPhanCongToTruongAsync(ct);
            }

            // 3) THIẾT BỊ
            if (Co(q, "thiet bi") && !LaHoiVatTu(q) && !LaHoiPhanCong(q) && !Co(q, "ho so bao tri", "ho so sua chua"))
            {
                var tbs = await _db.ThietBis.AsNoTracking()
                    .Select(t => new
                    {
                        t.MaThietBi,
                        t.TenThietBi,
                        t.LoaiThietBi,
                        t.TinhTrangHienTai,
                        t.NgayBaoTriGanNhat,
                        t.NgayBaoTriTiepTheo
                    }).ToListAsync(ct);

                var tenGoi = TrichTenThietBi(q);
                if (tenGoi != null && (Co(q, "trang thai", "tinh trang", "dang o", "lich bao tri")))
                {
                    var hit = tbs
                        .Select(x => new { x, sc = DiemKhop(tenGoi, x.TenThietBi) })
                        .Where(x => x.sc >= 2)
                        .OrderByDescending(x => x.sc)
                        .Select(x => x.x)
                        .FirstOrDefault();
                    if (hit != null)
                    {
                        var tt = string.IsNullOrWhiteSpace(hit.TinhTrangHienTai) ? "chưa ghi" : hit.TinhTrangHienTai;
                        var gan = hit.NgayBaoTriGanNhat?.ToString("dd/MM/yyyy") ?? "chưa có";
                        var tiep = hit.NgayBaoTriTiepTheo?.ToString("dd/MM/yyyy") ?? "chưa có";
                        return $"Dạ, thiết bị **{hit.TenThietBi}** hiện đang ở trạng thái **{tt}**. "
                             + $"Lịch bảo trì gần nhất là **{gan}** và tiếp theo là **{tiep}** ạ.";
                    }
                }

                if (Co(q, "bao nhieu", "tong", "co may", "danh muc", "tinh trang") && !Co(q, "ho so"))
                {
                    int Dem(string k) => tbs.Count(x => (x.TinhTrangHienTai ?? "")
                        .Contains(k, StringComparison.OrdinalIgnoreCase));
                    var sb = new StringBuilder();
                    sb.AppendLine($"Dạ, tổng **{tbs.Count}** thiết bị (Tất cả).");
                    sb.AppendLine($"• Sản xuất: **{Dem("Sản xuất")}** | Bảo trì: **{Dem("Bảo trì")}** | Sửa chữa: **{Dem("Sửa chữa")}**.");
                    foreach (var g in tbs.GroupBy(x => x.LoaiThietBi ?? "(chưa loại)").OrderByDescending(g => g.Count()))
                        sb.AppendLine($"• {g.Key}: **{g.Count()}**");
                    return sb.ToString().TrimEnd();
                }
            }

            // 4) LỊCH BẢO TRÌ DỰ KIẾN (loại "lịch sử …")
            if (LaHoiLichBaoTri(q) && vt != "Nhân viên kỹ thuật")
            {
                var lich = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                    .Where(c => c.NgayDuKienBaoTri >= DateOnly.FromDateTime(DateTime.Today))
                    .OrderBy(c => c.NgayDuKienBaoTri).Take(15)
                    .Select(c => new
                    {
                        c.NgayDuKienBaoTri,
                        Ten = c.MaThietBiNavigation != null ? c.MaThietBiNavigation.TenThietBi : ("TB#" + c.MaThietBi)
                    }).ToListAsync(ct);
                if (lich.Count == 0)
                    return "Dạ, chưa có lịch bảo trì dự kiến sắp tới trên kế hoạch.";
                var sbL = new StringBuilder();
                sbL.AppendLine("Dạ, lịch bảo trì dự kiến sắp tới:");
                foreach (var c in lich)
                    sbL.AppendLine($"• {c.NgayDuKienBaoTri:dd/MM/yyyy}: {c.Ten}");
                return sbL.ToString().TrimEnd();
            }

            // 5) HỒ SƠ BẢO TRÌ / SỬA CHỮA — chỉ khi ý định rõ
            if (vt != "Nhân viên kỹ thuật" && LaHoiHoSoBtSc(q, ngay, thang))
            {
                var uuSc = Co(q, "sua chua") && !Co(q, "bao tri");
                if (Co(q, "bao tri") && Co(q, "sua chua") && Co(q, "hoan thanh", "bao nhieu", "co may"))
                {
                    var a = await DemHsAsync(true, ngay, thang, nam, "Đã hoàn thành", ct);
                    var b = await DemHsAsync(false, ngay, thang, nam, "Đã hoàn thành", ct);
                    return a + "\n\n" + b;
                }

                string? tt = null;
                if (Co(q, "hoan thanh")) tt = "Đã hoàn thành";
                else if (Co(q, "cho duyet", "cho gd")) tt = "Chờ duyệt";
                else if (Co(q, "dang thuc hien")) tt = "Đang thực hiện";
                else if (Co(q, "tu choi")) tt = "Từ chối";
                else if (Co(q, "cho xuong", "chot ngay")) tt = "Chờ xưởng";
                else if (Co(q, "cho phan cong", "da duyet")) tt = "Đã duyệt";

                return await DemHsAsync(!uuSc, ngay, thang, nam, tt, ct);
            }

            // 6) Bước quy trình
            if (Co(q, "buoc") && (maBt != null || maSc != null || Co(q, "quy trinh")))
                return await TraLoiBuocAsync(maNd, vt, maBt, maSc, ct);

            // Không khớp ý định rõ → null → Groq + CONTEXT
            return null;
        }

        private static bool LaHoiVatTu(string q) =>
            Co(q, "vat tu", "ho so vat tu", "hs vat tu", "nguyen vat lieu", "vat lieu")
            || (Co(q, "tong tien") && Co(q, "vat", "tien vat"));

        private static bool LaHoiPhanCong(string q) =>
            Co(q, "phan cong", "lich su phan cong", "ai duoc giao", "nv duoc giao", "nhan vien duoc giao")
            || (Co(q, "cong viec") && Co(q, "giao", "duoc giao", "cua toi", "cua minh"));

        private static bool LaHoiLichBaoTri(string q)
        {
            if (Co(q, "lich su")) return false;
            return Co(q, "lich bao tri", "lich du kien", "lich sap toi")
                   || (Co(q, "lich") && Co(q, "bao tri", "sap toi", "du kien"));
        }

        private static bool LaHoiHoSoBtSc(string q, int? ngay, int? thang)
        {
            if (LaHoiVatTu(q) || LaHoiPhanCong(q) || LaHoiLichBaoTri(q)) return false;
            if (Co(q, "ho so bao tri", "ho so sua chua", "hs bao tri", "hs sua chua", "ho so bt", "ho so sc"))
                return true;
            if (Co(q, "ho so") && Co(q, "bao tri", "sua chua", "hoan thanh", "cho duyet", "dang thuc hien", "cho xuong", "cho phan cong"))
                return true;
            if (Co(q, "bao tri", "sua chua") && Co(q, "bao nhieu", "co may", "tong", "hoan thanh", "cho duyet")
                && (thang != null || ngay != null || Co(q, "thang", "nam", "ngay")))
                return true;
            return false;
        }

        private async Task<string> DemHsAsync(
            bool laBt, int? ngay, int? thang, int? nam, string? trangThai, CancellationToken ct)
        {
            var y = nam ?? DateTime.Now.Year;
            if (laBt)
            {
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

                var rows = raw.Select(h =>
                {
                    int d, m, n;
                    if (dictKh.TryGetValue(h.MaHoSoBaoTri, out var nk))
                    { d = nk.Day; m = nk.Month; n = nk.Year; }
                    else
                    { d = h.NgayHt.Day; m = h.NgayHt.Month; n = h.NgayHt.Year; }
                    return new { h.MaHoSoBaoTri, h.TrangThai, h.TenTb, h.NgayHt, D = d, M = m, N = n };
                }).AsEnumerable();

                if (thang != null || ngay != null || nam != null)
                    rows = rows.Where(x => x.N == y);
                if (thang != null)
                    rows = rows.Where(x => x.M == thang.Value);
                if (ngay != null)
                    rows = rows.Where(x => x.D == ngay.Value);
                if (!string.IsNullOrEmpty(trangThai))
                {
                    if (trangThai == "Chờ xưởng")
                        rows = rows.Where(x => x.TrangThai == "Chờ xưởng" || x.TrangThai == "Chờ chốt ngày");
                    else
                        rows = rows.Where(x => x.TrangThai == trangThai);
                }

                var list = rows.OrderByDescending(x => x.NgayHt).ToList();
                var ky = FormatKy(ngay, thang, nam, y);
                var label = trangThai ?? "tất cả trạng thái";
                var sb = new StringBuilder();
                sb.AppendLine($"Dạ, {ky} có **{list.Count}** hồ sơ bảo trì **{label}**.");
                foreach (var h in list.Take(12))
                    sb.AppendLine($"• BT#{h.MaHoSoBaoTri} | {h.TenTb} | {h.TrangThai} | {h.NgayHt:dd/MM/yyyy HH:mm}");
                if (list.Count > 12)
                    sb.AppendLine($"… và {list.Count - 12} hồ sơ khác.");
                return sb.ToString().TrimEnd();
            }
            else
            {
                var raw = await _db.HoSoSuaChuas.AsNoTracking()
                    .Select(h => new
                    {
                        h.MaHoSoSuaChua,
                        h.TrangThai,
                        NgayHt = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao,
                        TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                    }).ToListAsync(ct);

                var rows = raw.Select(h => new
                {
                    h.MaHoSoSuaChua,
                    h.TrangThai,
                    h.TenTb,
                    h.NgayHt,
                    D = h.NgayHt.Day,
                    M = h.NgayHt.Month,
                    N = h.NgayHt.Year
                }).AsEnumerable();

                if (thang != null || ngay != null || nam != null)
                    rows = rows.Where(x => x.N == y);
                if (thang != null)
                    rows = rows.Where(x => x.M == thang.Value);
                if (ngay != null)
                    rows = rows.Where(x => x.D == ngay.Value);
                if (!string.IsNullOrEmpty(trangThai))
                    rows = rows.Where(x => x.TrangThai == trangThai);

                var list = rows.OrderByDescending(x => x.NgayHt).ToList();
                var ky = FormatKy(ngay, thang, nam, y);
                var label = trangThai ?? "tất cả trạng thái";
                var sb = new StringBuilder();
                sb.AppendLine($"Dạ, {ky} có **{list.Count}** hồ sơ sửa chữa **{label}**.");
                foreach (var h in list.Take(12))
                    sb.AppendLine($"• SC#{h.MaHoSoSuaChua} | {h.TenTb} | {h.TrangThai} | {h.NgayHt:dd/MM/yyyy HH:mm}");
                if (list.Count > 12)
                    sb.AppendLine($"… và {list.Count - 12} hồ sơ khác.");
                return sb.ToString().TrimEnd();
            }
        }

        private static string FormatKy(int? ngay, int? thang, int? nam, int yDefault)
        {
            if (ngay != null && thang != null)
                return $"ngày {ngay:00}/{thang:00}/{nam ?? yDefault}";
            if (thang != null)
                return $"tháng {thang}/{nam ?? yDefault}";
            if (nam != null)
                return $"năm {nam}";
            return "toàn hệ thống";
        }

        private async Task<string> TraLoiVatTuAsync(
            int maNd, string vt, int? maBt, int? maSc, string? trangThai, CancellationToken ct)
        {
            var q = _db.HoSoSuDungVatTus.AsNoTracking().AsQueryable();
            if (vt == "Nhân viên kỹ thuật")
            {
                var maNv = await _db.NhanViens.AsNoTracking()
                    .Where(n => n.MaNguoiDung == maNd)
                    .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
                if (maNv == null) return "Dạ, không xác định được nhân viên.";
                q = q.Where(h => h.MaNhanVienTH == maNv);
            }
            if (maBt != null) q = q.Where(h => h.MaHoSoBaoTri == maBt);
            if (maSc != null) q = q.Where(h => h.MaHoSoSuaChua == maSc);

            // Lấy rộng rồi lọc trạng thái trên memory (trạng thái DB có thể viết hoa/thường khác nhau)
            var raw = await q.OrderByDescending(h => h.NgayThucHien).Take(200)
                .Select(h => new
                {
                    h.MaHoSoVatTu,
                    h.MaHoSoBaoTri,
                    h.MaHoSoSuaChua,
                    TenTb = h.TenThietBi ?? "",
                    h.TongTien,
                    h.TrangThai,
                    h.NgayThucHien,
                    SoLoai = h.ChiTietSuDungVatTus.Select(c => c.TenVatTu).Distinct().Count(),
                    TongSl = h.ChiTietSuDungVatTus.Sum(c => c.SoLuong)
                }).ToListAsync(ct);

            var list = raw.AsEnumerable();
            if (!string.IsNullOrEmpty(trangThai))
            {
                list = list.Where(h =>
                    (h.TrangThai ?? "").Contains(trangThai, StringComparison.OrdinalIgnoreCase)
                    || (trangThai == "Xác nhận" && (h.TrangThai ?? "").Contains("xác nhận", StringComparison.OrdinalIgnoreCase)));
            }
            var arr = list.Take(30).ToList();

            // Thống kê theo trạng thái (khi hỏi tổng)
            var theoTt = raw.GroupBy(x => x.TrangThai ?? "(trống)")
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();

            if (arr.Count == 0)
            {
                var sb0 = new StringBuilder();
                sb0.AppendLine(string.IsNullOrEmpty(trangThai)
                    ? "Dạ, chưa có hồ sơ vật tư trong phạm vi của bạn."
                    : $"Dạ, chưa có hồ sơ vật tư trạng thái **{trangThai}**.");
                if (theoTt.Count > 0)
                    sb0.AppendLine("Phân bố hiện có: " + string.Join(" | ", theoTt));
                return sb0.ToString().TrimEnd();
            }

            var sb = new StringBuilder();
            var label = string.IsNullOrEmpty(trangThai) ? "tất cả trạng thái" : trangThai;
            sb.AppendLine($"Dạ, có **{arr.Count}** hồ sơ vật tư (**{label}**)" +
                          (raw.Count > arr.Count ? $" (hiển thị {arr.Count}/{raw.Count})" : "") + ".");
            if (theoTt.Count > 0 && string.IsNullOrEmpty(trangThai))
                sb.AppendLine("Theo trạng thái: " + string.Join(" | ", theoTt));
            foreach (var x in arr.Take(15))
            {
                var k = x.MaHoSoBaoTri != null ? $"BT#{x.MaHoSoBaoTri}" : (x.MaHoSoSuaChua != null ? $"SC#{x.MaHoSoSuaChua}" : $"VT#{x.MaHoSoVatTu}");
                sb.AppendLine($"• {k} | {x.TenTb} | SL {x.TongSl} | {x.TongTien:N0} ₫ | {x.TrangThai} | {x.NgayThucHien:dd/MM/yyyy}");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Lịch sử / danh sách phân công cho Tổ trưởng, GĐ, Xưởng.</summary>
        private async Task<string> TraLoiPhanCongToTruongAsync(CancellationToken ct)
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
                    p.LaNguoiGhiChep,
                    TenNv = p.MaNhanVienThucHienNavigation != null
                        ? p.MaNhanVienThucHienNavigation.HoTen
                        : ("NV#" + p.MaNhanVienThucHien)
                }).ToListAsync(ct);

            if (pcs.Count == 0)
                return "Dạ, chưa có phân công công việc nào trên hệ thống.";

            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, có **{pcs.Count}** phân công gần đây (lịch sử phân công):");
            foreach (var g in pcs.GroupBy(x => x.TrangThai ?? "(trống)"))
                sb.AppendLine($"• Trạng thái **{g.Key}**: {g.Count()}");
            sb.AppendLine("Chi tiết:");
            foreach (var p in pcs.Take(20))
            {
                var hs = p.MaHoSoBaoTri != null ? $"BT#{p.MaHoSoBaoTri}" : $"SC#{p.MaHoSoSuaChua}";
                sb.AppendLine(
                    $"• PC#{p.MaPhanCong} | {hs} | {p.TenNv} | {p.TrangThai}"
                    + $" | {p.NgayPhanCong:dd/MM/yyyy}"
                    + (p.LaNguoiGhiChep ? " | ghi chép" : ""));
            }
            if (pcs.Count > 20)
                sb.AppendLine($"… và {pcs.Count - 20} phân công khác.");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> TraLoiBuocAsync(int maNd, string vt, int? maBt, int? maSc, CancellationToken ct)
        {
            var q = _db.TienDoBuocQuyTrinhs.AsNoTracking().AsQueryable();
            if (vt == "Nhân viên kỹ thuật")
            {
                var maNv = await _db.NhanViens.AsNoTracking()
                    .Where(n => n.MaNguoiDung == maNd)
                    .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
                if (maNv == null) return "Dạ, không xác định được nhân viên.";
                var maBts = await _db.PhanCongCongViecs.AsNoTracking()
                    .Where(p => p.MaNhanVienThucHien == maNv && p.MaHoSoBaoTri != null)
                    .Select(p => p.MaHoSoBaoTri!.Value).Distinct().ToListAsync(ct);
                var maScs = await _db.PhanCongCongViecs.AsNoTracking()
                    .Where(p => p.MaNhanVienThucHien == maNv && p.MaHoSoSuaChua != null)
                    .Select(p => p.MaHoSoSuaChua!.Value).Distinct().ToListAsync(ct);
                q = q.Where(t =>
                    (t.MaHoSoBaoTri != null && maBts.Contains(t.MaHoSoBaoTri.Value)) ||
                    (t.MaHoSoSuaChua != null && maScs.Contains(t.MaHoSoSuaChua.Value)));
            }
            if (maBt != null) q = q.Where(t => t.MaHoSoBaoTri == maBt);
            if (maSc != null) q = q.Where(t => t.MaHoSoSuaChua == maSc);

            var rows = await q.OrderByDescending(t => t.ThoiDiemCapNhat).Take(200)
                .Select(t => new { t.MaHoSoBaoTri, t.MaHoSoSuaChua, t.SoBuoc, t.MoTaBuoc, t.TrangThai })
                .ToListAsync(ct);
            if (rows.Count == 0)
                return "Dạ, chưa ghi nhận bước quy trình khớp yêu cầu.";
            var g = rows.GroupBy(t => t.MaHoSoBaoTri != null ? $"BT#{t.MaHoSoBaoTri}" : $"SC#{t.MaHoSoSuaChua}").First();
            var uniq = g.GroupBy(x => x.SoBuoc).Select(x => x.First()).OrderBy(x => x.SoBuoc).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, hồ sơ **{g.Key}**: **{uniq.Count} bước** (xong: {uniq.Count(x => x.TrangThai == "DaXong")}).");
            foreach (var b in uniq)
                sb.AppendLine($"• B{b.SoBuoc}: {b.MoTaBuoc} ({b.TrangThai})");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> TraLoiPhanCongNvktAsync(int maNd, CancellationToken ct)
        {
            var maNv = await _db.NhanViens.AsNoTracking()
                .Where(n => n.MaNguoiDung == maNd)
                .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
            if (maNv == null) return "Dạ, không xác định được nhân viên.";
            var pc = await _db.PhanCongCongViecs.AsNoTracking()
                .Where(p => p.MaNhanVienThucHien == maNv)
                .OrderByDescending(p => p.NgayPhanCong).Take(20)
                .Select(p => new { p.MaPhanCong, p.MaHoSoBaoTri, p.MaHoSoSuaChua, p.TrangThai, p.NgayPhanCong })
                .ToListAsync(ct);
            if (pc.Count == 0) return "Dạ, bạn chưa có phân công nào.";
            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, bạn có **{pc.Count}** phân công gần đây:");
            foreach (var g in pc.GroupBy(x => x.TrangThai ?? "(trống)"))
                sb.AppendLine($"• {g.Key}: {g.Count()}");
            foreach (var p in pc.Take(10))
            {
                var hs = p.MaHoSoBaoTri != null ? $"BT#{p.MaHoSoBaoTri}" : $"SC#{p.MaHoSoSuaChua}";
                sb.AppendLine($"• PC#{p.MaPhanCong} | {hs} | {p.TrangThai} | {p.NgayPhanCong:dd/MM/yyyy}");
            }
            return sb.ToString().TrimEnd();
        }

        private async Task<string> GanThongKeTheoThangAsync(string context, string vt, CancellationToken ct)
        {
            if (vt == "Nhân viên kỹ thuật") return context;
            var mapKh = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.MaHoSoBaoTri != null)
                .Select(c => new { Ma = c.MaHoSoBaoTri!.Value, c.NgayDuKienBaoTri })
                .ToListAsync(ct);
            var dictKh = mapKh.GroupBy(x => x.Ma).ToDictionary(g => g.Key, g => g.First().NgayDuKienBaoTri);
            var bts = await _db.HoSoBaoTris.AsNoTracking()
                .Select(h => new { h.MaHoSoBaoTri, h.TrangThai, NgayHt = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao })
                .ToListAsync(ct);

            var sb = new StringBuilder();
            sb.AppendLine(context);
            sb.AppendLine("THONG_KE_BT_THEO_THANG (uu tien ngay ke hoach):");
            var groups = bts.Select(h =>
            {
                int m, y;
                if (dictKh.TryGetValue(h.MaHoSoBaoTri, out var nk)) { m = nk.Month; y = nk.Year; }
                else { m = h.NgayHt.Month; y = h.NgayHt.Year; }
                return new { y, m, h.TrangThai };
            }).GroupBy(x => new { x.y, x.m, x.TrangThai });
            foreach (var g in groups.OrderByDescending(x => x.Key.y).ThenByDescending(x => x.Key.m).Take(36))
                sb.AppendLine($"  {g.Key.m:00}/{g.Key.y}|{g.Key.TrangThai}={g.Count()}");
            return sb.ToString();
        }

        private static string? TrichTenThietBi(string q)
        {
            var s = q;
            string[] bo =
            {
                "thiet bi", "trang thai", "tinh trang", "dang o", "la gi", "bao tri", "sua chua",
                "gan nhat", "tiep theo", "lich", "ngay", "cua", "bao nhieu", "co may", "danh muc",
                "thang", "nam", "ho so", "da", "duoc", "hien", "tai", "va", "cho", "toi", "minh", "sap toi"
            };
            foreach (var b in bo)
                s = Regex.Replace(s, $@"\b{Regex.Escape(b)}\b", " ");
            s = Regex.Replace(s, @"\d{4}", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length < 2 || Regex.IsMatch(s, @"^\d{1,2}$")) return null;
            return s;
        }

        private static int? TrichMa(string q, string prefix)
        {
            var m = Regex.Match(q, $@"\b{prefix}\s*#?\s*(\d+)\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var id)) return id;
            return null;
        }

        private static int DiemKhop(string q, string ten)
        {
            var t = ChuanHoa(ten);
            if (string.IsNullOrEmpty(t)) return 0;
            if (q.Contains(t) || t.Contains(q)) return 10;
            return t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => w.Length >= 2 && q.Contains(w));
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

            // Fallback chỉ khi đúng ý hỏi HS BT/SC — không nuốt vật tư / phân công
            if (vt != "Nhân viên kỹ thuật" && LaHoiHoSoBtSc(q, ngay, thang))
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

            return "Dạ, hiện đang dùng fallback rule (Groq chưa cấu hình hoặc lỗi model/API). "
                   + "Kiểm tra Groq:ApiKey + Groq:Model trong appsettings.json (model hiện hỗ trợ free: qwen/qwen3.8-27b, openai/gpt-oss-20b) rồi restart API. "
                   + "Hỏi mẫu: «tháng 12/2026 có bao nhiêu hồ sơ bảo trì đã hoàn thành?» / «nhân viên nào đang được phân công?»";
        }

        // ===================== ROLE + HELPERS =====================

        private static string MoTaPhamVi(string vt) => vt switch
        {
            "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" =>
                "- Công việc: tạo HS bảo trì, phân công nhiều NVKT, cập nhật NV bận, theo dõi bước/vật tư, thống kê.\n"
                + "- Dữ liệu được phép: thiết bị, lịch/KH/HS BT-SC, DS_NVKT (rảnh/bận), Phan_cong, HS chờ phân công, bước, vật tư.\n"
                + "- Ví dụ hỏi đúng: «HS chờ phân công tháng này?», «NV nào rảnh?», «BT#12 đang ở bước nào?»\n"
                + "- KHÔNG duyệt hộ Giám đốc; KHÔNG chốt ngày hộ Xưởng.",
            "Xưởng" or "Tổ trưởng sản xuất" =>
                "- Công việc: chốt ngày bảo trì trong tháng, gửi HS lên GĐ, xác nhận/từ chối kết quả NVKT.\n"
                + "- Dữ liệu: HS chờ xưởng, lịch chốt ngày, trạng thái sau khi GĐ duyệt/từ chối.\n"
                + "- KHÔNG phân công NVKT; KHÔNG duyệt cuối như GĐ.",
            "Giám đốc" =>
                "- Công việc: duyệt/từ chối HS BT-SC, xem thống kê toàn hệ thống.\n"
                + "- Dữ liệu: HS chờ duyệt, lịch sử phê duyệt, tổng hợp theo tháng/trạng thái.\n"
                + "- KHÔNG phân công NVKT; KHÔNG chốt ngày Xưởng.",
            "Nhân viên kỹ thuật" =>
                "- Công việc: nhận việc được phân công, chọn bước bảo trì/sửa chữa, hoàn thành gửi Xưởng.\n"
                + "- CHỈ dữ liệu gắn tài khoản này: phân công của tôi, HS của tôi, bước/vật tư của tôi.\n"
                + "- KHÔNG xem toàn công ty; KHÔNG duyệt; KHÔNG chốt ngày; KHÔNG phân công người khác.",
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
