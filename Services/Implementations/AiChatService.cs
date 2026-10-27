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
    /// OPC Bot.
    /// 1) Vai trò lấy từ JWT; mỗi vai trò chỉ được hỏi/đọc đúng chủ đề của vai trò (QuyenChuDe).
    /// 2) Câu hỏi về SỐ LIỆU được tính trực tiếp từ DB bằng code (cùng công thức màn hình app) —
    ///    LLM không tự đếm. LLM chỉ dùng cho câu hỏi giải thích/quy trình, với context gọn.
    /// 3) Người dùng báo sai → tính lại từ DB, hiểu lại ý, ghi nhớ cách hiểu (không ghi nhớ con số).
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
            ChatbotBaiHocStore.DatDuongDan(config["Chatbot:BaiHocPath"]);
        }

        private string? GroqKey => _config["Groq:ApiKey"]?.Trim();

        /// <summary>Model Groq (cấu hình Groq:Model). Mặc định giữ như bản cũ.</summary>
        private string GroqModel =>
            string.IsNullOrWhiteSpace(_config["Groq:Model"])
                ? "qwen/qwen3.8-27b"
                : _config["Groq:Model"]!.Trim();

        public Task<(bool online, string? model, string? loi)> KiemTraAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(GroqKey))
                return Task.FromResult<(bool, string?, string?)>((true, "OPC-TraDB",
                    "Chưa cấu hình Groq:ApiKey — số liệu vẫn tra trực tiếp DB, chỉ câu hỏi giải thích là bị hạn chế."));
            return Task.FromResult<(bool, string?, string?)>((true, GroqModel, null));
        }

        // =====================================================================
        //  KIỂU DỮ LIỆU
        // =====================================================================

        private enum VaiTroKey { Khac, ToTruong, Xuong, GiamDoc, Nvkt }

        private enum ChuDeHoi { Khac, HoSoBt, HoSoSc, VatTu, PhanCong, ThietBi, LichBt, Buoc }

        /// <summary>
        /// PHẠM VI CHỦ ĐỀ THEO VAI TRÒ — sửa ở đây nếu muốn mở/đóng quyền hỏi.
        /// NVKT luôn bị giới hạn trong dữ liệu gắn phân công của chính mình.
        /// </summary>
        private static readonly Dictionary<VaiTroKey, ChuDeHoi[]> QuyenChuDe = new()
        {
            [VaiTroKey.ToTruong] = new[] { ChuDeHoi.HoSoBt, ChuDeHoi.HoSoSc, ChuDeHoi.VatTu, ChuDeHoi.PhanCong, ChuDeHoi.ThietBi, ChuDeHoi.LichBt, ChuDeHoi.Buoc },
            [VaiTroKey.Xuong] = new[] { ChuDeHoi.HoSoBt, ChuDeHoi.HoSoSc, ChuDeHoi.VatTu, ChuDeHoi.ThietBi, ChuDeHoi.LichBt, ChuDeHoi.Buoc },
            [VaiTroKey.GiamDoc] = new[] { ChuDeHoi.HoSoBt, ChuDeHoi.HoSoSc, ChuDeHoi.VatTu, ChuDeHoi.ThietBi, ChuDeHoi.LichBt, ChuDeHoi.Buoc },
            [VaiTroKey.Nvkt] = new[] { ChuDeHoi.HoSoBt, ChuDeHoi.HoSoSc, ChuDeHoi.VatTu, ChuDeHoi.PhanCong, ChuDeHoi.Buoc },
            [VaiTroKey.Khac] = Array.Empty<ChuDeHoi>()
        };

        /// <summary>Cách hiểu một câu hỏi (kết quả phân tích) — đây là thứ được "học", không phải con số.</summary>
        private sealed class YeuCau
        {
            public ChuDeHoi ChuDe { get; set; } = ChuDeHoi.Khac;
            public bool CaBtVaSc { get; set; }
            /// <summary>Khóa trạng thái chuẩn hóa (TtKey / VtKey), null = không nêu.</summary>
            public string? TrangThai { get; set; }
            public int? Ngay { get; set; }
            public int? Thang { get; set; }
            public int? Nam { get; set; }
            public bool TatCa { get; set; }
            public int? MaBt { get; set; }
            public int? MaSc { get; set; }
            public string? TenThietBi { get; set; }
            public string? TinhTrangTb { get; set; }
            /// <summary>"app" = BT tính theo tháng kế hoạch (như thống kê app); "thuc_te" = theo ngày hoàn thành thực tế.</summary>
            public string CoSo { get; set; } = "app";
            public bool CoKy => Thang != null || Ngay != null || Nam != null;
            public YeuCau Clone() => (YeuCau)MemberwiseClone();
        }

        private sealed record HsRow(int Ma, string TenTb, string? TrangThai, DateTime NgayHt, DateOnly? NgayKh);

        private sealed class KetQuaHs
        {
            public List<HsRow> List { get; set; } = new();
            public Dictionary<string, int> TheoTt { get; set; } = new();
            public string? TtChinh { get; set; }
            public YeuCau Y { get; set; } = new();
            public string? GhiChuKy { get; set; }
        }

        private sealed class PhamViNv
        {
            public int? MaNv { get; set; }
            public HashSet<int> Bt { get; set; } = new();
            public HashSet<int> Sc { get; set; } = new();
        }

        // =====================================================================
        //  ĐIỂM VÀO
        // =====================================================================

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
            var vk = LayVaiTroKey(vt);
            var q = Nghia(cauHoi);

            if (LaNhayCam(q))
                return (true, "Dạ, mình không chia sẻ thông tin bảo mật hệ thống.", null);

            try
            {
                // 1) Người dùng báo sai → tính lại + hiểu lại + ghi nhớ
                if (LaBaoCauTraLoiSai(q))
                    return (true, await XuLySuaSaiAsync(maNguoiDung, vk, vt, ten, cauHoi.Trim(), lichSu, ct), null);

                // 2) Chặn yêu cầu vượt quyền vai trò
                var vuot = TuChoiVuotVaiTro(vk, q);
                if (vuot != null) return (true, vuot, null);

                // 3) Câu hỏi số liệu/danh sách → tra DB trực tiếp (luôn dữ liệu mới nhất)
                var y = XacDinhYeuCau(vk, q, lichSu);
                if (y.ChuDe != ChuDeHoi.Khac)
                    return (true, await TraLoiTheoYeuCauAsync(maNguoiDung, vk, vt, y, ct), null);

                // 4) Câu hỏi giải thích / quy trình → LLM với context gọn
                if (!string.IsNullOrEmpty(GroqKey))
                {
                    var context = await NapContextGonAsync(maNguoiDung, vk, vt, ct);
                    var (ok, text, err) = await GoiGroqAsync(vk, vt, ten, cauHoi.Trim(), context, lichSu, ct);
                    if (ok && !string.IsNullOrWhiteSpace(text))
                        return (true, text.Trim(), null);
                    _logger.LogWarning("Groq lỗi: {Err}", err);
                }

                return (true, MauCauHoi(vk, vt), null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chatbot lỗi");
                return (false, null, "Lỗi khi xử lý câu hỏi. Thử lại giúp mình.");
            }
        }

        // =====================================================================
        //  PHÂN TÍCH CÂU HỎI → YeuCau
        // =====================================================================

        private static YeuCau PhanTich(string q)
        {
            var y = new YeuCau();
            int? ngay = null, thang = null, nam = null;
            TrichNgayThangNam(q, ref ngay, ref thang, ref nam);
            y.Ngay = ngay; y.Thang = thang; y.Nam = nam;
            XuLyThoiGianTuongDoi(q, y);

            y.MaBt = TrichMa(q, "bt");
            y.MaSc = TrichMa(q, "sc");
            y.TatCa = Co(q, "tat ca", "toan bo", "tu truoc den nay", "luy ke", "tong so", "tong cong");
            y.CoSo = Co(q, "thuc te", "ngay hoan thanh") ? "thuc_te" : "app";

            var bt = Co(q, "bao tri");
            var sc = Co(q, "sua chua");
            var coHs = Co(q, "ho so");

            if (LaHoiVatTu(q))
            {
                y.ChuDe = ChuDeHoi.VatTu;
                y.TrangThai = TrichTrangThai(q, true);
                return y;
            }
            if (LaHoiPhanCong(q)) { y.ChuDe = ChuDeHoi.PhanCong; return y; }
            if (LaHoiLichBaoTri(q)) { y.ChuDe = ChuDeHoi.LichBt; return y; }
            if (Co(q, "buoc") && (y.MaBt != null || y.MaSc != null || Co(q, "quy trinh")))
            {
                y.ChuDe = ChuDeHoi.Buoc;
                return y;
            }

            y.TrangThai = TrichTrangThai(q, false);
            var hoiDem = Co(q, "bao nhieu", "co may", "tong", "hoan thanh", "cho duyet", "dang thuc hien",
                "tu choi", "cho xuong", "cho phan cong", "da duyet", "cho xac nhan");
            var tbThuan = Co(q, "thiet bi", "may moc", "danh sach may", "may nen")
                          && !coHs && !y.CoKy
                          && !Co(q, "hoan thanh", "da bao tri", "da sua chua");

            if (tbThuan)
            {
                y.ChuDe = ChuDeHoi.ThietBi;
                y.TenThietBi = TrichTenThietBi(q);
                if (Co(q, "dang sua chua")) y.TinhTrangTb = "Sửa chữa";
                else if (Co(q, "dang bao tri")) y.TinhTrangTb = "Bảo trì";
                else if (Co(q, "dang san xuat")) y.TinhTrangTb = "Sản xuất";
            }
            else if ((bt || sc) && (coHs || hoiDem || y.CoKy))
            {
                if (bt && sc) { y.ChuDe = ChuDeHoi.HoSoBt; y.CaBtVaSc = true; }
                else if (sc) y.ChuDe = ChuDeHoi.HoSoSc;
                else y.ChuDe = ChuDeHoi.HoSoBt;
            }
            else if (y.MaBt != null) y.ChuDe = ChuDeHoi.HoSoBt;
            else if (y.MaSc != null) y.ChuDe = ChuDeHoi.HoSoSc;
            else if (coHs && (hoiDem || y.CoKy || y.TrangThai != null))
            {
                y.ChuDe = ChuDeHoi.HoSoBt;
                y.CaBtVaSc = true;
            }
            return y;
        }

        /// <summary>Phân tích + nối tiếp ngữ cảnh hội thoại + áp dụng bài học đã học.</summary>
        private YeuCau XacDinhYeuCau(VaiTroKey vk, string q, IReadOnlyList<(string role, string content)>? lichSu)
        {
            var y = PhanTich(q);

            // Câu hỏi nối tiếp ("còn tháng 9?", "còn sửa chữa thì sao?") → kế thừa chủ đề câu trước
            if (y.ChuDe == ChuDeHoi.Khac && q.Length <= 70)
            {
                var noiTiep = y.CoKy || y.TrangThai != null || y.TatCa
                              || Co(q, "con ", "vay ", "the con", "so voi");
                var cauTruoc = noiTiep ? TrichCauHoiTruoc(lichSu) : null;
                if (cauTruoc != null)
                {
                    var prev = PhanTich(Nghia(cauTruoc));
                    if (prev.ChuDe != ChuDeHoi.Khac)
                    {
                        var m = prev.Clone();
                        if (y.CoKy) { m.Ngay = y.Ngay; m.Thang = y.Thang; m.Nam = y.Nam; }
                        if (y.TrangThai != null) m.TrangThai = y.TrangThai;
                        if (y.TatCa) m.TatCa = true;
                        if (m.ChuDe is ChuDeHoi.HoSoBt or ChuDeHoi.HoSoSc)
                        {
                            var bt = Co(q, "bao tri");
                            var sc = Co(q, "sua chua");
                            if (bt && sc) { m.ChuDe = ChuDeHoi.HoSoBt; m.CaBtVaSc = true; }
                            else if (sc) { m.ChuDe = ChuDeHoi.HoSoSc; m.CaBtVaSc = false; }
                            else if (bt) { m.ChuDe = ChuDeHoi.HoSoBt; m.CaBtVaSc = false; }
                        }
                        y = m;
                    }
                }
            }

            ApDungBaiHoc(vk, q, y);
            return y;
        }

        private static string? TrichTrangThai(string q, bool vatTu)
        {
            if (vatTu)
            {
                if (Co(q, "cho gui")) return "cho gui";
                if (Co(q, "cho xac nhan", "cho duyet", "da gui")) return "cho xac nhan";
                if (Co(q, "xac nhan", "da xem")) return "xac nhan";
                if (Co(q, "tu choi")) return "tu choi";
                return null;
            }
            if (Co(q, "hoan thanh", "da bao tri", "da sua chua", "thiet bi da")) return "hoan thanh";
            if (Co(q, "cho xac nhan")) return "cho xac nhan";
            if (Co(q, "cho duyet", "cho gd")) return "cho duyet";
            if (Co(q, "dang thuc hien")) return "dang thuc hien";
            if (Co(q, "tu choi")) return "tu choi";
            if (Co(q, "cho xuong", "chot ngay")) return "cho xuong";
            if (Co(q, "cho phan cong", "da duyet")) return "da duyet";
            return null;
        }

        /// <summary>Khóa trạng thái HS bảo trì/sửa chữa (chịu được khác biệt «Đã hoàn thành»/«Hoàn thành», hoa/thường…).</summary>
        private static string TtKey(string? t)
        {
            var n = ChuanHoa(t ?? "");
            if (n.Contains("hoan thanh")) return "hoan thanh";
            if (n.Contains("tu choi")) return "tu choi";
            if (n.Contains("cho xuong") || n.Contains("cho chot ngay")) return "cho xuong";
            if (n.Contains("cho phan cong") || n == "da duyet" || n.Contains("phe duyet")) return "da duyet";
            if (n.Contains("cho xac nhan")) return "cho xac nhan";
            if (n.Contains("cho duyet") || n.Contains("cho gd")) return "cho duyet";
            if (n.Contains("dang thuc hien")) return "dang thuc hien";
            if (n.Contains("huy")) return "da huy";
            if (n == "nhap") return "nhap";
            return n.Length == 0 ? "(trong)" : n;
        }

        /// <summary>Khóa trạng thái hồ sơ vật tư: Chờ gửi | Chờ xác nhận | Xác nhận (+ alias cũ).</summary>
        private static string VtKey(string? t)
        {
            var n = ChuanHoa(t ?? "");
            if (n.Contains("cho gui")) return "cho gui";
            if (n.Contains("cho xac nhan") || n.Contains("cho duyet") || n.Contains("da gui")) return "cho xac nhan";
            if (n.Contains("tu choi")) return "tu choi";
            if (n.Contains("xac nhan") || n == "da xem") return "xac nhan";
            return n.Length == 0 ? "(trong)" : n;
        }

        private static readonly Dictionary<string, string> NhanTtMap = new()
        {
            ["hoan thanh"] = "Đã hoàn thành",
            ["cho duyet"] = "Chờ duyệt",
            ["dang thuc hien"] = "Đang thực hiện",
            ["tu choi"] = "Từ chối",
            ["cho xuong"] = "Chờ xưởng",
            ["da duyet"] = "Đã duyệt (chờ phân công)",
            ["cho xac nhan"] = "Chờ xác nhận",
            ["cho gui"] = "Chờ gửi",
            ["xac nhan"] = "Xác nhận",
            ["da huy"] = "Đã hủy",
            ["nhap"] = "Nháp"
        };

        private static string NhanTt(string key) => NhanTtMap.TryGetValue(key, out var v) ? v : key;

        private static bool DaDong(string ttKey) => ttKey is "hoan thanh" or "tu choi" or "da huy";

        // =====================================================================
        //  ĐỊNH TUYẾN → TRẢ LỜI TỪ DB
        // =====================================================================

        private async Task<string> TraLoiTheoYeuCauAsync(
            int maNd, VaiTroKey vk, string vt, YeuCau y, CancellationToken ct)
        {
            if (!QuyenChuDe[vk].Contains(y.ChuDe))
                return $"Dạ, nội dung này nằm ngoài phạm vi vai trò «{vt}». Bạn có thể hỏi về: {MoTaViecDuocHoi(vk)}.";

            switch (y.ChuDe)
            {
                case ChuDeHoi.HoSoBt:
                case ChuDeHoi.HoSoSc:
                    return await TraLoiHoSoAsync(maNd, vk, y, ct);
                case ChuDeHoi.VatTu:
                    return await TraLoiVatTuAsync(maNd, vk, y, ct);
                case ChuDeHoi.PhanCong:
                    return await TraLoiPhanCongAsync(maNd, vk, y, ct);
                case ChuDeHoi.ThietBi:
                    return await TraLoiThietBiAsync(y, ct);
                case ChuDeHoi.LichBt:
                    return await TraLoiLichAsync(ct);
                case ChuDeHoi.Buoc:
                    return await TraLoiBuocAsync(maNd, vk, y, ct);
                default:
                    return MauCauHoi(vk, vt);
            }
        }

        // ---------- Phạm vi riêng của NVKT ----------

        private async Task<PhamViNv> LayPhamViNvAsync(int maNd, CancellationToken ct)
        {
            var pv = new PhamViNv();
            pv.MaNv = await _db.NhanViens.AsNoTracking()
                .Where(n => n.MaNguoiDung == maNd)
                .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
            if (pv.MaNv == null) return pv;

            var maNv = pv.MaNv;
            var pc = await _db.PhanCongCongViecs.AsNoTracking()
                .Where(p => p.MaNhanVienThucHien == maNv)
                .Select(p => new { p.MaHoSoBaoTri, p.MaHoSoSuaChua })
                .ToListAsync(ct);
            pv.Bt = pc.Where(x => x.MaHoSoBaoTri != null).Select(x => x.MaHoSoBaoTri!.Value).ToHashSet();
            pv.Sc = pc.Where(x => x.MaHoSoSuaChua != null).Select(x => x.MaHoSoSuaChua!.Value).ToHashSet();
            return pv;
        }

        // ---------- Hồ sơ bảo trì / sửa chữa ----------

        /// <summary>
        /// Nguồn dữ liệu HS duy nhất (cùng công thức thống kê app):
        /// BT: ngày = ngày kế hoạch (ChiTietKeHoachBaoTri) nếu có, không thì ngày hoàn thành/duyệt/tạo.
        /// SC: ngày = hoàn thành thực tế ?? ngày duyệt ?? ngày tạo.
        /// </summary>
        private async Task<List<HsRow>> LayHsAsync(bool laBt, HashSet<int>? gioiHan, CancellationToken ct)
        {
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

                return raw
                    .Where(h => gioiHan == null || gioiHan.Contains(h.MaHoSoBaoTri))
                    .Select(h => new HsRow(
                        h.MaHoSoBaoTri,
                        h.TenTb ?? "",
                        h.TrangThai,
                        h.NgayHt,
                        dictKh.TryGetValue(h.MaHoSoBaoTri, out var nk) ? nk : (DateOnly?)null))
                    .ToList();
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
                return raw
                    .Where(h => gioiHan == null || gioiHan.Contains(h.MaHoSoSuaChua))
                    .Select(h => new HsRow(h.MaHoSoSuaChua, h.TenTb ?? "", h.TrangThai, h.NgayHt, null))
                    .ToList();
            }
        }

        private static DateTime NgayApp(HsRow r, bool laBt, string coSo) =>
            laBt && coSo != "thuc_te" && r.NgayKh != null
                ? r.NgayKh.Value.ToDateTime(TimeOnly.MinValue)
                : r.NgayHt;

        /// <summary>Đọc ngày từ DateTime / DateTime? / DateOnly / DateOnly? (không phụ thuộc kiểu cột).</summary>
        private static DateTime LayNgay(object? o) => o switch
        {
            DateTime dt => dt,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            DateTimeOffset dto => dto.DateTime,
            _ => DateTime.MinValue
        };

        private static bool TrongKy(DateTime d, YeuCau y)
        {
            if (!y.CoKy) return true;
            if (d.Year != (y.Nam ?? DateTime.Now.Year)) return false;
            if (y.Thang != null && d.Month != y.Thang) return false;
            if (y.Ngay != null && d.Day != y.Ngay) return false;
            return true;
        }

        /// <summary>
        /// Hạn chế dữ liệu cũ: câu hỏi về «hoàn thành/từ chối/không nêu trạng thái» mà không nêu thời gian
        /// → mặc định THÁNG HIỆN TẠI. Trạng thái đang mở (chờ duyệt, đang thực hiện…) là hàng đợi hiện tại → không giới hạn.
        /// </summary>
        private static string? ChuanBiKy(YeuCau y)
        {
            if (y.CoKy) { y.Nam ??= DateTime.Now.Year; return null; }
            if (y.TatCa) return null;
            if (y.TrangThai == null || y.TrangThai is "hoan thanh" or "tu choi")
            {
                y.Thang = DateTime.Now.Month;
                y.Nam = DateTime.Now.Year;
                return "(Mặc định tháng hiện tại — nêu tháng/năm khác hoặc nói «tất cả» nếu bạn cần.)";
            }
            return null;
        }

        private static KetQuaHs TinhHs(List<HsRow> rows, bool laBt, YeuCau y)
        {
            var yy = y.Clone();
            var kq = new KetQuaHs { Y = yy, GhiChuKy = ChuanBiKy(yy) };
            var ky = rows.Where(r => TrongKy(NgayApp(r, laBt, yy.CoSo), yy)).ToList();
            kq.TheoTt = ky.GroupBy(r => TtKey(r.TrangThai)).ToDictionary(g => g.Key, g => g.Count());
            // Hỏi theo tháng mà không nêu trạng thái → số chính là «Đã hoàn thành» (trùng biểu đồ thống kê app)
            kq.TtChinh = yy.TrangThai ?? (yy.TatCa ? null : "hoan thanh");
            var loc = kq.TtChinh == null ? ky : ky.Where(r => TtKey(r.TrangThai) == kq.TtChinh).ToList();
            kq.List = loc.OrderByDescending(r => NgayApp(r, laBt, yy.CoSo)).ToList();
            return kq;
        }

        private async Task<string> TraLoiHoSoAsync(int maNd, VaiTroKey vk, YeuCau y, CancellationToken ct)
        {
            PhamViNv? pv = null;
            if (vk == VaiTroKey.Nvkt)
            {
                pv = await LayPhamViNvAsync(maNd, ct);
                if (pv.MaNv == null) return "Dạ, không xác định được nhân viên.";
            }

            var parts = new List<string>();
            if (y.ChuDe == ChuDeHoi.HoSoBt || y.CaBtVaSc)
                parts.Add(DinhDangHs(true, y, await LayHsAsync(true, pv?.Bt, ct)));
            if (y.ChuDe == ChuDeHoi.HoSoSc || y.CaBtVaSc)
                parts.Add(DinhDangHs(false, y, await LayHsAsync(false, pv?.Sc, ct)));
            return string.Join("\n\n", parts);
        }

        private static string DinhDangHs(bool laBt, YeuCau y, List<HsRow> rows)
        {
            var tien = laBt ? "BT" : "SC";
            var loai = laBt ? "bảo trì" : "sửa chữa";

            // Hỏi 1 hồ sơ cụ thể
            var ma = laBt ? y.MaBt : y.MaSc;
            if (ma != null)
            {
                var r = rows.FirstOrDefault(x => x.Ma == ma);
                if (r == null) return $"Dạ, không thấy hồ sơ {tien}#{ma} trong phạm vi của bạn.";
                return $"Dạ, {tien}#{r.Ma}: {r.TenTb} — trạng thái **{r.TrangThai}** — ngày {NgayApp(r, laBt, y.CoSo):dd/MM/yyyy}.";
            }

            var kq = TinhHs(rows, laBt, y);
            var yy = kq.Y;
            var nhan = kq.TtChinh == null ? "tất cả trạng thái" : NhanTt(kq.TtChinh);
            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, {FormatKy(yy)} có **{kq.List.Count}** hồ sơ {loai} **{nhan}**.");
            foreach (var h in kq.List.Take(12))
                sb.AppendLine($"• {tien}#{h.Ma} | {h.TenTb} | {h.TrangThai} | {NgayApp(h, laBt, yy.CoSo):dd/MM/yyyy}");
            if (kq.List.Count > 12)
                sb.AppendLine($"… và {kq.List.Count - 12} hồ sơ khác.");

            if (yy.TrangThai == null && kq.TheoTt.Count > 0)
            {
                var khac = kq.TheoTt.Where(kv => kv.Key != kq.TtChinh)
                    .Select(kv => $"{NhanTt(kv.Key)} {kv.Value}").ToList();
                if (khac.Count > 0)
                    sb.AppendLine("Trạng thái khác cùng kỳ: " + string.Join(" | ", khac));
            }
            if (yy.CoKy)
                sb.AppendLine(laBt && yy.CoSo != "thuc_te"
                    ? "(Tính theo tháng kế hoạch bảo trì — trùng thống kê trên app.)"
                    : "(Tính theo ngày hoàn thành thực tế.)");
            if (kq.GhiChuKy != null) sb.AppendLine(kq.GhiChuKy);
            return sb.ToString().TrimEnd();
        }

        private static string FormatKy(YeuCau y)
        {
            var nam = y.Nam ?? DateTime.Now.Year;
            if (y.Ngay != null && y.Thang != null) return $"ngày {y.Ngay:00}/{y.Thang:00}/{nam}";
            if (y.Thang != null) return $"tháng {y.Thang}/{nam}";
            if (y.Nam != null) return $"năm {y.Nam}";
            return "toàn hệ thống";
        }

        // ---------- Hồ sơ vật tư ----------

        private async Task<string> TraLoiVatTuAsync(int maNd, VaiTroKey vk, YeuCau y, CancellationToken ct)
        {
            var q = _db.HoSoSuDungVatTus.AsNoTracking().AsQueryable();
            if (vk == VaiTroKey.Nvkt)
            {
                var pv = await LayPhamViNvAsync(maNd, ct);
                if (pv.MaNv == null) return "Dạ, không xác định được nhân viên.";
                var maNv = pv.MaNv;
                q = q.Where(h => h.MaNhanVienTH == maNv);
            }
            var maBt = y.MaBt;
            var maSc = y.MaSc;
            if (maBt != null) q = q.Where(h => h.MaHoSoBaoTri == maBt);
            if (maSc != null) q = q.Where(h => h.MaHoSoSuaChua == maSc);

            var raw = await q.OrderByDescending(h => h.NgayThucHien)
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

            var kyList = raw.Where(x => !y.CoKy || TrongKy(LayNgay(x.NgayThucHien), y)).ToList();
            var theoTt = kyList.GroupBy(x => VtKey(x.TrangThai)).ToDictionary(g => g.Key, g => g.Count());
            var list = string.IsNullOrEmpty(y.TrangThai)
                ? kyList
                : kyList.Where(x => VtKey(x.TrangThai) == y.TrangThai).ToList();

            var label = string.IsNullOrEmpty(y.TrangThai) ? "tất cả trạng thái" : NhanTt(y.TrangThai);
            var pham = vk == VaiTroKey.Nvkt ? " của bạn" : "";
            var sb = new StringBuilder();
            if (list.Count == 0)
            {
                sb.AppendLine($"Dạ, {(y.CoKy ? FormatKy(y) + " " : "")}chưa có hồ sơ vật tư{pham} **{label}**.");
                if (theoTt.Count > 0)
                    sb.AppendLine("Hiện có: " + string.Join(" | ", theoTt.Select(kv => $"{NhanTt(kv.Key)} {kv.Value}")));
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine($"Dạ, {(y.CoKy ? FormatKy(y) + " " : "")}có **{list.Count}** hồ sơ vật tư{pham} (**{label}**).");
            if (string.IsNullOrEmpty(y.TrangThai) && theoTt.Count > 0)
                sb.AppendLine("Theo trạng thái: " + string.Join(" | ", theoTt.Select(kv => $"{NhanTt(kv.Key)} {kv.Value}")));
            foreach (var x in list.Take(15))
            {
                var k = x.MaHoSoBaoTri != null ? $"BT#{x.MaHoSoBaoTri}"
                    : (x.MaHoSoSuaChua != null ? $"SC#{x.MaHoSoSuaChua}" : $"VT#{x.MaHoSoVatTu}");
                sb.AppendLine($"• {k} | {x.TenTb} | SL {x.TongSl} | {x.TongTien:N0} ₫ | {x.TrangThai} | {x.NgayThucHien:dd/MM/yyyy}");
            }
            if (list.Count > 15) sb.AppendLine($"… và {list.Count - 15} hồ sơ khác.");
            return sb.ToString().TrimEnd();
        }

        // ---------- Phân công ----------

        private async Task<string> TraLoiPhanCongAsync(int maNd, VaiTroKey vk, YeuCau y, CancellationToken ct)
        {
            var q = _db.PhanCongCongViecs.AsNoTracking().AsQueryable();
            var laNv = vk == VaiTroKey.Nvkt;
            if (laNv)
            {
                var pv = await LayPhamViNvAsync(maNd, ct);
                if (pv.MaNv == null) return "Dạ, không xác định được nhân viên.";
                var maNv = pv.MaNv;
                q = q.Where(p => p.MaNhanVienThucHien == maNv);
            }

            var raw = await q.OrderByDescending(p => p.NgayPhanCong).Take(300)
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

            var pcs = raw.Where(p => !y.CoKy || TrongKy(LayNgay(p.NgayPhanCong), y)).ToList();
            if (pcs.Count == 0)
                return laNv ? "Dạ, bạn chưa có phân công nào." : "Dạ, chưa có phân công công việc nào khớp yêu cầu.";

            var sb = new StringBuilder();
            sb.AppendLine(laNv
                ? $"Dạ, bạn có **{pcs.Count}** phân công{(y.CoKy ? " " + FormatKy(y) : " gần đây")}:"
                : $"Dạ, có **{pcs.Count}** phân công{(y.CoKy ? " " + FormatKy(y) : " gần đây")}:");
            foreach (var g in pcs.GroupBy(x => x.TrangThai ?? "(trống)"))
                sb.AppendLine($"• Trạng thái **{g.Key}**: {g.Count()}");
            foreach (var p in pcs.Take(15))
            {
                var hs = p.MaHoSoBaoTri != null ? $"BT#{p.MaHoSoBaoTri}" : $"SC#{p.MaHoSoSuaChua}";
                sb.AppendLine(laNv
                    ? $"• PC#{p.MaPhanCong} | {hs} | {p.TrangThai} | {p.NgayPhanCong:dd/MM/yyyy}"
                    : $"• PC#{p.MaPhanCong} | {hs} | {p.TenNv} | {p.TrangThai} | {p.NgayPhanCong:dd/MM/yyyy}"
                      + (p.LaNguoiGhiChep ? " | ghi chép" : ""));
            }
            if (pcs.Count > 15) sb.AppendLine($"… và {pcs.Count - 15} phân công khác.");
            return sb.ToString().TrimEnd();
        }

        // ---------- Thiết bị ----------

        private async Task<string> TraLoiThietBiAsync(YeuCau y, CancellationToken ct)
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

            if (y.TenThietBi != null)
            {
                var hit = tbs
                    .Select(x => new { x, sc = DiemKhop(y.TenThietBi, x.TenThietBi) })
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

            int Dem(string k) => tbs.Count(x => (x.TinhTrangHienTai ?? "")
                .Contains(k, StringComparison.OrdinalIgnoreCase));

            var ds = tbs.AsEnumerable();
            if (y.TinhTrangTb != null)
                ds = ds.Where(x => (x.TinhTrangHienTai ?? "").Contains(y.TinhTrangTb, StringComparison.OrdinalIgnoreCase));
            var arr = ds.ToList();

            var sb = new StringBuilder();
            if (y.TinhTrangTb != null)
                sb.AppendLine($"Dạ, có **{arr.Count}** thiết bị đang **{y.TinhTrangTb}**.");
            else
            {
                sb.AppendLine($"Dạ, hệ thống đang quản lý **{tbs.Count}** thiết bị.");
                sb.AppendLine($"Trạng thái: Sản xuất **{Dem("Sản xuất")}** | Bảo trì **{Dem("Bảo trì")}** | Sửa chữa **{Dem("Sửa chữa")}**.");
            }
            foreach (var g in arr.GroupBy(x => x.LoaiThietBi ?? "(chưa loại)").OrderBy(g => g.Key))
            {
                sb.AppendLine($"**{g.Key}** ({g.Count()}):");
                foreach (var t in g.OrderBy(x => x.TenThietBi))
                    sb.AppendLine($"  • TB#{t.MaThietBi} {t.TenThietBi} — {t.TinhTrangHienTai ?? "—"}"
                                  + (string.IsNullOrWhiteSpace(t.ViTriLapDat) ? "" : $" ({t.ViTriLapDat})"));
            }
            return sb.ToString().TrimEnd();
        }

        // ---------- Lịch bảo trì dự kiến ----------

        private async Task<string> TraLoiLichAsync(CancellationToken ct)
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
            var sb = new StringBuilder();
            sb.AppendLine("Dạ, lịch bảo trì dự kiến sắp tới:");
            foreach (var c in lich)
                sb.AppendLine($"• {c.NgayDuKienBaoTri:dd/MM/yyyy}: {c.Ten}");
            return sb.ToString().TrimEnd();
        }

        // ---------- Bước quy trình ----------

        private async Task<string> TraLoiBuocAsync(int maNd, VaiTroKey vk, YeuCau y, CancellationToken ct)
        {
            var q = _db.TienDoBuocQuyTrinhs.AsNoTracking().AsQueryable();
            if (vk == VaiTroKey.Nvkt)
            {
                var pv = await LayPhamViNvAsync(maNd, ct);
                if (pv.MaNv == null) return "Dạ, không xác định được nhân viên.";
                var maBts = pv.Bt.ToList();
                var maScs = pv.Sc.ToList();
                q = q.Where(t =>
                    (t.MaHoSoBaoTri != null && maBts.Contains(t.MaHoSoBaoTri.Value)) ||
                    (t.MaHoSoSuaChua != null && maScs.Contains(t.MaHoSoSuaChua.Value)));
            }
            var maBt = y.MaBt;
            var maSc = y.MaSc;
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

        // =====================================================================
        //  NGƯỜI DÙNG BÁO SAI → TÍNH LẠI + HIỂU LẠI + GHI NHỚ
        // =====================================================================

        private async Task<string> XuLySuaSaiAsync(
            int maNd, VaiTroKey vk, string vt, string ten, string cauSua,
            IReadOnlyList<(string role, string content)>? lichSu, CancellationToken ct)
        {
            var cauTruoc = TrichCauHoiTruoc(lichSu);
            if (cauTruoc == null)
                return "Dạ, xin lỗi bạn. Bạn nhắc lại giúp mình câu hỏi cần kiểm tra "
                       + "(ví dụ: «tháng 10 có bao nhiêu hồ sơ bảo trì hoàn thành») để mình đối chiếu lại với số liệu trên app nhé.";

            var qTruoc = Nghia(cauTruoc);
            var yTruoc = XacDinhYeuCau(vk, qTruoc, null);
            var qSua = Nghia(cauSua);
            var yMoi = yTruoc.Clone();
            GopLoiSua(yMoi, PhanTich(qSua), qSua);
            var soMongDoi = TrichSoMongDoi(qSua);

            // Câu trước do LLM trả lời (không phải số liệu) → để LLM xem lại với dữ liệu mới
            if (yMoi.ChuDe == ChuDeHoi.Khac)
            {
                LuuPhanHoi(vk, cauTruoc, cauSua, soMongDoi, new List<ThayDoiTruong>(), false, qTruoc);
                if (!string.IsNullOrEmpty(GroqKey))
                {
                    var ctx = await NapContextGonAsync(maNd, vk, vt, ct);
                    var (ok, text, _) = await GoiGroqAsync(vk, vt, ten, cauTruoc, ctx, lichSu, ct,
                        "User báo câu trả lời trước SAI. Đối chiếu lại CONTEXT, nhận lỗi ngắn gọn rồi đưa câu trả lời đúng. "
                        + "Nếu CONTEXT không có dữ liệu thì nói rõ là chưa có, không đoán.");
                    if (ok && !string.IsNullOrWhiteSpace(text))
                        return "Dạ, xin lỗi — mình đã kiểm tra lại:\n" + text.Trim();
                }
                return "Dạ, xin lỗi bạn. Bạn nói rõ giúp mình đang hỏi về loại hồ sơ nào, trạng thái nào, tháng nào "
                       + "(ví dụ: «ý tôi là hồ sơ sửa chữa đã hoàn thành tháng 10») để mình tra lại cho đúng nhé.";
            }

            if (!QuyenChuDe[vk].Contains(yMoi.ChuDe))
                return $"Dạ, nội dung này nằm ngoài phạm vi vai trò «{vt}». Bạn có thể hỏi về: {MoTaViecDuocHoi(vk)}.";

            // Người dùng nêu số đúng trên app → tìm cách hiểu/cách tính cho ra đúng số đó
            string? giaiThich = null;
            if (soMongDoi != null && yMoi.ChuDe is ChuDeHoi.HoSoBt or ChuDeHoi.HoSoSc)
            {
                var pv = vk == VaiTroKey.Nvkt ? await LayPhamViNvAsync(maNd, ct) : null;
                var rowsBt = await LayHsAsync(true, pv?.Bt, ct);
                var rowsSc = await LayHsAsync(false, pv?.Sc, ct);
                if (DemTong(yMoi, rowsBt, rowsSc) != soMongDoi.Value)
                {
                    var khop = TimCachKhop(yMoi, soMongDoi.Value, rowsBt, rowsSc);
                    if (khop != null)
                    {
                        giaiThich = $"Số **{soMongDoi}** khớp khi hiểu theo cách này. " + MoTaThayDoi(yTruoc, khop);
                        yMoi = khop;
                    }
                }
                else
                {
                    giaiThich = $"Số **{soMongDoi}** của bạn khớp với dữ liệu hiện tại.";
                }
            }

            var traLoi = await TraLoiTheoYeuCauAsync(maNd, vk, vt, yMoi, ct);
            var deltas = TinhThayDoi(yTruoc, yMoi);
            var doiY = !GiongYeuCau(yTruoc, yMoi);

            var sb = new StringBuilder();
            sb.AppendLine(doiY
                ? "Dạ, xin lỗi — mình đã hiểu lại ý bạn và đối chiếu trực tiếp với dữ liệu hiện tại trên app:"
                : "Dạ, xin lỗi — mình đã đối chiếu lại trực tiếp với dữ liệu hiện tại trên app:");
            if (giaiThich != null)
                sb.AppendLine(giaiThich);
            else if (doiY)
            {
                var mt = MoTaThayDoi(yTruoc, yMoi);
                if (mt.Length > 0) sb.AppendLine("(" + mt + ")");
            }
            sb.AppendLine();
            sb.AppendLine(traLoi);

            if (!doiY)
            {
                var alt = await DoiChieuCoSoAsync(maNd, vk, yMoi, ct);
                if (alt != null) sb.AppendLine(alt);
                sb.AppendLine("Nếu số trên app khác, bạn cho mình biết số bạn đang thấy hoặc nói «ý tôi là …» "
                              + "(loại hồ sơ, trạng thái, tháng) để mình đối chiếu và ghi nhớ cho các lần sau.");
                LuuPhanHoi(vk, cauTruoc, cauSua, soMongDoi, deltas, false, qTruoc);
            }
            else if (deltas.Count > 0)
            {
                sb.AppendLine("Mình đã ghi nhớ cách hiểu này cho các câu hỏi tương tự.");
                LuuPhanHoi(vk, cauTruoc, cauSua, soMongDoi, deltas, true, qTruoc);
            }
            return sb.ToString().TrimEnd();
        }

        private static int DemTong(YeuCau y, List<HsRow> rowsBt, List<HsRow> rowsSc)
        {
            var n = 0;
            if (y.ChuDe == ChuDeHoi.HoSoBt || y.CaBtVaSc) n += TinhHs(rowsBt, true, y).List.Count;
            if (y.ChuDe == ChuDeHoi.HoSoSc || y.CaBtVaSc) n += TinhHs(rowsSc, false, y).List.Count;
            return n;
        }

        /// <summary>Thử các cách hiểu khác (cách tính, trạng thái, loại hồ sơ) xem cách nào ra đúng số người dùng nêu.</summary>
        private static YeuCau? TimCachKhop(YeuCau goc, int mongDoi, List<HsRow> rowsBt, List<HsRow> rowsSc)
        {
            var cands = new List<YeuCau>();
            void Them(Action<YeuCau> sua) { var c = goc.Clone(); sua(c); cands.Add(c); }
            string Dao(string s) => s == "app" ? "thuc_te" : "app";

            Them(c => c.CoSo = Dao(c.CoSo));
            foreach (var tt in new[] { "hoan thanh", "dang thuc hien", "cho duyet", "tu choi", "cho xuong", "da duyet" })
            {
                Them(c => c.TrangThai = tt);
                Them(c => { c.TrangThai = tt; c.CoSo = Dao(c.CoSo); });
            }
            Them(c => { c.TrangThai = null; c.TatCa = true; });
            Them(c => { c.ChuDe = c.ChuDe == ChuDeHoi.HoSoBt ? ChuDeHoi.HoSoSc : ChuDeHoi.HoSoBt; c.CaBtVaSc = false; });
            Them(c => { c.ChuDe = ChuDeHoi.HoSoBt; c.CaBtVaSc = true; });

            return cands.FirstOrDefault(c => DemTong(c, rowsBt, rowsSc) == mongDoi);
        }

        /// <summary>Khi cách hiểu không đổi: cho thấy cách tính khác (tháng kế hoạch vs ngày hoàn thành thực tế) ra bao nhiêu.</summary>
        private async Task<string?> DoiChieuCoSoAsync(int maNd, VaiTroKey vk, YeuCau y, CancellationToken ct)
        {
            if (y.ChuDe != ChuDeHoi.HoSoBt && !y.CaBtVaSc) return null;
            var pv = vk == VaiTroKey.Nvkt ? await LayPhamViNvAsync(maNd, ct) : null;
            var rows = await LayHsAsync(true, pv?.Bt, ct);
            var alt = y.Clone();
            alt.CoSo = y.CoSo == "app" ? "thuc_te" : "app";
            var a = TinhHs(rows, true, y).List.Count;
            var b = TinhHs(rows, true, alt).List.Count;
            if (a == b) return null;
            return $"Đối chiếu thêm (hồ sơ bảo trì): tính theo {(alt.CoSo == "thuc_te" ? "ngày hoàn thành thực tế" : "tháng kế hoạch")} "
                   + $"thì ra **{b}** hồ sơ (cách đang dùng: {a}).";
        }

        private static void GopLoiSua(YeuCau yMoi, YeuCau ySua, string qSua)
        {
            // Chỉ đổi loại hồ sơ khi lời sửa nhắc rõ loại, tránh «hồ sơ hoàn thành» làm lệch sang BT+SC
            if (ySua.ChuDe != ChuDeHoi.Khac && Co(qSua, "bao tri", "sua chua", "vat tu", "phan cong", "thiet bi", "lich"))
            {
                yMoi.ChuDe = ySua.ChuDe;
                yMoi.CaBtVaSc = ySua.CaBtVaSc;
            }
            if (ySua.TrangThai != null) yMoi.TrangThai = ySua.TrangThai;
            if (ySua.CoKy) { yMoi.Ngay = ySua.Ngay; yMoi.Thang = ySua.Thang; yMoi.Nam = ySua.Nam; }
            if (ySua.TatCa) yMoi.TatCa = true;
            if (Co(qSua, "thuc te")) yMoi.CoSo = "thuc_te";
            else if (Co(qSua, "ke hoach")) yMoi.CoSo = "app";
        }

        private static int? TrichSoMongDoi(string q)
        {
            var m = Regex.Match(q, @"\b(\d{1,4})\s*(?:ho so|thiet bi|cai)\b");
            if (!m.Success) m = Regex.Match(q, @"\b(?:phai la|dung la|la|co|ra|thay)\s+(\d{1,4})\b");
            return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
        }

        private static string DocTruong(YeuCau y, string t) => t switch
        {
            "chude" => y.ChuDe.ToString(),
            "trangthai" => y.TrangThai ?? "",
            "coso" => y.CoSo,
            "cabtvasc" => y.CaBtVaSc ? "1" : "0",
            "tatca" => y.TatCa ? "1" : "0",
            _ => ""
        };

        private static void GhiTruong(YeuCau y, string t, string v)
        {
            switch (t)
            {
                case "chude": if (Enum.TryParse<ChuDeHoi>(v, out var c)) y.ChuDe = c; break;
                case "trangthai": y.TrangThai = string.IsNullOrEmpty(v) ? null : v; break;
                case "coso": y.CoSo = v; break;
                case "cabtvasc": y.CaBtVaSc = v == "1"; break;
                case "tatca": y.TatCa = v == "1"; break;
            }
        }

        private static readonly string[] CacTruongHoc = { "chude", "trangthai", "coso", "cabtvasc", "tatca" };

        private static List<ThayDoiTruong> TinhThayDoi(YeuCau a, YeuCau b)
        {
            var l = new List<ThayDoiTruong>();
            foreach (var t in CacTruongHoc)
            {
                var x = DocTruong(a, t);
                var z = DocTruong(b, t);
                if (x != z) l.Add(new ThayDoiTruong { Truong = t, Truoc = x, Sau = z });
            }
            return l;
        }

        private static bool GiongYeuCau(YeuCau a, YeuCau b) =>
            CacTruongHoc.All(t => DocTruong(a, t) == DocTruong(b, t))
            && a.Ngay == b.Ngay && a.Thang == b.Thang && a.Nam == b.Nam;

        private static string TenLoai(YeuCau y) =>
            y.CaBtVaSc ? "bảo trì + sửa chữa" : y.ChuDe switch
            {
                ChuDeHoi.HoSoBt => "bảo trì",
                ChuDeHoi.HoSoSc => "sửa chữa",
                ChuDeHoi.VatTu => "vật tư",
                ChuDeHoi.PhanCong => "phân công",
                ChuDeHoi.ThietBi => "thiết bị",
                _ => "khác"
            };

        private static string MoTaThayDoi(YeuCau a, YeuCau b)
        {
            var l = new List<string>();
            if (a.ChuDe != b.ChuDe || a.CaBtVaSc != b.CaBtVaSc) l.Add("loại hồ sơ: " + TenLoai(b));
            if (a.TrangThai != b.TrangThai || a.TatCa != b.TatCa)
                l.Add("trạng thái: " + (b.TrangThai == null ? "tất cả" : NhanTt(b.TrangThai)));
            if (a.CoSo != b.CoSo)
                l.Add("cách tính: " + (b.CoSo == "thuc_te" ? "theo ngày hoàn thành thực tế" : "theo tháng kế hoạch"));
            if (a.Thang != b.Thang || a.Nam != b.Nam || a.Ngay != b.Ngay) l.Add("kỳ: " + FormatKy(b));
            return l.Count == 0 ? "" : "Đã điều chỉnh " + string.Join("; ", l) + ".";
        }

        // ---------- Học: lưu & áp dụng bài học ----------

        private static readonly HashSet<string> TuBoQua = new()
        {
            "co", "bao", "nhieu", "la", "gi", "cua", "toi", "minh", "va", "cho", "trong", "nay", "do", "the", "nao",
            "thang", "nam", "ngay", "duoc", "khong", "xin", "vui", "long", "hay", "giup", "em", "anh", "chi", "oi", "nhe", "voi"
        };

        private static HashSet<string> TokenChuDe(string qNghia) =>
            Regex.Split(qNghia, @"[^a-z0-9]+")
                .Where(t => t.Length >= 2 && !TuBoQua.Contains(t) && !t.All(char.IsDigit))
                .ToHashSet();

        private static void LuuPhanHoi(
            VaiTroKey vk, string cauTruoc, string loiSua, int? soMongDoi,
            List<ThayDoiTruong> deltas, bool daGiaiQuyet, string qTruoc)
        {
            ChatbotBaiHocStore.Them(new BaiHoc
            {
                VaiTro = vk.ToString(),
                CauGoc = cauTruoc.Length > 300 ? cauTruoc[..300] : cauTruoc,
                LoiSua = loiSua.Length > 300 ? loiSua[..300] : loiSua,
                SoMongDoi = soMongDoi,
                Tokens = TokenChuDe(qTruoc).ToList(),
                ThayDoi = deltas,
                DaGiaiQuyet = daGiaiQuyet && deltas.Count > 0
            });
        }

        /// <summary>
        /// Câu hỏi mới giống (đủ nhiều từ khóa chung) câu từng bị báo sai → nếu parser lại ra đúng cách hiểu sai cũ
        /// thì sửa theo cách hiểu đã được xác nhận. Đổi cách hỏi vẫn khớp vì so khớp theo từ khóa chủ đề đã chuẩn hóa đồng nghĩa.
        /// </summary>
        private static void ApDungBaiHoc(VaiTroKey vk, string q, YeuCau y)
        {
            var tk = TokenChuDe(q);
            if (tk.Count == 0) return;
            BaiHoc? best = null;
            double bestScore = 0;
            foreach (var b in ChatbotBaiHocStore.TatCa())
            {
                if (!b.DaGiaiQuyet || b.ThayDoi.Count == 0 || b.VaiTro != vk.ToString()) continue;
                var tb = new HashSet<string>(b.Tokens);
                var inter = tk.Count(tb.Contains);
                var union = tk.Count + tb.Count - inter;
                if (inter < 2 || union == 0) continue;
                var score = (double)inter / union;
                if (score >= 0.5 && score > bestScore) { best = b; bestScore = score; }
            }
            if (best == null) return;

            var dung = false;
            foreach (var d in best.ThayDoi)
            {
                if (DocTruong(y, d.Truong) == d.Truoc)
                {
                    GhiTruong(y, d.Truong, d.Sau);
                    dung = true;
                }
            }
            if (dung) ChatbotBaiHocStore.TangSuDung(best.Id);
        }

        private static bool LaBaoCauTraLoiSai(string q)
        {
            if (q.Length > 160) return false;
            var khang = Co(q, "sai roi", "tra loi sai", "cau tra loi sai", "ban sai", "sai mat", "khong dung", "chua dung",
                "sai so", "sai du lieu", "so lieu sai", "khong khop", "lech so", "khac voi app", "khong giong app");
            var xemLai = Co(q, "xem lai", "kiem tra lai", "doi chieu lai");
            return khang || (xemLai && q.Length <= 60);
        }

        private static string? TrichCauHoiTruoc(IReadOnlyList<(string role, string content)>? lichSu)
        {
            if (lichSu == null || lichSu.Count == 0) return null;
            for (var i = lichSu.Count - 1; i >= 0; i--)
            {
                var r = (lichSu[i].role ?? "").Trim().ToLowerInvariant();
                if (r is "user" or "người dùng" or "human")
                {
                    var c = lichSu[i].content?.Trim();
                    if (!string.IsNullOrWhiteSpace(c) && !LaBaoCauTraLoiSai(Nghia(c)))
                        return c;
                }
            }
            return null;
        }

        // =====================================================================
        //  LLM (Groq) — chỉ cho câu hỏi giải thích / quy trình
        // =====================================================================

        /// <summary>Context GỌN: chỉ chủ đề được phép của vai trò, hàng đợi đang mở + tháng hiện tại. Không đổ lịch sử cũ.</summary>
        private async Task<string> NapContextGonAsync(int maNd, VaiTroKey vk, string vt, CancellationToken ct)
        {
            var quyen = QuyenChuDe[vk];
            var now = DateTime.Now;
            var sb = new StringBuilder();
            sb.AppendLine($"Vai_tro: {vt}");
            sb.AppendLine($"Thoi_diem: {now:yyyy-MM-dd HH:mm}");
            sb.AppendLine("Chu_de_duoc_phep: " + string.Join(", ", quyen));

            PhamViNv? pv = null;
            if (vk == VaiTroKey.Nvkt)
            {
                pv = await LayPhamViNvAsync(maNd, ct);
                sb.AppendLine("Ghi_chu: chỉ dữ liệu gắn phân công của chính người dùng này.");
            }

            foreach (var laBt in new[] { true, false })
            {
                if (!quyen.Contains(laBt ? ChuDeHoi.HoSoBt : ChuDeHoi.HoSoSc)) continue;
                var tien = laBt ? "BT" : "SC";
                var rows = await LayHsAsync(laBt, pv == null ? null : (laBt ? pv.Bt : pv.Sc), ct);

                var dangMo = rows.Where(r => !DaDong(TtKey(r.TrangThai))).ToList();
                sb.AppendLine($"HS_{tien}_dang_mo (hàng đợi hiện tại, mọi thời điểm): {dangMo.Count}");
                foreach (var g in dangMo.GroupBy(r => TtKey(r.TrangThai)))
                    sb.AppendLine($"  {tien}[{NhanTt(g.Key)}]={g.Count()}");
                foreach (var r in dangMo.OrderByDescending(r => r.NgayHt).Take(15))
                    sb.AppendLine($"  {tien}#{r.Ma}|{r.TenTb}|tt={r.TrangThai}|ngay={NgayApp(r, laBt, "app"):dd/MM/yyyy}");

                var thangNay = rows.Where(r =>
                {
                    var d = NgayApp(r, laBt, "app");
                    return d.Year == now.Year && d.Month == now.Month;
                }).ToList();
                sb.AppendLine($"HS_{tien}_thang_{now.Month:00}/{now.Year}: {thangNay.Count}");
                foreach (var g in thangNay.GroupBy(r => TtKey(r.TrangThai)))
                    sb.AppendLine($"  {tien}_thang[{NhanTt(g.Key)}]={g.Count()}");
            }

            if (quyen.Contains(ChuDeHoi.VatTu))
            {
                var qv = _db.HoSoSuDungVatTus.AsNoTracking().AsQueryable();
                if (pv?.MaNv != null)
                {
                    var maNv = pv.MaNv;
                    qv = qv.Where(h => h.MaNhanVienTH == maNv);
                }
                var vts = await qv.Select(h => new { h.TrangThai }).ToListAsync(ct);
                sb.AppendLine($"HS_vat_tu_tong: {vts.Count}");
                foreach (var g in vts.GroupBy(x => VtKey(x.TrangThai)))
                    sb.AppendLine($"  VT[{NhanTt(g.Key)}]={g.Count()}");
            }

            if (quyen.Contains(ChuDeHoi.PhanCong))
            {
                var qp = _db.PhanCongCongViecs.AsNoTracking().AsQueryable();
                if (pv?.MaNv != null)
                {
                    var maNv = pv.MaNv;
                    qp = qp.Where(p => p.MaNhanVienThucHien == maNv);
                }
                var pcs = await qp.OrderByDescending(p => p.NgayPhanCong).Take(100)
                    .Select(p => new { p.TrangThai }).ToListAsync(ct);
                sb.AppendLine($"Phan_cong_gan_day: {pcs.Count}");
                foreach (var g in pcs.GroupBy(x => x.TrangThai ?? "(trống)"))
                    sb.AppendLine($"  PC[{g.Key}]={g.Count()}");
            }

            if (quyen.Contains(ChuDeHoi.ThietBi))
            {
                var tt = await _db.ThietBis.AsNoTracking()
                    .GroupBy(t => t.TinhTrangHienTai)
                    .Select(g => new { Tt = g.Key, C = g.Count() })
                    .ToListAsync(ct);
                sb.AppendLine($"Tong_thiet_bi: {tt.Sum(x => x.C)}");
                foreach (var g in tt) sb.AppendLine($"  TB[{g.Tt ?? "(trống)"}]={g.C}");
            }

            return sb.ToString();
        }

        private async Task<(bool ok, string? text, string? err)> GoiGroqAsync(
            VaiTroKey vk,
            string vt,
            string ten,
            string cauHoi,
            string context,
            IReadOnlyList<(string role, string content)>? lichSu,
            CancellationToken ct,
            string? ghiChuThem = null)
        {
            var system =
                "Bạn là OPC Bot — chatbot nội bộ app bảo trì/sửa chữa thiết bị cơ điện (OPC).\n" +
                $"Đây là chatbot của ĐÚNG vai trò «{vt}» (user: {ten}). Chỉ dùng dữ liệu trong CONTEXT; KHÔNG trả lời hộ vai trò khác.\n\n" +
                "SỐ LIỆU: Cấm bịa, cấm suy diễn, cấm lấy số cũ. Chỉ nêu con số có sẵn trong CONTEXT. " +
                "Nếu cần số liệu mà CONTEXT không có → nói chưa có và gợi ý hỏi theo mẫu, ví dụ «tháng 10 có bao nhiêu hồ sơ bảo trì hoàn thành».\n" +
                "Nếu thiếu thông tin về luồng nghiệp vụ → nói chưa có thông tin, không đoán.\n\n" +
                "Trả lời tiếng Việt, mở đầu «Dạ,», ngắn gọn, dùng gạch đầu dòng khi liệt kê.\n\n" +
                "KIẾN THỨC:\n" + XayDungKienThucHeThong(vk) + "\nPHẠM VI:\n" + MoTaPhamVi(vk) +
                (ghiChuThem != null ? "\nGHI CHÚ: " + ghiChuThem : "");

            var user = new StringBuilder();
            user.AppendLine("CONTEXT:");
            user.AppendLine(context);
            if (lichSu != null && lichSu.Count > 0)
            {
                user.AppendLine("LICH_SU_HOI_THOAI:");
                foreach (var (role, content) in lichSu.TakeLast(8))
                    user.AppendLine($"- {role}: {content}");
            }
            user.AppendLine("CAU_HOI:");
            user.AppendLine(cauHoi);

            var body = new
            {
                model = GroqModel,
                temperature = 0.1,
                max_tokens = 1500,
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
                        (int)res.StatusCode, GroqModel, raw.Length > 600 ? raw[..600] : raw);
                    string? groqMsg = null;
                    try
                    {
                        using var errDoc = JsonDocument.Parse(raw);
                        if (errDoc.RootElement.TryGetProperty("error", out var errEl)
                            && errEl.TryGetProperty("message", out var msgEl))
                            groqMsg = msgEl.GetString();
                    }
                    catch { /* bỏ qua */ }
                    return (false, null, string.IsNullOrEmpty(groqMsg)
                        ? $"Groq {(int)res.StatusCode}"
                        : $"Groq {(int)res.StatusCode}: {groqMsg}");
                }

                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    return (false, null, "no choices");

                var msg = choices[0].GetProperty("message");
                var text = msg.TryGetProperty("content", out var c) ? c.GetString() : null;
                // Một số model (qwen…) chèn phần suy nghĩ trong <think>…</think> → bỏ đi, không đưa cho người dùng
                if (text != null)
                    text = Regex.Replace(text, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
                return string.IsNullOrEmpty(text) ? (false, null, "empty") : (true, text, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Groq call failed model={Model}", GroqModel);
                return (false, null, ex.Message);
            }
        }

        // =====================================================================
        //  KIẾN THỨC / PHẠM VI THEO VAI TRÒ
        // =====================================================================

        private static VaiTroKey LayVaiTroKey(string vt)
        {
            var s = ChuanHoa(vt);
            if (s.Contains("nhan vien ky thuat")) return VaiTroKey.Nvkt;
            if (s.Contains("to truong co dien") || s.Contains("to truong ky thuat")) return VaiTroKey.ToTruong;
            if (s.Contains("xuong") || s.Contains("to truong san xuat")) return VaiTroKey.Xuong;
            if (s.Contains("giam doc")) return VaiTroKey.GiamDoc;
            return VaiTroKey.Khac;
        }

        private static string MoTaViecDuocHoi(VaiTroKey vk) => vk switch
        {
            VaiTroKey.ToTruong => "hồ sơ bảo trì/sửa chữa, phân công NVKT, vật tư, thiết bị, lịch bảo trì, bước quy trình",
            VaiTroKey.Xuong => "hồ sơ bảo trì/sửa chữa, vật tư, thiết bị, lịch bảo trì, bước quy trình",
            VaiTroKey.GiamDoc => "hồ sơ chờ duyệt, thống kê bảo trì/sửa chữa, vật tư, thiết bị, lịch bảo trì",
            VaiTroKey.Nvkt => "việc được phân công cho bạn, hồ sơ bảo trì/sửa chữa của bạn, bước quy trình, vật tư của bạn",
            _ => "thông tin gắn với tài khoản đang đăng nhập"
        };

        private static string MauCauHoi(VaiTroKey vk, string vt)
        {
            var vd = vk == VaiTroKey.Nvkt
                ? "• Việc được phân công của tôi?\n• Các bước còn lại của BT#12?\n• Hồ sơ vật tư của tôi chờ xác nhận?"
                : "• Tháng 10 có bao nhiêu hồ sơ bảo trì hoàn thành?\n• Hồ sơ bảo trì nào đang chờ duyệt?\n• Hồ sơ vật tư chờ xác nhận?";
            return $"Dạ, mình chưa hiểu rõ ý bạn (vai trò «{vt}»). Bạn thử hỏi theo mẫu:\n{vd}\n"
                   + $"Mình hỗ trợ: {MoTaViecDuocHoi(vk)}.";
        }

        private static string XayDungKienThucHeThong(VaiTroKey vk)
        {
            var chung =
                "APP: 2 chức năng — Bảo trì và Sửa chữa thiết bị cơ điện. Vai trò: Tổ trưởng cơ điện, Xưởng, Giám đốc, Nhân viên kỹ thuật (NVKT).\n"
                + "Trạng thái hồ sơ: Chờ duyệt | Đang thực hiện | Hoàn thành | Từ chối. Hồ sơ vật tư: Chờ gửi | Chờ xác nhận | Xác nhận.\n\n"
                + "LUỒNG BẢO TRÌ:\n"
                + "1) Tổ trưởng cơ điện tạo hồ sơ (thiết bị, danh mục, tháng bảo trì, ngày dự kiến bắt đầu–kết thúc). Ngày tổ trưởng chọn chỉ là DỰ KIẾN.\n"
                + "2) Gửi Xưởng; Xưởng xem/chỉnh (Xưởng CHỐT ngày bảo trì cuối cùng) rồi gửi Giám đốc duyệt.\n"
                + "3) Giám đốc duyệt → mở khóa phân công cho Tổ trưởng.\n"
                + "4) Tổ trưởng phân công một hoặc nhiều NVKT (NVKT có thể nhận nhiều thiết bị; bận thì tổ trưởng bỏ chọn, trao đổi ngoài hệ thống).\n"
                + "5) NVKT thấy yêu cầu bảo trì trên tài khoản → «Tiến hành bảo trì» → chọn vật tư theo từng bước (Lưu → thành hồ sơ vật tư ở Quản lý hồ sơ vật tư của Tổ trưởng) "
                + "→ ghi kết quả từng bước → «Hoàn thành» → yêu cầu của NVKT và hồ sơ bảo trì chuyển «Hoàn thành».\n"
                + "6) Tổ trưởng vào Quản lý hồ sơ vật tư, gửi hồ sơ vật tư cho Giám đốc → trạng thái «Chờ xác nhận».\n"
                + "CHƯA CÓ THÔNG TIN CHẮC CHẮN: luồng sửa chữa chi tiết; điều gì xảy ra khi Giám đốc từ chối; ai xác nhận hồ sơ vật tư và sau đó ra sao. "
                + "Gặp câu hỏi này → nói chưa có thông tin, không bịa.\n";

            var rieng = vk switch
            {
                VaiTroKey.ToTruong => "VAI TRÒ NÀY (Tổ trưởng cơ điện): tạo HS bảo trì, gửi Xưởng, phân công NVKT sau khi Giám đốc duyệt, quản lý hồ sơ vật tư. Không chốt ngày thay Xưởng, không duyệt thay Giám đốc.\n",
                VaiTroKey.Xuong => "VAI TRÒ NÀY (Xưởng): xem/chỉnh và chốt ngày bảo trì, gửi Giám đốc. Không phân công NVKT, không duyệt thay Giám đốc.\n",
                VaiTroKey.GiamDoc => "VAI TRÒ NÀY (Giám đốc): duyệt hồ sơ, xem thống kê. Không phân công NVKT, không chốt ngày thay Xưởng.\n",
                VaiTroKey.Nvkt => "VAI TRÒ NÀY (NVKT): chỉ thấy việc được phân công cho mình; chọn vật tư, ghi kết quả từng bước, bấm Hoàn thành. Không xem dữ liệu toàn công ty, không duyệt, không phân công.\n",
                _ => "VAI TRÒ NÀY: chỉ thông tin gắn tài khoản đang đăng nhập.\n"
            };
            return chung + rieng;
        }

        private static string MoTaPhamVi(VaiTroKey vk) =>
            "- Được hỏi: " + MoTaViecDuocHoi(vk) + ".\n- Ngoài phạm vi này → từ chối lịch sự và nói vai trò nào phụ trách.";

        private static string? TuChoiVuotVaiTro(VaiTroKey vk, string q)
        {
            if (vk == VaiTroKey.Nvkt)
            {
                if (Co(q, "toan bo", "tat ca nhan vien", "toan cong ty"))
                    return "Dạ, NVKT chỉ xem công việc được phân công trên tài khoản của bạn.";
                if (Co(q, "duyet") && Co(q, "ho so", "giam doc"))
                    return "Dạ, phê duyệt hồ sơ thuộc Giám đốc — NVKT không thực hiện được.";
                if (Co(q, "chot ngay"))
                    return "Dạ, chốt ngày bảo trì thuộc Xưởng.";
                if (Co(q, "phan cong") && Co(q, "nhan vien", "to truong") && Co(q, "cho", "giup", "thay"))
                    return "Dạ, phân công nhân viên thuộc Tổ trưởng cơ điện.";
            }
            if (vk == VaiTroKey.ToTruong)
            {
                if (Co(q, "thay giam doc") || (Co(q, "toi duyet", "duyet ho") && Co(q, "giam doc")))
                    return "Dạ, Tổ trưởng không duyệt hộ Giám đốc. Sau khi Giám đốc duyệt, bạn mới phân công NVKT.";
                if (Co(q, "chot ngay") && Co(q, "thay", "ho"))
                    return "Dạ, chốt ngày bảo trì thuộc Xưởng.";
            }
            if (vk == VaiTroKey.Xuong)
            {
                if (Co(q, "phan cong") && Co(q, "thay to truong", "ho to truong", "toi phan cong", "giup phan cong"))
                    return "Dạ, phân công NVKT thuộc Tổ trưởng cơ điện. Xưởng chốt ngày bảo trì.";
            }
            if (vk == VaiTroKey.GiamDoc)
            {
                if (Co(q, "phan cong nhan vien") && Co(q, "thay", "ho"))
                    return "Dạ, phân công thuộc Tổ trưởng cơ điện (sau khi bạn duyệt hồ sơ).";
                if (Co(q, "chot ngay") && Co(q, "thay", "ho"))
                    return "Dạ, chốt ngày thuộc Xưởng.";
            }
            return null;
        }

        // =====================================================================
        //  HELPERS CHUNG
        // =====================================================================

        private static bool LaHoiVatTu(string q) =>
            Co(q, "vat tu", "ho so vat tu", "hs vat tu", "nguyen vat lieu")
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

        private static string? TrichTenThietBi(string q)
        {
            var s = q;
            string[] bo =
            {
                "thiet bi", "trang thai", "tinh trang", "dang o", "la gi", "bao tri", "sua chua",
                "gan nhat", "tiep theo", "lich", "ngay", "cua", "bao nhieu", "co may", "danh muc",
                "thang", "nam", "ho so", "da", "duoc", "hien", "tai", "va", "cho", "toi", "minh", "sap toi",
                "nhung", "nao", "gom", "danh sach", "he thong", "co", "gi", "dang", "san xuat"
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

        private static void XuLyThoiGianTuongDoi(string q, YeuCau y)
        {
            var now = DateTime.Now;
            if (Co(q, "hom nay")) { y.Ngay = now.Day; y.Thang = now.Month; y.Nam = now.Year; return; }
            if (y.Thang == null)
            {
                if (Co(q, "thang nay")) { y.Thang = now.Month; y.Nam ??= now.Year; }
                else if (Co(q, "thang truoc", "thang vua roi", "thang qua"))
                {
                    var d = now.AddMonths(-1);
                    y.Thang = d.Month; y.Nam = d.Year;
                }
            }
            if (y.Nam == null)
            {
                if (Co(q, "nam nay")) y.Nam = now.Year;
                else if (Co(q, "nam ngoai", "nam truoc", "nam qua")) y.Nam = now.Year - 1;
            }
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

        /// <summary>Từ đồng nghĩa → dạng chuẩn, để đổi cách hỏi vẫn hiểu như nhau.</summary>
        private static readonly (Regex re, string den)[] DongNghia = new (string tu, string den)[]
        {
            ("bao duong", "bao tri"),
            ("hoan tat", "hoan thanh"), ("lam xong", "hoan thanh"), ("da xong", "hoan thanh"), ("xong", "hoan thanh"),
            ("hs", "ho so"), ("phieu", "ho so"),
            ("hu hong", "sua chua"), ("su co", "sua chua"), ("khac phuc", "sua chua"),
            ("bi tra lai", "tu choi"), ("bi tra", "tu choi"), ("bi loai", "tu choi"), ("khong duyet", "tu choi"),
            ("cho giam doc duyet", "cho duyet"), ("cho giam doc", "cho duyet"), ("cho gd duyet", "cho duyet"), ("cho phe duyet", "cho duyet"),
            ("dang lam", "dang thuc hien"), ("dang tien hanh", "dang thuc hien"), ("dang xu ly", "dang thuc hien"),
            ("phu tung", "vat tu"), ("linh kien", "vat tu"), ("vat lieu", "vat tu"),
            ("giao viec", "phan cong"), ("duoc giao", "phan cong")
        }.Select(x => (new Regex($@"\b{Regex.Escape(x.tu)}\b", RegexOptions.Compiled), x.den)).ToArray();

        /// <summary>Chuẩn hóa không dấu + thay từ đồng nghĩa.</summary>
        private static string Nghia(string input)
        {
            var s = ChuanHoa(input);
            foreach (var (re, den) in DongNghia)
                s = re.Replace(s, den);
            return s;
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