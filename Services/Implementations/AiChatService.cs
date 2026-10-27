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
    /// OPC Bot: Groq + KIẾN THỨC HỆ THỐNG + CONTEXT DB theo vai trò.
    /// LLM hiểu toàn bộ luồng app; mỗi role chỉ được trả lời trong phạm vi role đó.
    /// Không dùng cặp Q&amp;A mẫu; không dùng Ollama.
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

            try
            {
                // KIẾN TRÚC: Chatbot = "người đọc app"
                // 1) Nạp TOÀN BỘ snapshot DB liên quan (thiết bị, HS, phân công, vật tư…) theo vai trò.
                // 2) Groq ĐỌC câu hỏi tự nhiên → tự tìm trong snapshot → trả lời (KHÔNG khớp mẫu câu hỏi).
                // 3) Chỉ khi Groq lỗi/hết key mới tra DB trực tiếp (vẫn không dùng Q&A mẫu).
                var context = await NapContextVaiTroAsync(maNguoiDung, vt, ct);
                context = await GanThongKeTheoThangAsync(context, vt, ct);

                if (!string.IsNullOrEmpty(GroqKey))
                {
                    var (ok, text, err) = await GoiGroqAsync(vt, ten, cauHoi.Trim(), context, lichSu, ct);
                    if (ok && !string.IsNullOrWhiteSpace(text))
                        return (true, text.Trim(), null);
                    _logger.LogWarning("Groq lỗi, chuyển tra DB trực tiếp: {Err}", err);
                }

                // Fallback: tra DB theo ngữ nghĩa câu hỏi (không gợi ý mẫu câu)
                var soLieu = await TraLoiSoLieuChinhXacAsync(maNguoiDung, vt, q, cauHoi.Trim(), ct);
                if (soLieu != null)
                    return (true, soLieu, null);

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
            // Đủ danh sách thiết bị để trả lời «hệ thống có những thiết bị nào?»
            sb.AppendLine("DS_thiet_bi (đầy đủ — dùng khi user hỏi danh sách/có những thiết bị nào):");
            foreach (var t in tbs.OrderBy(x => x.LoaiThietBi).ThenBy(x => x.TenThietBi))
            {
                sb.AppendLine(
                    $"  TB#{t.MaThietBi}|{t.TenThietBi}|loai={t.LoaiThietBi}|vitri={t.ViTriLapDat}"
                    + $"|tt={t.TinhTrangHienTai}"
                    + $"|bt_gan={(t.NgayBaoTriGanNhat.HasValue ? t.NgayBaoTriGanNhat.Value.ToString("dd/MM/yyyy") : "-")}"
                    + $"|bt_tiep={(t.NgayBaoTriTiepTheo.HasValue ? t.NgayBaoTriTiepTheo.Value.ToString("dd/MM/yyyy") : "-")}");
            }

            // Kho vật tư (catalog) — hỏi «có những vật tư nào»
            var vatTuCat = await _db.VatTus.AsNoTracking()
                .Select(v => new { v.MaVatTu, v.TenVatTu, v.DonViTinh, v.SoLuongTonKho, v.DonGia })
                .OrderBy(v => v.TenVatTu)
                .ToListAsync(ct);
            sb.AppendLine($"Tong_vat_tu_kho: {vatTuCat.Count}");
            sb.AppendLine("DS_vat_tu_kho:");
            foreach (var v in vatTuCat)
                sb.AppendLine($"  VTU#{v.MaVatTu}|{v.TenVatTu}|dvt={v.DonViTinh}|ton={v.SoLuongTonKho}|dongia={v.DonGia:0}");

            if (vt is "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" or "Xưởng" or "Tổ trưởng sản xuất" or "Giám đốc" or "Phó giám đốc")
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

                var vtsAll = await _db.HoSoSuDungVatTus.AsNoTracking()
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
                sb.AppendLine($"Tong_HS_vat_tu: {vtsAll.Count}");
                foreach (var g in vtsAll.GroupBy(x => x.TrangThai ?? "(trống)"))
                    sb.AppendLine($"  VT_TT[{g.Key}]={g.Count()}");
                // Alias UI: "Xác nhận" gồm cả "Đã xem"
                var vtXacNhan = vtsAll.Count(x =>
                {
                    var t = x.TrangThai ?? "";
                    return t.Contains("Xác nhận", StringComparison.OrdinalIgnoreCase)
                           || t.Equals("Đã xem", StringComparison.OrdinalIgnoreCase);
                });
                sb.AppendLine($"  VT_TT[Xác nhận_hoac_Da_xem]={vtXacNhan}");
                sb.AppendLine("HS_vat_tu:");
                foreach (var v in vtsAll.OrderByDescending(x => x.NgayThucHien).Take(50))
                {
                    var hs = v.MaHoSoBaoTri != null ? $"BT#{v.MaHoSoBaoTri}"
                        : (v.MaHoSoSuaChua != null ? $"SC#{v.MaHoSoSuaChua}" : $"VT#{v.MaHoSoVatTu}");
                    sb.AppendLine($"  VT#{v.MaHoSoVatTu}|{hs}|{v.TenTb}|sl={v.TongSl}|loai={v.SoLoai}|tien={v.TongTien:0}|tt={v.TrangThai}|ngay={v.NgayThucHien:dd/MM/yyyy}");
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

        // ===================== GROQ + KIẾN THỨC HỆ THỐNG =====================

        private async Task<(bool ok, string? text, string? err)> GoiGroqAsync(
            string vt,
            string ten,
            string cauHoi,
            string context,
            IReadOnlyList<(string role, string content)>? lichSu,
            CancellationToken ct)
        {
            // Không khớp mẫu câu hỏi. LLM tự đọc CAU_HOI + CONTEXT_DB như người đang xem app.
            var kienThuc = XayDungKienThucHeThong(vt);
            var phamVi = MoTaPhamVi(vt);
            var system =
                "Bạn là OPC Bot — trợ lý đã «vào trong» app quản lý bảo trì/sửa chữa thiết bị cơ điện (Công ty Dược OPC).\n" +
                "Bạn đang xem đúng dữ liệu vận hành realtime của app (CONTEXT_DB) và nắm quy trình nghiệp vụ (KIẾN THỨC).\n" +
                $"Người hỏi: {ten}. Vai trò đăng nhập: «{vt}».\n\n" +
                "CÁCH LÀM VIỆC (bắt buộc):\n" +
                "1) Đọc hiểu CAU_HOI bằng tiếng Việt tự nhiên — không cần câu hỏi đúng mẫu, không cần từ khóa cố định.\n" +
                "2) Tự xác định user muốn biết gì (danh sách, số lượng, trạng thái, quy trình, ai phụ trách, mã HS, tên máy…).\n" +
                "3) Tự tìm câu trả lời trong CONTEXT_DB và KIẾN THỨC. Ví dụ:\n" +
                "   - «hệ thống có những thiết bị nào?» → liệt kê từ DS_thiet_bi / Tong_thiet_bi / Danh_muc\n" +
                "   - «máy X đang thế nào?» → tìm tên trong DS_thiet_bi\n" +
                "   - «có bao nhiêu hồ sơ chờ duyệt?» → đếm HS_BT / HS_SC theo trạng thái\n" +
                "   - «tôi phải làm gì tiếp?» → giải thích bước tiếp theo theo vai trò từ KIẾN THỨC\n" +
                "4) Trả lời đúng trọng tâm, đủ ý; liệt kê thì dùng bullet; mở đầu bằng «Dạ,».\n" +
                "5) Chỉ dùng số liệu có trong CONTEXT_DB — cấm bịa mã HS, tên thiết bị, tên người.\n" +
                "6) Nếu CONTEXT không có dữ liệu phù hợp → nói rõ «trong dữ liệu hiện tại chưa thấy…», không bịa, không bảo user hỏi theo mẫu.\n" +
                "7) Tôn trọng phạm vi vai trò «" + vt + "»: không làm hộ việc của role khác; có thể giải thích role nào phụ trách.\n" +
                "8) Cấm lộ mật khẩu, API key, mã nguồn.\n\n" +
                "KIẾN THỨC NGHIỆP VỤ:\n" + kienThuc + "\n" +
                "PHẠM VI VAI TRÒ:\n" + phamVi;

            var user = new StringBuilder();
            user.AppendLine("CONTEXT_DB — snapshot dữ liệu app đang chạy (nguồn sự thật duy nhất cho số liệu):");
            user.AppendLine(context);
            if (lichSu != null && lichSu.Count > 0)
            {
                user.AppendLine("LICH_SU_HOI_THOAI:");
                foreach (var (role, content) in lichSu.TakeLast(8))
                    user.AppendLine($"- {role}: {content}");
            }
            user.AppendLine("CAU_HOI_CUA_NGUOI_DUNG:");
            user.AppendLine(cauHoi);
            user.AppendLine("Hãy phân tích câu hỏi trên và trả lời dựa trên CONTEXT_DB + kiến thức nghiệp vụ.");

            // Groq OpenAI-compatible Chat Completions
            var body = new
            {
                model = GroqModel,
                temperature = 0.2,
                max_tokens = 2048,
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
                // UI/DB: "Xác nhận" | "Đã xem" | "Chờ duyệt" | "Chờ gửi" | "Đã gửi GĐ"
                string? ttVt = null;
                if (Co(q, "xac nhan", "da xac nhan", "da xem")) ttVt = "Xác nhận";
                else if (Co(q, "cho duyet", "da gui")) ttVt = "Chờ duyệt";
                else if (Co(q, "cho gui")) ttVt = "Chờ gửi";
                else if (Co(q, "tu choi")) ttVt = "Từ chối";
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

            // 3) THIẾT BỊ — danh sách / có những gì / trạng thái từng máy (fallback khi Groq lỗi)
            if (Co(q, "thiet bi", "may moc", "may nen", "danh sach may")
                && !LaHoiVatTu(q) && !LaHoiPhanCong(q)
                && !Co(q, "ho so bao tri", "ho so sua chua"))
            {
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

                // Hỏi 1 máy cụ thể
                var tenGoi = TrichTenThietBi(q);
                if (tenGoi != null)
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
                        var vtTri = string.IsNullOrWhiteSpace(hit.ViTriLapDat) ? "chưa ghi" : hit.ViTriLapDat;
                        return $"Dạ, **{hit.TenThietBi}** (TB#{hit.MaThietBi}).\n"
                             + $"• Loại: {hit.LoaiThietBi ?? "—"}\n"
                             + $"• Vị trí: {vtTri}\n"
                             + $"• Trạng thái: **{tt}**\n"
                             + $"• BT gần nhất: {gan} | BT tiếp theo: {tiep}";
                    }
                }

                // Hỏi danh sách / hệ thống có những thiết bị nào / gồm những gì
                var hoiDanhSach = Co(q, "nao", "nhung", "gom", "bao gom", "danh sach", "danh muc",
                    "he thong", "co nhung", "nhung gi", "cai gi", "nhung may");
                var hoiDem = Co(q, "bao nhieu", "tong", "co may", "tinh trang");
                if (hoiDanhSach || hoiDem || (Co(q, "thiet bi") && !Co(q, "ho so")))
                {
                    int Dem(string k) => tbs.Count(x => (x.TinhTrangHienTai ?? "")
                        .Contains(k, StringComparison.OrdinalIgnoreCase));
                    var sb = new StringBuilder();
                    sb.AppendLine($"Dạ, hệ thống đang quản lý **{tbs.Count}** thiết bị.");
                    sb.AppendLine($"Trạng thái: Sản xuất **{Dem("Sản xuất")}** | Bảo trì **{Dem("Bảo trì")}** | Sửa chữa **{Dem("Sửa chữa")}**.");
                    foreach (var g in tbs.GroupBy(x => x.LoaiThietBi ?? "(chưa loại)").OrderBy(g => g.Key))
                    {
                        sb.AppendLine($"**{g.Key}** ({g.Count()}):");
                        foreach (var t in g.OrderBy(x => x.TenThietBi))
                            sb.AppendLine($"  • TB#{t.MaThietBi} {t.TenThietBi} — {t.TinhTrangHienTai ?? "—"}"
                                          + (string.IsNullOrWhiteSpace(t.ViTriLapDat) ? "" : $" ({t.ViTriLapDat})"));
                    }
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
                {
                    var t = h.TrangThai ?? "";
                    if (trangThai == "Xác nhận")
                        return t.Contains("Xác nhận", StringComparison.OrdinalIgnoreCase)
                               || t.Equals("Đã xem", StringComparison.OrdinalIgnoreCase);
                    if (trangThai == "Chờ duyệt")
                        return t.Contains("Chờ duyệt", StringComparison.OrdinalIgnoreCase)
                               || t.Equals("Đã gửi GĐ", StringComparison.OrdinalIgnoreCase);
                    return t.Contains(trangThai, StringComparison.OrdinalIgnoreCase);
                });
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

            // Danh sách thiết bị khi Groq lỗi — không bắt buộc từ khóa «bao nhiêu»
            if (Co(q, "thiet bi", "may moc") && !Co(q, "ho so"))
            {
                var tbs = await _db.ThietBis.AsNoTracking()
                    .Select(t => new { t.MaThietBi, t.TenThietBi, t.LoaiThietBi, t.TinhTrangHienTai, t.ViTriLapDat })
                    .OrderBy(t => t.LoaiThietBi).ThenBy(t => t.TenThietBi)
                    .ToListAsync(ct);
                if (tbs.Count == 0)
                    return "Dạ, trong hệ thống hiện chưa có thiết bị nào được ghi nhận.";
                var sbTb = new StringBuilder();
                sbTb.AppendLine($"Dạ, hệ thống đang quản lý **{tbs.Count}** thiết bị:");
                foreach (var g in tbs.GroupBy(x => x.LoaiThietBi ?? "(chưa loại)"))
                {
                    sbTb.AppendLine($"**{g.Key}** ({g.Count()}):");
                    foreach (var t in g)
                        sbTb.AppendLine($"  • TB#{t.MaThietBi} {t.TenThietBi} — {t.TinhTrangHienTai ?? "—"}");
                }
                return sbTb.ToString().TrimEnd();
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

            return "Dạ, hiện chưa tìm thấy dữ liệu khớp câu hỏi trong hệ thống (hoặc dịch vụ AI tạm gián đoạn). "
                   + "Bạn thử hỏi lại theo nội dung đang có trên app — ví dụ thiết bị, hồ sơ bảo trì/sửa chữa, "
                   + "phân công, vật tư, trạng thái — mình sẽ tra đúng dữ liệu đang vận hành.";
        }

        // ===================== ROLE + HELPERS =====================

        /// <summary>
        /// Kiến thức domain toàn hệ thống (không phải Q&amp;A mẫu).
        /// LLM dùng để hiểu luồng/app; phạm vi trả lời vẫn bị khóa theo vai trò.
        /// </summary>
        private static string XayDungKienThucHeThong(string vt)
        {
            var chung =
                "APP: 2 chức năng chính — Bảo trì định kỳ và Sửa chữa sự cố thiết bị cơ điện.\n"
                + "VAI TRÒ: Tổ trưởng cơ điện | Xưởng | Giám đốc (Phó giám đốc) | Nhân viên kỹ thuật | Admin hệ thống.\n\n"
                + "LUỒNG BẢO TRÌ (tuần tự):\n"
                + "1) Tổ trưởng cơ điện TẠO hồ sơ bảo trì (gắn thiết bị, nội dung, thời gian dự kiến).\n"
                + "2) Tổ trưởng GỬI hồ sơ → Xưởng.\n"
                + "3) Xưởng CHỐT NGÀY bảo trì trong tháng đó.\n"
                + "4) Xưởng GỬI hồ sơ → Giám đốc duyệt.\n"
                + "5) Giám đốc TỪ CHỐI → trả Xưởng chỉnh sửa; Giám đốc DUYỆT → mở khóa phân công cho Tổ trưởng.\n"
                + "6) Tổ trưởng PHÂN CÔNG một hoặc nhiều NVKT (NV bận thì Tổ trưởng cập nhật lại trên hệ thống).\n"
                + "7) Mỗi NVKT nhận việc trên tài khoản mình → chọn các bước quy trình → đủ bước thì HOÀN THÀNH → gửi Xưởng.\n"
                + "8) Xưởng xác nhận kết quả hoàn thành.\n\n"
                + "LUỒNG SỬA CHỮA (tóm tắt):\n"
                + "- Xưởng tạo hồ sơ sửa chữa (mô tả hư hỏng, phương án) → gửi Giám đốc duyệt → sau duyệt Tổ trưởng phân công NVKT → NVKT thực hiện bước → hoàn thành gửi Xưởng.\n\n"
                + "TỪ ĐIỂN TRẠNG THÁI HỒ SƠ BT (thường gặp):\n"
                + "Nháp | Chờ xưởng | Chờ GĐ duyệt / Chờ duyệt | Từ chối | Đã duyệt | Chờ phân công | Đang thực hiện | Chờ xác nhận | Đã hoàn thành | Đã hủy.\n"
                + "Ý nghĩa: «Đã duyệt»/«Chờ phân công» = GĐ đã duyệt, Tổ trưởng được phân công NVKT.\n"
                + "«Đang thực hiện» = NVKT đã nhận/tiến hành. «Chờ xác nhận» = NVKT hoàn thành, chờ Xưởng.\n\n"
                + "PHÂN CÔNG: 1 HS có thể gắn nhiều NVKT; có thể có người ghi chép (LaNguoiGhiChep).\n"
                + "BƯỚC QUY TRÌNH: lấy từ catalog QuyTrinhThietBi theo thiết bị; tiến độ lưu TienDoBuocQuyTrinh (SoBuoc, MoTaBuoc, TrangThai).\n"
                + "VẬT TƯ: hồ sơ sử dụng vật tư gắn HS BT hoặc SC; trạng thái có thể gồm chờ xác nhận / Xác nhận / Đã xem.\n"
                + "THIẾT BỊ: TinhTrangHienTai ví dụ Sản xuất | Bảo trì | Sửa chữa; có NgayBaoTriGanNhat / NgayBaoTriTiepTheo.\n";

            // Bổ sung kiến thức sâu hơn cho đúng role đang chat (vẫn trong khung hệ thống)
            var theoRole = vt switch
            {
                "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" =>
                    "CHI TIẾT CHO TỔ TRƯỞNG CƠ ĐIỆN:\n"
                    + "- Tạo HS BT, gửi Xưởng; sau GĐ duyệt mới phân công.\n"
                    + "- Phân công nhiều NV; cập nhật khi NV bận.\n"
                    + "- Theo dõi bước, vật tư, lịch sử phân công, thiết bị, kế hoạch BT.\n"
                    + "- Không chốt ngày thay Xưởng; không duyệt thay Giám đốc.\n",
                "Xưởng" or "Tổ trưởng sản xuất" =>
                    "CHI TIẾT CHO XƯỞNG:\n"
                    + "- Nhận HS BT từ Tổ trưởng → chốt ngày trong tháng → gửi Giám đốc.\n"
                    + "- GĐ từ chối → Xưởng chỉnh sửa rồi gửi lại.\n"
                    + "- Tạo HS sửa chữa; xác nhận kết quả khi NVKT hoàn thành.\n"
                    + "- Không phân công NVKT; không phê duyệt cuối như GĐ.\n",
                "Giám đốc" or "Phó giám đốc" =>
                    "CHI TIẾT CHO GIÁM ĐỐC / PHÓ GIÁM ĐỐC:\n"
                    + "- Duyệt hoặc từ chối HS bảo trì / sửa chữa / vật tư (theo phân quyền).\n"
                    + "- Xem thống kê, lịch sử phê duyệt, trạng thái toàn hệ thống.\n"
                    + "- Không phân công NVKT; không chốt ngày Xưởng.\n",
                "Nhân viên kỹ thuật" =>
                    "CHI TIẾT CHO NHÂN VIÊN KỸ THUẬT:\n"
                    + "- Chỉ thấy việc được phân công cho mình.\n"
                    + "- Chọn bước bảo trì/sửa chữa; khi đủ bước bấm hoàn thành → gửi Xưởng.\n"
                    + "- Có thể ghi nhận vật tư sử dụng theo bước.\n"
                    + "- Không xem toàn công ty; không duyệt; không chốt ngày; không phân công người khác.\n",
                _ => "CHI TIẾT: chỉ thông tin gắn tài khoản đang đăng nhập.\n"
            };

            return chung + theoRole;
        }

        private static string MoTaPhamVi(string vt) => vt switch
        {
            "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" =>
                "- Được hỏi/trả lời: tạo & theo dõi HS bảo trì, phân công NVKT, NV rảnh/bận, HS chờ phân công,\n"
                + "  bước quy trình, vật tư gắn HS, thiết bị, lịch/kế hoạch BT, thống kê liên quan tổ.\n"
                + "- CẤM: duyệt hộ GĐ, chốt ngày hộ Xưởng, xem hộ việc riêng tư không liên quan.\n"
                + "- Khi user hỏi «tôi làm gì?» / «quy trình của tôi» → giải thích đúng bước 1–2 và 6 của luồng BT.",
            "Xưởng" or "Tổ trưởng sản xuất" =>
                "- Được hỏi/trả lời: HS chờ xưởng, chốt ngày BT, gửi GĐ, HS bị từ chối cần sửa,\n"
                + "  tạo/theo dõi HS sửa chữa, xác nhận hoàn thành từ NVKT, trạng thái liên quan xưởng.\n"
                + "- CẤM: phân công NVKT, phê duyệt cuối như GĐ.\n"
                + "- Khi hỏi quy trình xưởng → nhấn mạnh bước 3–4–5 và xác nhận bước 8.",
            "Giám đốc" or "Phó giám đốc" =>
                "- Được hỏi/trả lời: HS chờ duyệt, duyệt/từ chối, lịch sử phê duyệt, thống kê toàn hệ thống,\n"
                + "  trạng thái BT/SC/vật tư tổng hợp.\n"
                + "- CẤM: phân công NVKT, chốt ngày Xưởng.\n"
                + "- Khi hỏi quy trình GĐ → nhấn mạnh bước 5 (duyệt/từ chối).",
            "Nhân viên kỹ thuật" =>
                "- CHỈ dữ liệu của tài khoản này: phân công của tôi, HS của tôi, bước/vật tư của tôi.\n"
                + "- Được hỏi: việc đang làm, bước còn lại, cách hoàn thành gửi Xưởng.\n"
                + "- CẤM: toàn công ty, duyệt, chốt ngày, phân công người khác.",
            _ => "- Chỉ thông tin gắn tài khoản đang đăng nhập."
        };

        private static string? TuChoiVuotVaiTro(string vt, string q)
        {
            // Chặn sớm các yêu cầu vượt quyền rõ ràng (Groq vẫn nhận kiến thức hệ thống để giải thích đúng ai phụ trách)
            if (vt == "Nhân viên kỹ thuật")
            {
                if (Co(q, "toan bo", "tat ca nhan vien", "toan cong ty"))
                    return "Dạ, NVKT chỉ xem công việc được phân công trên tài khoản của bạn.";
                if (Co(q, "duyet") && Co(q, "ho so", "giam doc"))
                    return "Dạ, phê duyệt hồ sơ thuộc Giám đốc — NVKT không thực hiện được.";
                if (Co(q, "chot ngay"))
                    return "Dạ, chốt ngày bảo trì thuộc Xưởng.";
                if (Co(q, "phan cong") && Co(q, "nhan vien", "to truong"))
                    return "Dạ, phân công nhân viên thuộc Tổ trưởng cơ điện.";
            }
            if (vt is "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật")
            {
                if (Co(q, "thay giam doc") || (Co(q, "toi duyet", "duyet ho") && Co(q, "giam doc")))
                    return "Dạ, Tổ trưởng không duyệt hộ Giám đốc. Sau khi GĐ duyệt, bạn mới phân công NVKT.";
                if (Co(q, "chot ngay") && Co(q, "thay", "ho"))
                    return "Dạ, chốt ngày bảo trì trong tháng thuộc Xưởng.";
            }
            if (vt is "Xưởng" or "Tổ trưởng sản xuất")
            {
                // Chỉ chặn khi Xưởng muốn tự phân công thay Tổ trưởng
                if (Co(q, "phan cong") && Co(q, "thay to truong", "ho to truong", "toi phan cong", "giup phan cong"))
                    return "Dạ, phân công NVKT thuộc Tổ trưởng cơ điện. Xưởng chốt ngày và xác nhận hoàn thành.";
            }
            if (vt is "Giám đốc" or "Phó giám đốc")
            {
                if (Co(q, "phan cong nhan vien") && Co(q, "thay", "ho"))
                    return "Dạ, phân công thuộc Tổ trưởng cơ điện (sau khi bạn duyệt hồ sơ).";
                if (Co(q, "chot ngay") && Co(q, "thay", "ho"))
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
