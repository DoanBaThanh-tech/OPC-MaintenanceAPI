using System.Globalization;
using System.Net.Http.Json;
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
    /// Chatbot theo vai trò: mỗi lần hỏi nạp TOÀN BỘ số liệu liên quan vai trò từ DB,
    /// đưa cho AI local trả lời theo đúng câu hỏi (không dùng mẫu Q&amp;A cố định).
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
                    return (false, null, $"Ollama trả {(int)res.StatusCode}. Hãy mở Ollama trên máy.");
                return (true, Model, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ollama offline");
                return (false, null,
                    "Không kết nối được Ollama. Hãy chạy Ollama trên máy (http://127.0.0.1:11434).");
            }
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
                return (false, null, "Bạn ơi, gửi giúp mình một câu hỏi nhé.");

            var ten = string.IsNullOrWhiteSpace(hoTen) ? "bạn" : hoTen.Trim();
            var vt = (vaiTro ?? "Người dùng").Trim();

            if (LaCauHoiNhayCam(cauHoi))
            {
                return (true,
                    $"{ten} ơi, mình không chia sẻ được code hay mật khẩu hệ thống nhé. "
                    + "Bạn hỏi về hồ sơ, quy trình, vật tư, số liệu trong phạm vi công việc của mình — mình hỗ trợ liền.",
                    null);
            }

            // Tách nhiều yêu cầu trong 1 tin nhắn
            var cacCau = TachNhieuCauHoi(cauHoi);
            var traLoiParts = new List<string>();

            string boDuLieu;
            try
            {
                boDuLieu = await NapDuLieuTheoVaiTroAsync(maNguoiDung, vt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi nạp dữ liệu vai trò");
                return (false, null, "Mình chưa đọc được dữ liệu hệ thống lúc này. Bạn thử lại sau vài giây giúp mình nhé.");
            }

            foreach (var (idx, cau) in cacCau.Select((c, i) => (i + 1, c)))
            {
                // Số liệu quan trọng: trả lời CHÍNH XÁC từ DB (không để model bịa số)
                var chinhXac = await TryTraLoiChinhXacAsync(maNguoiDung, vt, ten, cau, ct);
                string doan;
                if (!string.IsNullOrWhiteSpace(chinhXac))
                {
                    doan = chinhXac!;
                }
                else
                {
                    var (ok, traLoi, loi) = await GoiOllamaVoiDuLieuAsync(
                        vt, ten, cau, boDuLieu, idx == 1 ? lichSu : null, ct);
                    if (!ok || string.IsNullOrWhiteSpace(traLoi))
                        doan = loi ?? "Mình chưa trả lời được phần này. Bạn diễn đạt lại giúp mình nhé.";
                    else
                        doan = traLoi!;
                }

                if (cacCau.Count > 1)
                    traLoiParts.Add($"**Yêu cầu {idx}:** {cau.Trim()}\n{doan}");
                else
                    traLoiParts.Add(doan);
            }

            return (true, string.Join("\n\n", traLoiParts), null);
        }

        /// <summary>Tách 2+ câu hỏi trong một tin (dấu ?, xuống dòng, hoặc "và/với lại/thêm").</summary>
        private static List<string> TachNhieuCauHoi(string raw)
        {
            var text = raw.Trim();
            // Ưu tiên tách theo dòng
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (lines.Count >= 2) return lines;

            // Tách theo dấu ?
            var byQ = Regex.Split(text, @"(?<=\?)\s+")
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (byQ.Count >= 2) return byQ;

            // Tách theo "và hỏi" / "với lại" / "thêm nữa" / số thứ tự 1) 2)
            var byNum = Regex.Split(text, @"(?:^|\s)(?:\d+[\.\)\-:]|thứ\s*\d+)\s+", RegexOptions.IgnoreCase)
                .Select(x => x.Trim()).Where(x => x.Length > 3).ToList();
            if (byNum.Count >= 2) return byNum;

            var byVa = Regex.Split(text, @"\s+(?:với lại|thêm nữa|ngoài ra|và hỏi)\s+", RegexOptions.IgnoreCase)
                .Select(x => x.Trim()).Where(x => x.Length > 3).ToList();
            if (byVa.Count >= 2) return byVa;

            return new List<string> { text };
        }

        /// <summary>
        /// Trả lời số liệu chính xác từ DB — thân thiện, đủ thông tin, không cọc lốc.
        /// Null = để AI hội thoại xử lý.
        /// </summary>
        private async Task<string?> TryTraLoiChinhXacAsync(
            int maNguoiDung, string vaiTro, string ten, string cauHoi, CancellationToken ct)
        {
            var q = ChuanHoaKhongDau(cauHoi);
            var hoiVatTu = Co(q, "vat tu", "so luong vat", "dung bao nhieu vat", "bao nhieu vat tu", "may vat tu");
            var hoiBuoc = Co(q, "buoc", "quy trinh") && Co(q, "bao nhieu", "may", "so ", "co may", "duoc chon", "da chon");
            var hoiSoHs = Co(q, "ho so", "bao tri", "sua chua") && Co(q, "bao nhieu", "may", "so luong", "dem");
            var hoiHoanThanh = Co(q, "hoan thanh");
            var hoiGio = Co(q, "luc may", "may gio", "khi nao", "gio hoan thanh");
            var hoiTongTien = Co(q, "tong tien", "chi phi");

            if (hoiVatTu)
                return await TraLoiVatTuThanThienAsync(maNguoiDung, vaiTro, ten, q, ct);
            if (hoiBuoc)
                return await TraLoiBuocThanThienAsync(maNguoiDung, vaiTro, ten, q, ct);
            if (hoiGio || (hoiHoanThanh && Co(q, "luc", "gio", "ngay")))
                return await TraLoiGioHoanThanhThanThienAsync(ten, q, ct);
            if (hoiSoHs)
                return await TraLoiDemHsThanThienAsync(ten, q, ct);
            if (hoiTongTien && (vaiTro is "Giám đốc" or "Xưởng" or "Tổ trưởng sản xuất"))
                return await TraLoiTongTienThanThienAsync(ten, ct);

            return null;
        }

        private async Task<string> TraLoiVatTuThanThienAsync(
            int maNguoiDung, string vaiTro, string ten, string q, CancellationToken ct)
        {
            int? filterNv = null;
            if (vaiTro == "Nhân viên kỹ thuật")
            {
                filterNv = await _db.NhanViens.AsNoTracking()
                    .Where(n => n.MaNguoiDung == maNguoiDung)
                    .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
            }

            // Mã HS nếu có
            int? maBt = null, maSc = null;
            var mBt = Regex.Match(q, @"\bbt\s*#?\s*(\d+)\b");
            var mSc = Regex.Match(q, @"\bsc\s*#?\s*(\d+)\b");
            if (mBt.Success && int.TryParse(mBt.Groups[1].Value, out var idb)) maBt = idb;
            if (mSc.Success && int.TryParse(mSc.Groups[1].Value, out var ids)) maSc = ids;

            var query = _db.HoSoSuDungVatTus.AsNoTracking().AsQueryable();
            if (filterNv != null) query = query.Where(h => h.MaNhanVienTH == filterNv);
            if (maBt != null) query = query.Where(h => h.MaHoSoBaoTri == maBt);
            if (maSc != null) query = query.Where(h => h.MaHoSoSuaChua == maSc);
            // Ưu tiên bảo trì nếu câu hỏi nhắc BT
            if (Co(q, "bao tri") && maBt == null && maSc == null)
                query = query.Where(h => h.MaHoSoBaoTri != null);
            if (Co(q, "sua chua") && maBt == null && maSc == null)
                query = query.Where(h => h.MaHoSoSuaChua != null);

            var list = await query.OrderByDescending(h => h.NgayThucHien).Take(8)
                .Select(h => new
                {
                    h.MaHoSoBaoTri,
                    h.MaHoSoSuaChua,
                    h.TenThietBi,
                    h.TongTien,
                    h.TrangThai,
                    h.NgayThucHien,
                    ChiTiet = h.ChiTietSuDungVatTus.Select(c => new { c.TenVatTu, c.SoLuong, c.SoBuoc, c.DonGia }).ToList()
                }).ToListAsync(ct);

            if (list.Count == 0)
            {
                return $"{ten} ơi, hiện mình chưa thấy hồ sơ sử dụng vật tư nào"
                       + (filterNv != null ? " trong công việc của bạn" : " khớp câu hỏi")
                       + " trên hệ thống. Khi NVKT chọn vật tư và lưu quy trình, số liệu sẽ hiện ở đây nhé.";
            }

            // Gần nhất hoặc theo HS
            var focus = list[0];
            var hs = focus.MaHoSoBaoTri != null ? $"BT#{focus.MaHoSoBaoTri}" : $"SC#{focus.MaHoSoSuaChua}";
            var soLoai = focus.ChiTiet.Select(c => c.TenVatTu).Distinct().Count();
            var tongSl = focus.ChiTiet.Sum(c => c.SoLuong);
            var sb = new StringBuilder();
            sb.AppendLine($"{ten} ơi, với quy trình hồ sơ **{hs}** (thiết bị: {focus.TenThietBi}, ngày {focus.NgayThucHien:dd/MM/yyyy HH:mm}):");
            sb.AppendLine($"• Có **{soLoai} loại vật tư** được sử dụng, tổng số lượng **{tongSl}**, tổng tiền **{focus.TongTien:N0} ₫** (trạng thái phiếu: {focus.TrangThai}).");
            if (focus.ChiTiet.Count > 0)
            {
                sb.AppendLine("Chi tiết:");
                foreach (var c in focus.ChiTiet.OrderBy(x => x.SoBuoc).ThenBy(x => x.TenVatTu))
                    sb.AppendLine($"  – Bước {c.SoBuoc}: {c.TenVatTu} × {c.SoLuong} (đơn giá {c.DonGia:N0} ₫)");
            }
            else
                sb.AppendLine("Phiếu đã tạo nhưng chưa có dòng chi tiết vật tư.");

            if (list.Count > 1 && maBt == null && maSc == null)
            {
                sb.AppendLine();
                sb.AppendLine($"Ngoài ra còn {list.Count - 1} hồ sơ vật tư gần đây khác. Bạn muốn xem mã HS cụ thể (vd. BT#12) mình liệt kê đúng hồ sơ đó nhé.");
            }
            return sb.ToString().TrimEnd();
        }

        private async Task<string> TraLoiBuocThanThienAsync(
            int maNguoiDung, string vaiTro, string ten, string q, CancellationToken ct)
        {
            int? maBt = null, maSc = null;
            var mBt = Regex.Match(q, @"\bbt\s*#?\s*(\d+)\b");
            var mSc = Regex.Match(q, @"\bsc\s*#?\s*(\d+)\b");
            if (mBt.Success && int.TryParse(mBt.Groups[1].Value, out var idb)) maBt = idb;
            if (mSc.Success && int.TryParse(mSc.Groups[1].Value, out var ids)) maSc = ids;

            var query = _db.TienDoBuocQuyTrinhs.AsNoTracking().AsQueryable();
            if (vaiTro == "Nhân viên kỹ thuật")
            {
                var maNv = await _db.NhanViens.AsNoTracking()
                    .Where(n => n.MaNguoiDung == maNguoiDung).Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
                if (maNv != null)
                {
                    var pc = await _db.PhanCongCongViecs.AsNoTracking()
                        .Where(p => p.MaNhanVienThucHien == maNv)
                        .Select(p => new { p.MaHoSoBaoTri, p.MaHoSoSuaChua }).ToListAsync(ct);
                    var bts = pc.Where(x => x.MaHoSoBaoTri != null).Select(x => x.MaHoSoBaoTri!.Value).ToList();
                    var scs = pc.Where(x => x.MaHoSoSuaChua != null).Select(x => x.MaHoSoSuaChua!.Value).ToList();
                    query = query.Where(t =>
                        (t.MaHoSoBaoTri != null && bts.Contains(t.MaHoSoBaoTri.Value)) ||
                        (t.MaHoSoSuaChua != null && scs.Contains(t.MaHoSoSuaChua.Value)));
                }
            }
            if (maBt != null) query = query.Where(t => t.MaHoSoBaoTri == maBt);
            if (maSc != null) query = query.Where(t => t.MaHoSoSuaChua == maSc);
            if (Co(q, "bao tri") && maBt == null && maSc == null)
                query = query.Where(t => t.MaHoSoBaoTri != null);

            var rows = await query.OrderByDescending(t => t.ThoiDiemCapNhat).Take(300)
                .Select(t => new { t.MaHoSoBaoTri, t.MaHoSoSuaChua, t.SoBuoc, t.MoTaBuoc, t.TrangThai, t.ThoiDiemCapNhat })
                .ToListAsync(ct);

            if (rows.Count == 0)
                return $"{ten} ơi, hiện chưa có bước quy trình nào được ghi nhận"
                       + (maBt != null ? $" trên BT#{maBt}" : maSc != null ? $" trên SC#{maSc}" : "")
                       + ". Khi NVKT chọn và hoàn thành bước trên app, mình sẽ có số liệu ngay.";

            var g = rows.GroupBy(t => t.MaHoSoBaoTri != null ? $"BT#{t.MaHoSoBaoTri}" : $"SC#{t.MaHoSoSuaChua}")
                .OrderByDescending(x => x.Max(i => i.ThoiDiemCapNhat)).First();
            var uniq = g.GroupBy(x => x.SoBuoc).Select(x => x.OrderByDescending(i => i.ThoiDiemCapNhat).First())
                .OrderBy(x => x.SoBuoc).ToList();
            var xong = uniq.Count(x => x.TrangThai == "DaXong");
            var dang = uniq.Count(x => x.TrangThai == "DangLam");
            var sb = new StringBuilder();
            sb.AppendLine($"{ten} ơi, quy trình hồ sơ **{g.Key}** hiện có **{uniq.Count} bước** đã được chọn/ghi nhận trên hệ thống.");
            sb.AppendLine($"Trong đó **{xong} bước đã xong**, **{dang} bước đang làm**.");
            sb.AppendLine("Danh sách bước:");
            foreach (var b in uniq)
                sb.AppendLine($"  – Bước {b.SoBuoc}: {b.MoTaBuoc} ({b.TrangThai})");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> TraLoiGioHoanThanhThanThienAsync(string ten, string q, CancellationToken ct)
        {
            var uuBt = Co(q, "bao tri") || !Co(q, "sua chua");
            if (uuBt)
            {
                var h = await _db.HoSoBaoTris.AsNoTracking()
                    .Where(x => x.TrangThai == "Đã hoàn thành")
                    .OrderByDescending(x => x.ThoiDiemKetThucThucTe ?? x.NgayDuyet ?? x.NgayTao)
                    .Select(x => new
                    {
                        x.MaHoSoBaoTri,
                        Ten = x.MaThieBiNavigation != null ? x.MaThieBiNavigation.TenThietBi : "",
                        x.ThoiDiemBatDauThucTe,
                        x.ThoiDiemKetThucThucTe,
                        x.NgayDuyet,
                        x.NgayTao
                    }).FirstOrDefaultAsync(ct);
                if (h == null)
                    return $"{ten} ơi, hiện chưa có hồ sơ bảo trì nào ở trạng thái Đã hoàn thành trên hệ thống.";
                var kt = h.ThoiDiemKetThucThucTe?.ToString("dd/MM/yyyy HH:mm")
                         ?? h.NgayDuyet?.ToString("dd/MM/yyyy HH:mm")
                         ?? h.NgayTao.ToString("dd/MM/yyyy HH:mm");
                var bd = h.ThoiDiemBatDauThucTe?.ToString("dd/MM/yyyy HH:mm") ?? "chưa ghi";
                return $"{ten} ơi, hồ sơ bảo trì **BT#{h.MaHoSoBaoTri}** (thiết bị: {h.Ten}) đã hoàn thành.\n"
                       + $"• Bắt đầu thực tế: **{bd}**\n"
                       + $"• Hoàn thành: **{kt}**"
                       + (h.ThoiDiemKetThucThucTe == null ? " (theo ngày duyệt/tạo vì chưa ghi giờ kết thúc thực tế)." : ".");
            }
            else
            {
                var h = await _db.HoSoSuaChuas.AsNoTracking()
                    .Where(x => x.TrangThai == "Đã hoàn thành")
                    .OrderByDescending(x => x.ThoiDiemKetThucThucTe ?? x.NgayDuyet ?? x.NgayTao)
                    .Select(x => new
                    {
                        x.MaHoSoSuaChua,
                        Ten = x.MaThieBiNavigation != null ? x.MaThieBiNavigation.TenThietBi : "",
                        x.ThoiDiemBatDauThucTe,
                        x.ThoiDiemKetThucThucTe,
                        x.NgayDuyet,
                        x.NgayTao
                    }).FirstOrDefaultAsync(ct);
                if (h == null)
                    return $"{ten} ơi, hiện chưa có hồ sơ sửa chữa nào Đã hoàn thành.";
                var kt = h.ThoiDiemKetThucThucTe?.ToString("dd/MM/yyyy HH:mm")
                         ?? h.NgayDuyet?.ToString("dd/MM/yyyy HH:mm")
                         ?? h.NgayTao.ToString("dd/MM/yyyy HH:mm");
                return $"{ten} ơi, hồ sơ **SC#{h.MaHoSoSuaChua}** ({h.Ten}) hoàn thành lúc **{kt}**.";
            }
        }

        private async Task<string> TraLoiDemHsThanThienAsync(string ten, string q, CancellationToken ct)
        {
            var baoTri = Co(q, "bao tri") || !Co(q, "sua chua");
            if (baoTri)
            {
                var groups = await _db.HoSoBaoTris.AsNoTracking()
                    .GroupBy(h => h.TrangThai ?? "(trống)")
                    .Select(g => new { Tt = g.Key, So = g.Count() }).ToListAsync(ct);
                int Dem(string tt) => groups.FirstOrDefault(x => x.Tt == tt)?.So ?? 0;
                if (Co(q, "hoan thanh"))
                    return $"{ten} ơi, hiện có **{Dem("Đã hoàn thành")}** hồ sơ bảo trì ở trạng thái **Đã hoàn thành** (trên tổng **{groups.Sum(x => x.So)}** hồ sơ BT).";
                var sb = new StringBuilder();
                sb.AppendLine($"{ten} ơi, tổng số hồ sơ bảo trì trên hệ thống là **{groups.Sum(x => x.So)}**, chia theo trạng thái:");
                foreach (var g in groups.OrderByDescending(x => x.So))
                    sb.AppendLine($"• {g.Tt}: **{g.So}**");
                return sb.ToString().TrimEnd();
            }
            else
            {
                var groups = await _db.HoSoSuaChuas.AsNoTracking()
                    .GroupBy(h => h.TrangThai ?? "(trống)")
                    .Select(g => new { Tt = g.Key, So = g.Count() }).ToListAsync(ct);
                var sb = new StringBuilder();
                sb.AppendLine($"{ten} ơi, tổng hồ sơ sửa chữa: **{groups.Sum(x => x.So)}**.");
                foreach (var g in groups.OrderByDescending(x => x.So))
                    sb.AppendLine($"• {g.Tt}: **{g.So}**");
                return sb.ToString().TrimEnd();
            }
        }

        private async Task<string> TraLoiTongTienThanThienAsync(string ten, CancellationToken ct)
        {
            var now = DateTime.Now;
            var vt = await _db.HoSoSuDungVatTus.AsNoTracking()
                .Where(h => h.TrangThai == "Xác nhận" || h.TrangThai == "Đã duyệt")
                .Select(h => new { h.TongTien, h.NgayThucHien }).ToListAsync(ct);
            var thang = vt.Where(x => x.NgayThucHien.Year == now.Year && x.NgayThucHien.Month == now.Month).Sum(x => x.TongTien);
            var nam = vt.Where(x => x.NgayThucHien.Year == now.Year).Sum(x => x.TongTien);
            return $"{ten} ơi, tổng tiền vật tư **đã xác nhận** trên hệ thống:\n"
                   + $"• Tháng {now.Month}/{now.Year}: **{thang:N0} ₫**\n"
                   + $"• Cả năm {now.Year}: **{nam:N0} ₫**\n"
                   + $"• Mọi thời điểm: **{vt.Sum(x => x.TongTien):N0} ₫**";
        }

        private static string ChuanHoaKhongDau(string input)
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

        // ===================== NẠP DỮ LIỆU THEO VAI TRÒ =====================
        // ===================== NẠP DỮ LIỆU THEO VAI TRÒ =====================

        private async Task<string> NapDuLieuTheoVaiTroAsync(int maNguoiDung, string vaiTro, CancellationToken ct)
        {
            var sb = new StringBuilder();
            var now = DateTime.Now;
            sb.AppendLine($"Thời điểm hệ thống: {now:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine($"Vai trò đang hỗ trợ: {vaiTro}");
            sb.AppendLine();

            switch (vaiTro)
            {
                case "Nhân viên kỹ thuật":
                    await NapNvktAsync(sb, maNguoiDung, ct);
                    break;
                case "Tổ trưởng cơ điện":
                case "Tổ trưởng kỹ thuật":
                    await NapToTruongAsync(sb, maNguoiDung, ct);
                    break;
                case "Xưởng":
                case "Tổ trưởng sản xuất":
                    await NapXuongAsync(sb, ct);
                    break;
                case "Giám đốc":
                    await NapGiamDocAsync(sb, now, ct);
                    break;
                case "Admin hệ thống":
                    await NapAdminAsync(sb, ct);
                    break;
                default:
                    sb.AppendLine("Vai trò chưa có bộ dữ liệu chuyên biệt.");
                    break;
            }

            return sb.ToString();
        }

        private async Task NapDemTrangThaiHsAsync(StringBuilder sb, CancellationToken ct)
        {
            var bt = await _db.HoSoBaoTris.AsNoTracking()
                .GroupBy(h => h.TrangThai ?? "(trống)")
                .Select(g => new { Tt = g.Key, So = g.Count() })
                .ToListAsync(ct);
            sb.AppendLine($"[Hồ sơ bảo trì] Tổng: {bt.Sum(x => x.So)}");
            foreach (var g in bt.OrderByDescending(x => x.So))
                sb.AppendLine($"  - {g.Tt}: {g.So}");

            var sc = await _db.HoSoSuaChuas.AsNoTracking()
                .GroupBy(h => h.TrangThai ?? "(trống)")
                .Select(g => new { Tt = g.Key, So = g.Count() })
                .ToListAsync(ct);
            sb.AppendLine($"[Hồ sơ sửa chữa] Tổng: {sc.Sum(x => x.So)}");
            foreach (var g in sc.OrderByDescending(x => x.So))
                sb.AppendLine($"  - {g.Tt}: {g.So}");
        }

        private async Task NapTienDoBuocGanDayAsync(StringBuilder sb, IEnumerable<int>? chiBt, IEnumerable<int>? chiSc, int takeHs, CancellationToken ct)
        {
            var q = _db.TienDoBuocQuyTrinhs.AsNoTracking().AsQueryable();
            var btList = chiBt?.ToList();
            var scList = chiSc?.ToList();
            if (btList != null || scList != null)
            {
                btList ??= new List<int>();
                scList ??= new List<int>();
                q = q.Where(t =>
                    (t.MaHoSoBaoTri != null && btList.Contains(t.MaHoSoBaoTri.Value)) ||
                    (t.MaHoSoSuaChua != null && scList.Contains(t.MaHoSoSuaChua.Value)));
            }

            var rows = await q.OrderByDescending(t => t.ThoiDiemCapNhat).Take(400)
                .Select(t => new
                {
                    t.MaHoSoBaoTri,
                    t.MaHoSoSuaChua,
                    t.SoBuoc,
                    t.MoTaBuoc,
                    t.TrangThai,
                    t.TenNhanVien,
                    t.ThoiDiemCapNhat
                }).ToListAsync(ct);

            sb.AppendLine("[Tiến độ bước quy trình — bước đã chọn/ghi trên từng hồ sơ]");
            if (rows.Count == 0)
            {
                sb.AppendLine("  (Chưa có bước nào được ghi nhận)");
                return;
            }

            var groups = rows
                .GroupBy(t => t.MaHoSoBaoTri != null ? $"BT#{t.MaHoSoBaoTri}" : $"SC#{t.MaHoSoSuaChua}")
                .OrderByDescending(g => g.Max(x => x.ThoiDiemCapNhat))
                .Take(takeHs);

            foreach (var g in groups)
            {
                var uniq = g.GroupBy(x => x.SoBuoc)
                    .Select(x => x.OrderByDescending(i => i.ThoiDiemCapNhat).First())
                    .OrderBy(x => x.SoBuoc).ToList();
                var xong = uniq.Count(x => x.TrangThai == "DaXong");
                var dang = uniq.Count(x => x.TrangThai == "DangLam");
                sb.AppendLine($"  {g.Key}: {uniq.Count} bước đã chọn (xong={xong}, đang làm={dang})");
                foreach (var b in uniq)
                    sb.AppendLine($"    • Bước {b.SoBuoc}: {b.MoTaBuoc} | {b.TrangThai}"
                                  + (string.IsNullOrWhiteSpace(b.TenNhanVien) ? "" : $" | {b.TenNhanVien}"));
            }
        }

        private async Task NapHsHoanThanhChiTietAsync(StringBuilder sb, int take, CancellationToken ct)
        {
            var bt = await _db.HoSoBaoTris.AsNoTracking()
                .Where(h => h.TrangThai == "Đã hoàn thành")
                .OrderByDescending(h => h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao)
                .Take(take)
                .Select(h => new
                {
                    h.MaHoSoBaoTri,
                    Ten = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ("TB#" + h.MaThieBi),
                    h.ThoiDiemBatDauThucTe,
                    h.ThoiDiemKetThucThucTe,
                    h.NgayTao,
                    h.NgayDuyet
                }).ToListAsync(ct);

            sb.AppendLine("[HS bảo trì Đã hoàn thành — gần nhất]");
            if (bt.Count == 0) sb.AppendLine("  (không có)");
            foreach (var h in bt)
            {
                var kt = FmtGio(h.ThoiDiemKetThucThucTe, h.NgayDuyet, h.NgayTao);
                var bd = h.ThoiDiemBatDauThucTe?.ToString("dd/MM/yyyy HH:mm") ?? "chưa ghi";
                sb.AppendLine($"  BT#{h.MaHoSoBaoTri} | {h.Ten} | BĐ {bd} | HT {kt}");
            }

            var sc = await _db.HoSoSuaChuas.AsNoTracking()
                .Where(h => h.TrangThai == "Đã hoàn thành")
                .OrderByDescending(h => h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao)
                .Take(take)
                .Select(h => new
                {
                    h.MaHoSoSuaChua,
                    Ten = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ("TB#" + h.MaThieBi),
                    h.ThoiDiemBatDauThucTe,
                    h.ThoiDiemKetThucThucTe,
                    h.NgayTao,
                    h.NgayDuyet
                }).ToListAsync(ct);

            sb.AppendLine("[HS sửa chữa Đã hoàn thành — gần nhất]");
            if (sc.Count == 0) sb.AppendLine("  (không có)");
            foreach (var h in sc)
            {
                var kt = FmtGio(h.ThoiDiemKetThucThucTe, h.NgayDuyet, h.NgayTao);
                var bd = h.ThoiDiemBatDauThucTe?.ToString("dd/MM/yyyy HH:mm") ?? "chưa ghi";
                sb.AppendLine($"  SC#{h.MaHoSoSuaChua} | {h.Ten} | BĐ {bd} | HT {kt}");
            }
        }

        private async Task NapHsGanDayAsync(StringBuilder sb, int take, CancellationToken ct)
        {
            var bt = await _db.HoSoBaoTris.AsNoTracking()
                .OrderByDescending(h => h.NgayTao).Take(take)
                .Select(h => new
                {
                    h.MaHoSoBaoTri,
                    h.TrangThai,
                    Ten = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ("TB#" + h.MaThieBi),
                    h.NgayTao,
                    h.ThoiDiemBatDauThucTe,
                    h.ThoiDiemKetThucThucTe
                }).ToListAsync(ct);
            sb.AppendLine($"[HS bảo trì gần đây — {take} dòng]");
            foreach (var h in bt)
                sb.AppendLine($"  BT#{h.MaHoSoBaoTri} | {h.Ten} | {h.TrangThai} | tạo {h.NgayTao:dd/MM/yyyy HH:mm}"
                              + (h.ThoiDiemKetThucThucTe != null ? $" | HT {h.ThoiDiemKetThucThucTe:dd/MM/yyyy HH:mm}" : ""));

            var sc = await _db.HoSoSuaChuas.AsNoTracking()
                .OrderByDescending(h => h.NgayTao).Take(take)
                .Select(h => new
                {
                    h.MaHoSoSuaChua,
                    h.TrangThai,
                    Ten = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ("TB#" + h.MaThieBi),
                    h.NgayTao,
                    h.ThoiDiemKetThucThucTe
                }).ToListAsync(ct);
            sb.AppendLine($"[HS sửa chữa gần đây — {take} dòng]");
            foreach (var h in sc)
                sb.AppendLine($"  SC#{h.MaHoSoSuaChua} | {h.Ten} | {h.TrangThai} | tạo {h.NgayTao:dd/MM/yyyy HH:mm}"
                              + (h.ThoiDiemKetThucThucTe != null ? $" | HT {h.ThoiDiemKetThucThucTe:dd/MM/yyyy HH:mm}" : ""));
        }

        private async Task NapVatTuAsync(StringBuilder sb, int? chiMaNv, int take, CancellationToken ct)
        {
            var q = _db.HoSoSuDungVatTus.AsNoTracking().AsQueryable();
            if (chiMaNv != null)
                q = q.Where(h => h.MaNhanVienTH == chiMaNv);

            var list = await q.OrderByDescending(h => h.NgayThucHien).Take(take)
                .Select(h => new
                {
                    h.MaHoSoVatTu,
                    h.MaHoSoBaoTri,
                    h.MaHoSoSuaChua,
                    h.TenThietBi,
                    h.LoaiCongViec,
                    h.TongTien,
                    h.TrangThai,
                    h.NgayThucHien,
                    ChiTiet = h.ChiTietSuDungVatTus.Select(c => new
                    {
                        c.SoBuoc,
                        c.TenVatTu,
                        c.SoLuong,
                        c.DonGia
                    }).ToList()
                }).ToListAsync(ct);

            sb.AppendLine(chiMaNv != null
                ? "[Vật tư trong quy trình — CỦA NHÂN VIÊN NÀY]"
                : "[Vật tư sử dụng trong quy trình HS — chi tiết số lượng]");
            if (list.Count == 0) sb.AppendLine("  (không có hồ sơ vật tư)");
            foreach (var h in list)
            {
                var hs = h.MaHoSoBaoTri != null ? $"BT#{h.MaHoSoBaoTri}" : $"SC#{h.MaHoSoSuaChua}";
                var tongSl = h.ChiTiet.Sum(c => c.SoLuong);
                var soDongVt = h.ChiTiet.Count;
                var soLoaiVt = h.ChiTiet.Select(c => c.TenVatTu).Distinct().Count();
                sb.AppendLine($"  {hs} | {h.NgayThucHien:dd/MM/yyyy HH:mm} | {h.LoaiCongViec} | {h.TenThietBi}");
                sb.AppendLine($"    Tổng tiền: {h.TongTien:N0}₫ | TT: {h.TrangThai} | Số dòng VT: {soDongVt} | Số loại VT: {soLoaiVt} | Tổng SL: {tongSl}");
                foreach (var c in h.ChiTiet.OrderBy(x => x.SoBuoc).ThenBy(x => x.TenVatTu))
                    sb.AppendLine($"    • Bước {c.SoBuoc}: {c.TenVatTu} × {c.SoLuong} (đơn giá {c.DonGia:N0}₫)");
            }

            // Tổng hợp theo từng HS BT/SC (gộp nếu nhiều phiếu VT)
            sb.AppendLine("[Tổng hợp vật tư theo hồ sơ BT/SC]");
            var theoHs = list.GroupBy(h => h.MaHoSoBaoTri != null ? $"BT#{h.MaHoSoBaoTri}" : $"SC#{h.MaHoSoSuaChua}");
            foreach (var g in theoHs)
            {
                var allCt = g.SelectMany(x => x.ChiTiet).ToList();
                var tongSl = allCt.Sum(c => c.SoLuong);
                var soLoai = allCt.Select(c => c.TenVatTu).Distinct().Count();
                sb.AppendLine($"  {g.Key}: {soLoai} loại vật tư, tổng số lượng dùng = {tongSl}, tổng tiền phiếu = {g.Sum(x => x.TongTien):N0}₫");
            }
        }

        private async Task NapNvktAsync(StringBuilder sb, int maNguoiDung, CancellationToken ct)
        {
            sb.AppendLine("=== PHẠM VI: chỉ dữ liệu CÔNG VIỆC CỦA NHÂN VIÊN ĐANG ĐĂNG NHẬP ===");
            var maNv = await _db.NhanViens.AsNoTracking()
                .Where(n => n.MaNguoiDung == maNguoiDung)
                .Select(n => (int?)n.MaNhanVien)
                .FirstOrDefaultAsync(ct);
            if (maNv == null)
            {
                sb.AppendLine("Tài khoản chưa gắn mã nhân viên.");
                return;
            }
            sb.AppendLine($"MaNhanVien={maNv}");

            var pc = await _db.PhanCongCongViecs.AsNoTracking()
                .Where(p => p.MaNhanVienThucHien == maNv)
                .OrderByDescending(p => p.NgayPhanCong)
                .Take(40)
                .Select(p => new { p.MaHoSoBaoTri, p.MaHoSoSuaChua, p.TrangThai, p.NgayPhanCong })
                .ToListAsync(ct);
            sb.AppendLine($"[Phân công] số dòng gần đây: {pc.Count}");
            foreach (var p in pc)
            {
                var hs = p.MaHoSoBaoTri != null ? $"BT#{p.MaHoSoBaoTri}" : $"SC#{p.MaHoSoSuaChua}";
                sb.AppendLine($"  {hs} | {p.TrangThai} | {p.NgayPhanCong:dd/MM/yyyy HH:mm}");
            }

            var maBt = pc.Where(x => x.MaHoSoBaoTri != null).Select(x => x.MaHoSoBaoTri!.Value).Distinct().ToList();
            var maSc = pc.Where(x => x.MaHoSoSuaChua != null).Select(x => x.MaHoSoSuaChua!.Value).Distinct().ToList();

            if (maBt.Count > 0)
            {
                var bt = await _db.HoSoBaoTris.AsNoTracking().Where(h => maBt.Contains(h.MaHoSoBaoTri))
                    .Select(h => new
                    {
                        h.MaHoSoBaoTri,
                        h.TrangThai,
                        Ten = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "",
                        h.ThoiDiemBatDauThucTe,
                        h.ThoiDiemKetThucThucTe,
                        h.NgayTao
                    }).ToListAsync(ct);
                sb.AppendLine("[HS bảo trì được phân công]");
                foreach (var h in bt)
                    sb.AppendLine($"  BT#{h.MaHoSoBaoTri} | {h.Ten} | {h.TrangThai} | BĐ {h.ThoiDiemBatDauThucTe:dd/MM/yyyy HH:mm} | HT {h.ThoiDiemKetThucThucTe:dd/MM/yyyy HH:mm}");
            }
            if (maSc.Count > 0)
            {
                var sc = await _db.HoSoSuaChuas.AsNoTracking().Where(h => maSc.Contains(h.MaHoSoSuaChua))
                    .Select(h => new
                    {
                        h.MaHoSoSuaChua,
                        h.TrangThai,
                        Ten = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "",
                        h.ThoiDiemBatDauThucTe,
                        h.ThoiDiemKetThucThucTe
                    }).ToListAsync(ct);
                sb.AppendLine("[HS sửa chữa được phân công]");
                foreach (var h in sc)
                    sb.AppendLine($"  SC#{h.MaHoSoSuaChua} | {h.Ten} | {h.TrangThai} | BĐ {h.ThoiDiemBatDauThucTe:dd/MM/yyyy HH:mm} | HT {h.ThoiDiemKetThucThucTe:dd/MM/yyyy HH:mm}");
            }

            await NapTienDoBuocGanDayAsync(sb, maBt, maSc, 15, ct);
            await NapVatTuAsync(sb, maNv, 20, ct);

            sb.AppendLine("[Cách vận hành NVKT] Nhận việc → chi tiết HS → chọn/làm từng bước quy trình → vật tư → gửi xưởng xác nhận.");
        }

        private async Task NapToTruongAsync(StringBuilder sb, int maNguoiDung, CancellationToken ct)
        {
            sb.AppendLine("=== PHẠM VI: Tổ trưởng cơ điện — hồ sơ, thiết bị, kế hoạch, phân công, tiến độ bước ===");
            await NapDemTrangThaiHsAsync(sb, ct);
            await NapHsGanDayAsync(sb, 20, ct);
            await NapHsHoanThanhChiTietAsync(sb, 15, ct);
            await NapTienDoBuocGanDayAsync(sb, null, null, 15, ct);
            await NapVatTuAsync(sb, null, 15, ct);

            var maNv = await _db.NhanViens.AsNoTracking()
                .Where(n => n.MaNguoiDung == maNguoiDung)
                .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
            if (maNv != null)
            {
                var tao = await _db.HoSoBaoTris.AsNoTracking().Where(h => h.MaNhanVienTao == maNv)
                    .GroupBy(h => h.TrangThai ?? "").Select(g => new { Tt = g.Key, So = g.Count() }).ToListAsync(ct);
                sb.AppendLine($"[HS BT do tổ trưởng này tạo] tổng {tao.Sum(x => x.So)}");
                foreach (var g in tao) sb.AppendLine($"  - {g.Tt}: {g.So}");
            }

            var soTb = await _db.ThietBis.AsNoTracking().CountAsync(ct);
            sb.AppendLine($"[Thiết bị] số lượng: {soTb}");

            var lich = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.NgayDuKienBaoTri >= DateOnly.FromDateTime(DateTime.Now))
                .OrderBy(c => c.NgayDuKienBaoTri).Take(15)
                .Select(c => new
                {
                    c.NgayDuKienBaoTri,
                    Ten = c.MaThietBiNavigation != null ? c.MaThietBiNavigation.TenThietBi : ("TB#" + c.MaThietBi)
                }).ToListAsync(ct);
            sb.AppendLine("[Lịch BT dự kiến sắp tới]");
            if (lich.Count == 0) sb.AppendLine("  (chưa có)");
            foreach (var x in lich) sb.AppendLine($"  {x.NgayDuKienBaoTri:dd/MM/yyyy}: {x.Ten}");

            sb.AppendLine("[Vận hành] Kế hoạch → tạo HS BT → xưởng chốt ngày → GĐ duyệt → phân công NVKT.");
            sb.AppendLine("[Ràng buộc] 1 BT/tháng/TB; đang BT chưa xong thì chưa tạo SC; không 2 SC cùng TB trong 1 ngày.");
        }

        private async Task NapXuongAsync(StringBuilder sb, CancellationToken ct)
        {
            sb.AppendLine("=== PHẠM VI: Xưởng — chốt ngày, duyệt quy trình/vật tư, HS BT-SC ===");
            await NapDemTrangThaiHsAsync(sb, ct);
            await NapHsGanDayAsync(sb, 20, ct);
            await NapHsHoanThanhChiTietAsync(sb, 12, ct);
            await NapTienDoBuocGanDayAsync(sb, null, null, 15, ct);
            await NapVatTuAsync(sb, null, 20, ct);
            sb.AppendLine("[Vận hành] Chốt ngày BT → gửi GĐ; GĐ từ chối → sửa; xác nhận/từ chối quy trình NVKT; tạo/theo dõi SC.");
        }

        private async Task NapGiamDocAsync(StringBuilder sb, DateTime now, CancellationToken ct)
        {
            sb.AppendLine("=== PHẠM VI: Giám đốc — duyệt HS, thống kê, chi phí ===");
            await NapDemTrangThaiHsAsync(sb, ct);
            await NapHsHoanThanhChiTietAsync(sb, 12, ct);
            await NapTienDoBuocGanDayAsync(sb, null, null, 10, ct);

            var vt = await _db.HoSoSuDungVatTus.AsNoTracking()
                .Where(h => h.TrangThai == "Xác nhận" || h.TrangThai == "Đã duyệt")
                .Select(h => new { h.TongTien, h.NgayThucHien }).ToListAsync(ct);
            var thang = vt.Where(x => x.NgayThucHien.Year == now.Year && x.NgayThucHien.Month == now.Month).Sum(x => x.TongTien);
            var nam = vt.Where(x => x.NgayThucHien.Year == now.Year).Sum(x => x.TongTien);
            sb.AppendLine($"[Tổng tiền VT đã xác nhận] tháng {now.Month}/{now.Year}: {thang:N0}₫ | năm {now.Year}: {nam:N0}₫ | mọi lúc: {vt.Sum(x => x.TongTien):N0}₫");
            sb.AppendLine("[Vận hành] Duyệt/từ chối HS BT; xem thống kê số lượng & chi phí.");
        }

        private async Task NapAdminAsync(StringBuilder sb, CancellationToken ct)
        {
            sb.AppendLine("=== PHẠM VI: Admin — tài khoản, vai trò ===");
            sb.AppendLine($"Số tài khoản: {await _db.QuanLyNguoiDungs.AsNoTracking().CountAsync(ct)}");
            sb.AppendLine($"Số vai trò: {await _db.VaiTros.AsNoTracking().CountAsync(ct)}");
        }

        private static string FmtGio(DateTime? thucTe, DateTime? duyet, DateTime tao)
        {
            if (thucTe.HasValue) return thucTe.Value.ToString("dd/MM/yyyy HH:mm") + " (thực tế)";
            if (duyet.HasValue) return duyet.Value.ToString("dd/MM/yyyy HH:mm") + " (ngày duyệt)";
            return tao.ToString("dd/MM/yyyy HH:mm") + " (ngày tạo)";
        }

        // ===================== GỌI OLLAMA =====================

        private async Task<(bool ok, string? traLoi, string? loi)> GoiOllamaVoiDuLieuAsync(
            string vaiTro, string hoTen, string cauHoi, string boDuLieu,
            IReadOnlyList<(string role, string content)>? lichSu, CancellationToken ct)
        {
            var system = new StringBuilder();
            system.AppendLine("Bạn là OPC Bot — trợ lý thân thiện của app bảo trì & sửa chữa (dược OPC).");
            system.AppendLine($"User: {hoTen}. Vai trò: «{vaiTro}».");
            system.AppendLine("Xưng «mình», nói chuyện lịch sự, ấm áp, đầy đủ ý — không cọc lốc, không cục tính.");
            system.AppendLine();
            system.AppendLine("QUY TẮC:");
            system.AppendLine("1) Trả lời đúng chủ đề câu hỏi, đủ thông tin cần thiết (số liệu, tên HS, trạng thái…) — không thiếu ý.");
            system.AppendLine("2) Không nhồi chủ đề lạc đề; nhưng trong đúng chủ đề thì giải thích rõ ràng, dễ hiểu.");
            system.AppendLine("3) Mọi số liệu lấy từ DỮ LIỆU HỆ THỐNG — không bịa số.");
            system.AppendLine("4) CẤM bảo «vào app xem». Không có dữ liệu thì nói nhẹ nhàng chưa ghi nhận.");
            system.AppendLine("5) Phạm vi «" + vaiTro + "». Cấm lộ code/mật khẩu.");
            system.AppendLine();
            system.AppendLine("DỮ LIỆU HỆ THỐNG (realtime):");
            system.AppendLine(boDuLieu);

            var messages = new List<object> { new { role = "system", content = system.ToString() } };
            if (lichSu != null)
            {
                foreach (var (role, content) in lichSu.TakeLast(8))
                {
                    if (string.IsNullOrWhiteSpace(content)) continue;
                    messages.Add(new
                    {
                        role = role is "assistant" or "user" ? role : "user",
                        content = content.Trim()
                    });
                }
            }
            messages.Add(new
            {
                role = "user",
                content = "Câu hỏi: " + cauHoi.Trim()
                          + "\n\nChỉ trả lời đúng câu này, ngắn gọn, lấy số liệu từ DỮ LIỆU HỆ THỐNG. Không thêm thông tin ngoài chủ đề. Không bảo vào app xem."
            });

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
                        temperature = 0.2,
                        num_predict = 450
                    }
                };

                using var res = await client.PostAsJsonAsync($"{BaseUrl}/api/chat", payload, ct);
                var raw = await res.Content.ReadAsStringAsync(ct);
                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Ollama fail {Code}: {Body}", (int)res.StatusCode, raw);
                    return (false, null, "AI local chưa sẵn sàng. Kiểm tra Ollama đang chạy và đã tải model.");
                }

                using var doc = JsonDocument.Parse(raw);
                string? text = null;
                if (doc.RootElement.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var c))
                    text = c.GetString();

                if (string.IsNullOrWhiteSpace(text))
                    return (false, null, "AI không sinh được câu trả lời. Thử hỏi lại rõ hơn một chút.");

                text = LocCauTraLoiCam(text);
                return (true, text, null);
            }
            catch (TaskCanceledException)
            {
                return (false, null, "Câu trả lời hơi lâu (model local đang xử lý). Thử hỏi ngắn hơn hoặc đợi rồi gửi lại.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ollama error");
                return (false, null, "Không gọi được AI local. Mở Ollama rồi thử lại.");
            }
        }

        /// <summary>Chặn sót câu đẩy user vào app.</summary>
        private static string LocCauTraLoiCam(string text)
        {
            var cam = new[]
            {
                "vào app xem", "vao app xem", "mở app", "mo app", "trên giao diện app",
                "bạn hãy kiểm tra trên app", "tự kiểm tra trên hệ thống"
            };
            var lower = text.ToLowerInvariant();
            if (cam.Any(k => lower.Contains(k)))
            {
                return text + "\n\n(Mình xin lỗi nếu câu trên chưa đủ cụ thể — bạn hỏi lại chi tiết hơn, mình sẽ trả lời bằng đúng số liệu hệ thống.)";
            }
            return text.Trim();
        }

        private static bool LaCauHoiNhayCam(string cauHoi)
        {
            var t = cauHoi.ToLowerInvariant();
            t = Regex.Replace(t, @"\s+", " ");
            string[] cam =
            {
                "mật khẩu", "password", "connection string", "jwt", "secret key", "api key",
                "source code", "mã nguồn", "appsettings", "private key", "ngrok token"
            };
            return cam.Any(k => t.Contains(k));
        }
    }
}
