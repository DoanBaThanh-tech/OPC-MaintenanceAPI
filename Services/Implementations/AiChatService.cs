using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    /// <summary>
    /// OPC Bot — Rule-based / Intent-driven.
    /// Không dùng Ollama/Gemini. Phân tích intent + slot → query DB theo vai trò → trả lời ngắn.
    /// </summary>
    public class AiChatService : IAiChatService
    {
        private readonly OPCDbContext _db;
        private readonly ILogger<AiChatService> _logger;

        public AiChatService(OPCDbContext db, ILogger<AiChatService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public Task<(bool online, string? model, string? loi)> KiemTraAsync(CancellationToken ct = default)
            => Task.FromResult<(bool, string?, string?)>((true, "OPC-IntentEngine/v1", null));

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
                return (true, "Dạ, mình không chia sẻ thông tin bảo mật hệ thống. Hỏi nghiệp vụ trong phạm vi vai trò của bạn.", null);

            var vuot = KiemTraVuotQuyen(vt, q);
            if (vuot != null)
                return (true, $"Dạ, {vuot}", null);

            try
            {
                var intents = PhanTichIntent(q, cauHoi);
                var parts = new List<string>();
                var i = 0;
                foreach (var intent in intents)
                {
                    i++;
                    var ans = await XuLyIntentAsync(maNguoiDung, vt, ten, intent, ct);
                    if (intents.Count > 1)
                        parts.Add($"**Yêu cầu {i}:** {intent.MoTaNgan}\n{ans}");
                    else
                        parts.Add(ans);
                }
                return (true, string.Join("\n\n", parts), null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Intent engine lỗi");
                return (false, null, "Lỗi khi đọc dữ liệu hệ thống. Thử lại giúp mình.");
            }
        }

        // ===================== INTENT =====================

        private enum IntentKind
        {
            Chao,
            HuongDan,
            DemThietBi,
            TrangThaiThietBi,
            DemHsBt,
            DemHsSc,
            VatTu,
            BuocQuyTrinh,
            LichBaoTri,
            PhanCongCaNhan,
            KhongRo
        }

        private sealed class Intent
        {
            public IntentKind Kind { get; set; }
            public int? Thang { get; set; }
            public int? Nam { get; set; }
            public string? TrangThaiHs { get; set; } // Đã hoàn thành | Chờ duyệt | Đang thực hiện | null = tất cả
            public string? TenThietBiGoiY { get; set; }
            public int? MaBt { get; set; }
            public int? MaSc { get; set; }
            public string MoTaNgan { get; set; } = "";
        }

        private List<Intent> PhanTichIntent(string q, string cauGoc)
        {
            // Tách nhiều câu
            var segments = TachCau(cauGoc);
            var list = new List<Intent>();
            foreach (var seg in segments)
            {
                var qi = ChuanHoa(seg);
                list.Add(PhanTichMotCau(qi, seg));
            }
            return list;
        }

        private Intent PhanTichMotCau(string q, string goc)
        {
            var it = new Intent
            {
                Thang = TrichThang(q),
                Nam = TrichNam(q),
                MaBt = TrichMa(q, "bt"),
                MaSc = TrichMa(q, "sc"),
                MoTaNgan = goc.Trim().Length > 80 ? goc.Trim()[..80] + "…" : goc.Trim()
            };

            if (Co(q, "xin chao", "hello", "chao", "hi "))
            {
                it.Kind = IntentKind.Chao;
                return it;
            }
            if (Co(q, "cach", "lam sao", "huong dan", "phai lam gi"))
            {
                it.Kind = IntentKind.HuongDan;
                return it;
            }

            // Vật tư
            if (Co(q, "vat tu", "so luong vat", "tong tien", "chi phi vat"))
            {
                it.Kind = IntentKind.VatTu;
                return it;
            }

            // Bước quy trình
            if (Co(q, "buoc", "quy trinh") && !Co(q, "huong dan"))
            {
                it.Kind = IntentKind.BuocQuyTrinh;
                return it;
            }

            // Lịch BT
            if (Co(q, "lich bao tri", "lich sap", "ngay du kien", "bao tri tiep theo", "bao tri gan nhat")
                && !Co(q, "ho so"))
            {
                // Có thể là hỏi 1 TB
                if (Co(q, "thiet bi") || TrichTenThietBiGoiY(q) != null)
                {
                    it.Kind = IntentKind.TrangThaiThietBi;
                    it.TenThietBiGoiY = TrichTenThietBiGoiY(q);
                    return it;
                }
                it.Kind = IntentKind.LichBaoTri;
                return it;
            }

            // Phân công cá nhân (NVKT)
            if (Co(q, "phan cong", "cong viec cua toi", "viec duoc giao", "viec cua minh"))
            {
                it.Kind = IntentKind.PhanCongCaNhan;
                return it;
            }

            // Hồ sơ SC
            if (Co(q, "sua chua") && (Co(q, "ho so", "bao nhieu", "co may", "dem", "hoan thanh", "cho duyet", "dang thuc hien", "thang")))
            {
                it.Kind = IntentKind.DemHsSc;
                it.TrangThaiHs = TrichTrangThaiHs(q);
                return it;
            }

            // Hồ sơ BT
            if (Co(q, "bao tri", "ho so") && (Co(q, "ho so", "bao nhieu", "co may", "dem", "hoan thanh", "cho duyet", "dang thuc hien", "thang", "nam")))
            {
                it.Kind = IntentKind.DemHsBt;
                it.TrangThaiHs = TrichTrangThaiHs(q);
                return it;
            }
            if (Co(q, "hoan thanh") && Co(q, "thang", "nam", "ho so"))
            {
                it.Kind = Co(q, "sua chua") ? IntentKind.DemHsSc : IntentKind.DemHsBt;
                it.TrangThaiHs = "Đã hoàn thành";
                return it;
            }

            // Thiết bị: trạng thái 1 TB hoặc tổng quan
            if (Co(q, "thiet bi", "tinh trang", "trang thai", "san xuat", "danh muc", "bom", "may "))
            {
                var tenGoi = TrichTenThietBiGoiY(q);
                if (tenGoi != null || Co(q, "trang thai", "tinh trang", "dang o"))
                {
                    it.Kind = IntentKind.TrangThaiThietBi;
                    it.TenThietBiGoiY = tenGoi;
                    return it;
                }
                it.Kind = IntentKind.DemThietBi;
                return it;
            }

            // Mặc định: nếu có "tháng" + hoàn thành → dem HS BT
            if (it.Thang != null && Co(q, "hoan thanh"))
            {
                it.Kind = Co(q, "sua chua") ? IntentKind.DemHsSc : IntentKind.DemHsBt;
                it.TrangThaiHs = "Đã hoàn thành";
                return it;
            }

            it.Kind = IntentKind.KhongRo;
            return it;
        }

        private static string? TrichTrangThaiHs(string q)
        {
            if (Co(q, "hoan thanh")) return "Đã hoàn thành";
            if (Co(q, "cho duyet")) return "Chờ duyệt";
            if (Co(q, "dang thuc hien")) return "Đang thực hiện";
            if (Co(q, "tu choi")) return "Từ chối";
            if (Co(q, "cho xuong", "chot ngay")) return "Chờ xưởng";
            return null;
        }

        private static string? TrichTenThietBiGoiY(string q)
        {
            // Bỏ cụm phổ biến, giữ phần còn lại làm gợi ý tên
            var s = q;
            string[] bo =
            {
                "thiet bi", "trang thai", "tinh trang", "dang o", "la gi", "bao tri", "sua chua",
                "gan nhat", "tiep theo", "lich", "ngay", "cua", "bao nhieu", "co may", "danh muc",
                "thang", "nam", "ho so", "da", "duoc", "hien", "tai", "va", "cho", "toi", "minh"
            };
            foreach (var b in bo)
                s = Regex.Replace(s, $@"\b{Regex.Escape(b)}\b", " ");
            s = Regex.Replace(s, @"\d{4}", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length < 2) return null;
            // bỏ số tháng đơn
            if (Regex.IsMatch(s, @"^\d{1,2}$")) return null;
            return s;
        }

        // ===================== XỬ LÝ INTENT =====================

        private async Task<string> XuLyIntentAsync(
            int maNd, string vt, string ten, Intent it, CancellationToken ct)
        {
            return it.Kind switch
            {
                IntentKind.Chao => Chao(ten, vt),
                IntentKind.HuongDan => HuongDan(vt),
                IntentKind.DemThietBi => await DemThietBiAsync(ct),
                IntentKind.TrangThaiThietBi => await TrangThaiThietBiAsync(it, ct),
                IntentKind.DemHsBt => await DemHsAsync(true, it, vt, ct),
                IntentKind.DemHsSc => await DemHsAsync(false, it, vt, ct),
                IntentKind.VatTu => await VatTuAsync(maNd, vt, it, ct),
                IntentKind.BuocQuyTrinh => await BuocAsync(maNd, vt, it, ct),
                IntentKind.LichBaoTri => await LichBtAsync(vt, ct),
                IntentKind.PhanCongCaNhan => await PhanCongNvktAsync(maNd, vt, ct),
                _ => "Dạ, mình chưa nhận đúng ý. Hỏi rõ giúp: số hồ sơ theo tháng/trạng thái, tên thiết bị, vật tư, bước quy trình hoặc việc được phân công."
            };
        }

        private static string Chao(string ten, string vt)
            => $"Dạ, chào {ten}. OPC Bot (Intent) — phạm vi «{vt}». Hỏi số liệu nghiệp vụ, mình trả lời từ database.";

        private static string HuongDan(string vt) => vt switch
        {
            "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" =>
                "Dạ, Tổ trưởng: lập KH/HS bảo trì → phân công NVKT → theo dõi thiết bị & vật tư. Không duyệt hộ GĐ / không chốt ngày hộ Xưởng.",
            "Xưởng" or "Tổ trưởng sản xuất" =>
                "Dạ, Xưởng: chốt ngày BT → gửi GĐ; bị trả về thì sửa & gửi lại; xác nhận/từ chối quy trình NVKT.",
            "Giám đốc" =>
                "Dạ, Giám đốc: duyệt/từ chối HS; xem lịch sử & thống kê. Hỏi số HS chờ duyệt hoặc đã hoàn thành theo tháng.",
            "Nhân viên kỹ thuật" =>
                "Dạ, NVKT: Tiến hành → làm bước + vật tư → Hoàn thành gửi Xưởng. Bị từ chối: Cập nhật quy trình theo lý do.",
            _ => $"Dạ, hỏi số liệu trong đúng vai trò «{vt}»."
        };

        private async Task<string> DemThietBiAsync(CancellationToken ct)
        {
            var tbs = await _db.ThietBis.AsNoTracking()
                .Select(t => new { t.TenThietBi, t.LoaiThietBi, t.TinhTrangHienTai })
                .ToListAsync(ct);

            int Dem(string key) => tbs.Count(x =>
                (x.TinhTrangHienTai ?? "").Contains(key, StringComparison.OrdinalIgnoreCase));

            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, tổng **{tbs.Count}** thiết bị (Tất cả).");
            sb.AppendLine($"• Sản xuất: **{Dem("Sản xuất")}** | Bảo trì: **{Dem("Bảo trì")}** | Sửa chữa: **{Dem("Sửa chữa")}**.");

            var theoLoai = tbs.GroupBy(x => string.IsNullOrWhiteSpace(x.LoaiThietBi) ? "(chưa phân loại)" : x.LoaiThietBi!)
                .OrderByDescending(g => g.Count());
            sb.AppendLine("Theo danh mục:");
            foreach (var g in theoLoai)
                sb.AppendLine($"• {g.Key}: **{g.Count()}**");

            var dangBt = tbs.Where(x => (x.TinhTrangHienTai ?? "").Contains("Bảo trì")).Select(x => x.TenThietBi).Take(10);
            var dangSc = tbs.Where(x => (x.TinhTrangHienTai ?? "").Contains("Sửa chữa")).Select(x => x.TenThietBi).Take(10);
            if (dangBt.Any())
            {
                sb.AppendLine("Đang Bảo trì:");
                foreach (var n in dangBt) sb.AppendLine($"• {n}");
            }
            if (dangSc.Any())
            {
                sb.AppendLine("Đang Sửa chữa:");
                foreach (var n in dangSc) sb.AppendLine($"• {n}");
            }
            return sb.ToString().TrimEnd();
        }

        private async Task<string> TrangThaiThietBiAsync(Intent it, CancellationToken ct)
        {
            var all = await _db.ThietBis.AsNoTracking()
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

            if (all.Count == 0)
                return "Dạ, chưa có dữ liệu thiết bị.";

            if (!string.IsNullOrWhiteSpace(it.TenThietBiGoiY))
            {
                var goi = ChuanHoa(it.TenThietBiGoiY);
                var hit = all
                    .Select(t => new { T = t, Score = DiemKhop(goi, t.TenThietBi) })
                    .Where(x => x.Score >= 2)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();

                if (hit != null)
                {
                    var t = hit.T;
                    var tt = string.IsNullOrWhiteSpace(t.TinhTrangHienTai) ? "chưa ghi" : t.TinhTrangHienTai;
                    var gan = t.NgayBaoTriGanNhat?.ToString("dd/MM/yyyy") ?? "chưa có";
                    var tiep = t.NgayBaoTriTiepTheo?.ToString("dd/MM/yyyy") ?? "chưa có";
                    return $"Dạ, thiết bị **{t.TenThietBi}** hiện đang ở trạng thái **{tt}**. "
                         + $"Lịch bảo trì gần nhất là **{gan}** và tiếp theo là **{tiep}** ạ.";
                }
            }

            // Không khớp tên → tổng quan
            return await DemThietBiAsync(ct);
        }

        private async Task<string> DemHsAsync(bool laBt, Intent it, string vt, CancellationToken ct)
        {
            if (vt == "Nhân viên kỹ thuật")
                return "Dạ, NVKT chỉ xem việc được phân công. Hỏi «công việc được giao» hoặc «phân công của tôi».";

            var nam = it.Nam ?? DateTime.Now.Year;
            var thang = it.Thang;
            var ttLoc = it.TrangThaiHs;

            if (laBt)
            {
                var raw = await _db.HoSoBaoTris.AsNoTracking()
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

                var rows = raw.Select(h =>
                {
                    int th, na;
                    if (dictKh.TryGetValue(h.MaHoSoBaoTri, out var nk))
                    {
                        th = nk.Month; na = nk.Year;
                    }
                    else
                    {
                        var d = ttLoc == "Đã hoàn thành" ? h.NgayHt : h.NgayTao;
                        th = d.Month; na = d.Year;
                    }
                    return new { h.MaHoSoBaoTri, h.TrangThai, h.TenTb, h.NgayHt, Thang = th, Nam = na };
                }).AsEnumerable();

                // Có tháng/năm trong câu → lọc; không thì toàn hệ thống (mọi tháng) nhưng vẫn có thể lọc năm nếu có
                if (thang != null)
                    rows = rows.Where(x => x.Nam == nam && x.Thang == thang.Value);
                else if (it.Nam != null)
                    rows = rows.Where(x => x.Nam == nam);

                if (!string.IsNullOrEmpty(ttLoc))
                    rows = rows.Where(x => x.TrangThai == ttLoc
                        || (ttLoc == "Chờ xưởng" && (x.TrangThai == "Chờ xưởng" || x.TrangThai == "Chờ chốt ngày")));

                var list = rows.OrderByDescending(x => x.NgayHt).ToList();
                var ky = thang != null ? $"tháng {thang}/{nam}"
                    : it.Nam != null ? $"năm {nam}"
                    : "toàn hệ thống";
                var labelTt = ttLoc ?? "tất cả trạng thái";

                var sb = new StringBuilder();
                sb.AppendLine($"Dạ, {ky} có **{list.Count}** hồ sơ bảo trì **{labelTt}**.");
                foreach (var h in list.Take(10))
                    sb.AppendLine($"• BT#{h.MaHoSoBaoTri} | {h.TenTb} | {h.TrangThai} | {h.NgayHt:dd/MM/yyyy HH:mm}");
                if (list.Count > 10)
                    sb.AppendLine($"… và {list.Count - 10} hồ sơ khác.");
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
                        h.NgayTao,
                        TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                    }).ToListAsync(ct);

                var rows = raw.Select(h =>
                {
                    var d = ttLoc == "Đã hoàn thành" ? h.NgayHt : h.NgayTao;
                    return new { h.MaHoSoSuaChua, h.TrangThai, h.TenTb, h.NgayHt, Thang = d.Month, Nam = d.Year };
                }).AsEnumerable();

                if (thang != null)
                    rows = rows.Where(x => x.Nam == nam && x.Thang == thang.Value);
                else if (it.Nam != null)
                    rows = rows.Where(x => x.Nam == nam);

                if (!string.IsNullOrEmpty(ttLoc))
                    rows = rows.Where(x => x.TrangThai == ttLoc);

                var list = rows.OrderByDescending(x => x.NgayHt).ToList();
                var ky = thang != null ? $"tháng {thang}/{nam}"
                    : it.Nam != null ? $"năm {nam}"
                    : "toàn hệ thống";
                var labelTt = ttLoc ?? "tất cả trạng thái";

                var sb = new StringBuilder();
                sb.AppendLine($"Dạ, {ky} có **{list.Count}** hồ sơ sửa chữa **{labelTt}**.");
                foreach (var h in list.Take(10))
                    sb.AppendLine($"• SC#{h.MaHoSoSuaChua} | {h.TenTb} | {h.TrangThai} | {h.NgayHt:dd/MM/yyyy HH:mm}");
                if (list.Count > 10)
                    sb.AppendLine($"… và {list.Count - 10} hồ sơ khác.");
                return sb.ToString().TrimEnd();
            }
        }

        private async Task<string> VatTuAsync(int maNd, string vt, Intent it, CancellationToken ct)
        {
            IQueryable<Core.Entities.HoSoSuDungVatTu> q = _db.HoSoSuDungVatTus.AsNoTracking();

            if (vt == "Nhân viên kỹ thuật")
            {
                var maNv = await _db.NhanViens.AsNoTracking()
                    .Where(n => n.MaNguoiDung == maNd)
                    .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
                if (maNv == null) return "Dạ, không xác định được nhân viên.";
                q = q.Where(h => h.MaNhanVienTH == maNv);
            }

            if (it.MaBt != null) q = q.Where(h => h.MaHoSoBaoTri == it.MaBt);
            if (it.MaSc != null) q = q.Where(h => h.MaHoSoSuaChua == it.MaSc);

            var list = await q.OrderByDescending(h => h.NgayThucHien).Take(15)
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

            if (list.Count == 0)
                return "Dạ, chưa có hồ sơ vật tư trong phạm vi của bạn.";

            var focus = list[0];
            var hs = focus.MaHoSoBaoTri != null ? $"BT#{focus.MaHoSoBaoTri}" : $"SC#{focus.MaHoSoSuaChua}";
            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, quy trình **{hs}** ({focus.TenTb}, {focus.NgayThucHien:dd/MM/yyyy HH:mm}):");
            sb.AppendLine($"• **{focus.SoLoai}** loại vật tư, tổng SL **{focus.TongSl}**, tổng tiền **{focus.TongTien:N0} ₫**, TT phiếu: **{focus.TrangThai}**.");
            if (list.Count > 1)
            {
                sb.AppendLine("Phiếu gần đây khác:");
                foreach (var h in list.Skip(1).Take(5))
                {
                    var k = h.MaHoSoBaoTri != null ? $"BT#{h.MaHoSoBaoTri}" : $"SC#{h.MaHoSoSuaChua}";
                    sb.AppendLine($"• {k} | {h.TenTb} | SL {h.TongSl} | {h.TongTien:N0} ₫ | {h.TrangThai}");
                }
            }
            return sb.ToString().TrimEnd();
        }

        private async Task<string> BuocAsync(int maNd, string vt, Intent it, CancellationToken ct)
        {
            var q = _db.TienDoBuocQuyTrinhs.AsNoTracking().AsQueryable();

            if (vt == "Nhân viên kỹ thuật")
            {
                var maNv = await _db.NhanViens.AsNoTracking()
                    .Where(n => n.MaNguoiDung == maNd)
                    .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
                if (maNv == null) return "Dạ, không xác định được nhân viên.";
                var maBt = await _db.PhanCongCongViecs.AsNoTracking()
                    .Where(p => p.MaNhanVienThucHien == maNv && p.MaHoSoBaoTri != null)
                    .Select(p => p.MaHoSoBaoTri!.Value).Distinct().ToListAsync(ct);
                var maSc = await _db.PhanCongCongViecs.AsNoTracking()
                    .Where(p => p.MaNhanVienThucHien == maNv && p.MaHoSoSuaChua != null)
                    .Select(p => p.MaHoSoSuaChua!.Value).Distinct().ToListAsync(ct);
                q = q.Where(t =>
                    (t.MaHoSoBaoTri != null && maBt.Contains(t.MaHoSoBaoTri.Value)) ||
                    (t.MaHoSoSuaChua != null && maSc.Contains(t.MaHoSoSuaChua.Value)));
            }

            if (it.MaBt != null) q = q.Where(t => t.MaHoSoBaoTri == it.MaBt);
            if (it.MaSc != null) q = q.Where(t => t.MaHoSoSuaChua == it.MaSc);

            var rows = await q.OrderByDescending(t => t.ThoiDiemCapNhat).Take(200)
                .Select(t => new { t.MaHoSoBaoTri, t.MaHoSoSuaChua, t.SoBuoc, t.MoTaBuoc, t.TrangThai, t.ThoiDiemCapNhat })
                .ToListAsync(ct);

            if (rows.Count == 0)
                return "Dạ, chưa ghi nhận bước quy trình trong phạm vi của bạn.";

            var groups = rows.GroupBy(t => t.MaHoSoBaoTri != null ? $"BT#{t.MaHoSoBaoTri}" : $"SC#{t.MaHoSoSuaChua}");
            var focus = groups.First();
            var uniq = focus.GroupBy(x => x.SoBuoc).Select(g => g.OrderByDescending(x => x.ThoiDiemCapNhat).First())
                .OrderBy(x => x.SoBuoc).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, hồ sơ **{focus.Key}**: **{uniq.Count} bước** (xong: {uniq.Count(x => x.TrangThai == "DaXong")}, đang: {uniq.Count(x => x.TrangThai == "DangLam")}).");
            foreach (var b in uniq)
                sb.AppendLine($"• B{b.SoBuoc}: {b.MoTaBuoc} ({b.TrangThai})");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> LichBtAsync(string vt, CancellationToken ct)
        {
            if (vt == "Nhân viên kỹ thuật")
                return "Dạ, lịch kế hoạch do Tổ trưởng/Xưởng quản lý. NVKT xem việc được phân công.";

            var list = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.NgayDuKienBaoTri >= DateOnly.FromDateTime(DateTime.Now))
                .OrderBy(c => c.NgayDuKienBaoTri).Take(15)
                .Select(c => new
                {
                    c.NgayDuKienBaoTri,
                    Ten = c.MaThietBiNavigation != null ? c.MaThietBiNavigation.TenThietBi : ("TB#" + c.MaThietBi)
                }).ToListAsync(ct);

            if (list.Count == 0)
                return "Dạ, chưa có lịch bảo trì dự kiến sắp tới.";

            var sb = new StringBuilder();
            sb.AppendLine("Dạ, lịch bảo trì dự kiến sắp tới:");
            foreach (var x in list)
                sb.AppendLine($"• {x.NgayDuKienBaoTri:dd/MM/yyyy}: {x.Ten}");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> PhanCongNvktAsync(int maNd, string vt, CancellationToken ct)
        {
            if (vt != "Nhân viên kỹ thuật")
                return "Dạ, intent «phân công cá nhân» dành cho NVKT. Tổ trưởng hỏi số HS hoặc danh sách thiết bị giúp mình.";

            var maNv = await _db.NhanViens.AsNoTracking()
                .Where(n => n.MaNguoiDung == maNd)
                .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
            if (maNv == null) return "Dạ, không xác định được nhân viên.";

            var pc = await _db.PhanCongCongViecs.AsNoTracking()
                .Where(p => p.MaNhanVienThucHien == maNv)
                .OrderByDescending(p => p.NgayPhanCong).Take(20)
                .Select(p => new { p.MaPhanCong, p.MaHoSoBaoTri, p.MaHoSoSuaChua, p.TrangThai, p.NgayPhanCong })
                .ToListAsync(ct);

            if (pc.Count == 0)
                return "Dạ, bạn chưa có phân công nào trên hệ thống.";

            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, bạn có **{pc.Count}** phân công gần đây:");
            foreach (var g in pc.GroupBy(x => x.TrangThai ?? "(trống)"))
                sb.AppendLine($"• {g.Key}: {g.Count()}");
            foreach (var p in pc.Take(8))
            {
                var hs = p.MaHoSoBaoTri != null ? $"BT#{p.MaHoSoBaoTri}" : $"SC#{p.MaHoSoSuaChua}";
                sb.AppendLine($"• PC#{p.MaPhanCong} | {hs} | {p.TrangThai} | {p.NgayPhanCong:dd/MM/yyyy}");
            }
            return sb.ToString().TrimEnd();
        }

        // ===================== Helpers =====================

        private static string? KiemTraVuotQuyen(string vt, string q)
        {
            if (vt is "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật")
            {
                if (Co(q, "thay giam doc") && Co(q, "duyet"))
                    return "Tổ trưởng không duyệt hộ Giám đốc.";
            }
            if (vt == "Nhân viên kỹ thuật")
            {
                if (Co(q, "toan bo cong ty", "tat ca nhan vien", "duyet ho so giam doc"))
                    return "NVKT chỉ xem công việc được phân công trên tài khoản của bạn.";
            }
            return null;
        }

        private static List<string> TachCau(string raw)
        {
            var text = raw.Trim();
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (lines.Count >= 2) return lines;
            var byQ = Regex.Split(text, @"(?<=\?)\s+").Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (byQ.Count >= 2) return byQ;
            return new List<string> { text };
        }

        private static string ChuanHoa(string input)
        {
            var s = input.Trim().ToLowerInvariant();
            s = Regex.Replace(s, @"\s+", " ");
            var norm = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in norm)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC).Replace('đ', 'd');
        }

        private static bool Co(string q, params string[] keys) => keys.Any(k => q.Contains(k));

        private static bool LaNhayCam(string q)
        {
            string[] cam = { "mat khau", "password", "connection string", "jwt", "secret", "api key", "source code", "ma nguon", "appsettings" };
            return cam.Any(k => q.Contains(k));
        }

        private static int? TrichThang(string q)
        {
            var m = Regex.Match(q, @"thang\s*(\d{1,2})");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var t1) && t1 is >= 1 and <= 12)
                return t1;
            m = Regex.Match(q, @"\b(\d{1,2})\s*[/\-]\s*(20\d{2})\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var t2) && t2 is >= 1 and <= 12)
                return t2;
            return null;
        }

        private static int? TrichNam(string q)
        {
            var m = Regex.Match(q, @"(20\d{2})");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var y) && y is >= 2020 and <= 2100)
                return y;
            return null;
        }

        private static int? TrichMa(string q, string prefix)
        {
            var m = Regex.Match(q, $@"\b{prefix}\s*#?\s*(\d+)\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var id))
                return id;
            return null;
        }

        private static int DiemKhop(string q, string ten)
        {
            var t = ChuanHoa(ten);
            if (string.IsNullOrEmpty(t)) return 0;
            if (q.Contains(t) || t.Contains(q)) return 10;
            var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Count(w => w.Length >= 2 && q.Contains(w));
        }
    }
}
