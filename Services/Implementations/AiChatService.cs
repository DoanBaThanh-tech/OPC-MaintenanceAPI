using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    /// <summary>
    /// OPC Bot v2 — State Memory + trả lời trực tiếp từ DB (≤2s).
    /// Không phụ thuộc Ollama cho câu hỏi số liệu; phân quyền theo vai trò JWT.
    /// </summary>
    public class AiChatService : IAiChatService
    {
        private readonly OPCDbContext _db;
        private readonly ILogger<AiChatService> _logger;

        /// <summary>Cache state theo (maNguoiDung, vaiTro) — TTL 30s.</summary>
        private static readonly ConcurrentDictionary<string, (DateTime At, RoleState State)> StateCache = new();
        private static readonly TimeSpan StateTtl = TimeSpan.FromSeconds(30);

        public AiChatService(OPCDbContext db, ILogger<AiChatService> logger, IConfiguration _)
        {
            _db = db;
            _logger = logger;
        }

        public Task<(bool online, string? model, string? loi)> KiemTraAsync(CancellationToken ct = default)
            => Task.FromResult<(bool, string?, string?)>((true, "OPC-StateEngine/v2", null));

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
            var q = ChuanHoa(cauHoi);

            if (LaNhayCam(q))
                return (true, $"Dạ, mình không chia sẻ thông tin bảo mật hệ thống. Hỏi nghiệp vụ trong phạm vi «{vt}» giúp mình.", null);

            // Chặn hỏi ngoài phạm vi vai trò
            var chan = KiemTraVuotQuyen(vt, q);
            if (chan != null)
                return (true, $"Dạ, {chan}", null);

            try
            {
                var state = await LayHoacNapStateAsync(maNguoiDung, vt, ct);
                var cacCau = TachCau(cauHoi);
                var parts = new List<string>();

                foreach (var (i, cau) in cacCau.Select((c, idx) => (idx + 1, c)))
                {
                    var qi = ChuanHoa(cau);
                    var ans = await TraLoiTuStateVaDbAsync(maNguoiDung, vt, ten, qi, cau, state, ct);
                    if (cacCau.Count > 1)
                        parts.Add($"**Yêu cầu {i}:** {cau.Trim()}\n{ans}");
                    else
                        parts.Add(ans);
                }

                return (true, string.Join("\n\n", parts), null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OPC Bot lỗi");
                return (false, null, "Mình gặp lỗi khi đọc dữ liệu hệ thống. Bạn thử lại giúp mình nhé.");
            }
        }

        // ===================== STATE MEMORY =====================

        private async Task<RoleState> LayHoacNapStateAsync(int maNd, string vt, CancellationToken ct)
        {
            var key = $"{maNd}|{vt}";
            if (StateCache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < StateTtl)
                return hit.State;

            var state = await NapStateAsync(maNd, vt, ct);
            StateCache[key] = (DateTime.UtcNow, state);
            return state;
        }

        private async Task<RoleState> NapStateAsync(int maNd, string vt, CancellationToken ct)
        {
            var s = new RoleState { VaiTro = vt, TaiLuc = DateTime.Now };

            // Thiết bị — mọi vai trò nghiệp vụ đều có thể tra cứu (trừ Admin)
            if (vt is not "Admin hệ thống")
            {
                var tbs = await _db.ThietBis.AsNoTracking()
                    .Select(t => new TbItem
                    {
                        Ma = t.MaThietBi,
                        Ten = t.TenThietBi,
                        Loai = t.LoaiThietBi ?? "",
                        ViTri = t.ViTriLapDat ?? "",
                        TinhTrang = t.TinhTrangHienTai ?? "",
                        BtGanNhat = t.NgayBaoTriGanNhat,
                        BtTiepTheo = t.NgayBaoTriTiepTheo
                    }).ToListAsync(ct);
                s.ThietBis = tbs;
                s.DemTheoTinhTrang = tbs.GroupBy(x => string.IsNullOrWhiteSpace(x.TinhTrang) ? "(trống)" : x.TinhTrang)
                    .ToDictionary(g => g.Key, g => g.Count());
                s.DemTheoLoai = tbs.GroupBy(x => string.IsNullOrWhiteSpace(x.Loai) ? "(chưa phân loại)" : x.Loai)
                    .ToDictionary(g => g.Key, g => g.Count());
            }

            if (vt is "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" or "Xưởng" or "Tổ trưởng sản xuất" or "Giám đốc")
            {
                s.BtTheoTt = await DemHsBtAsync(ct);
                s.ScTheoTt = await DemHsScAsync(ct);

                s.BtGanDay = await _db.HoSoBaoTris.AsNoTracking()
                    .OrderByDescending(h => h.NgayTao).Take(25)
                    .Select(h => new HsItem
                    {
                        Ma = h.MaHoSoBaoTri,
                        TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "",
                        TrangThai = h.TrangThai,
                        NgayTao = h.NgayTao,
                        Bd = h.ThoiDiemBatDauThucTe,
                        Ht = h.ThoiDiemKetThucThucTe
                    }).ToListAsync(ct);

                s.ScGanDay = await _db.HoSoSuaChuas.AsNoTracking()
                    .OrderByDescending(h => h.NgayTao).Take(25)
                    .Select(h => new HsItem
                    {
                        Ma = h.MaHoSoSuaChua,
                        TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "",
                        TrangThai = h.TrangThai,
                        NgayTao = h.NgayTao,
                        Bd = h.ThoiDiemBatDauThucTe,
                        Ht = h.ThoiDiemKetThucThucTe
                    }).ToListAsync(ct);

                // Vật tư đã GĐ xác nhận + chờ duyệt
                s.VatTu = await _db.HoSoSuDungVatTus.AsNoTracking()
                    .OrderByDescending(h => h.NgayThucHien).Take(30)
                    .Select(h => new VtItem
                    {
                        MaBt = h.MaHoSoBaoTri,
                        MaSc = h.MaHoSoSuaChua,
                        TenTb = h.TenThietBi ?? "",
                        TongTien = h.TongTien,
                        TrangThai = h.TrangThai,
                        Ngay = h.NgayThucHien,
                        SoLoai = h.ChiTietSuDungVatTus.Select(c => c.TenVatTu).Distinct().Count(),
                        TongSl = h.ChiTietSuDungVatTus.Sum(c => c.SoLuong)
                    }).ToListAsync(ct);

                // Tiến độ bước gần đây
                var tienDo = await _db.TienDoBuocQuyTrinhs.AsNoTracking()
                    .OrderByDescending(t => t.ThoiDiemCapNhat).Take(200)
                    .Select(t => new { t.MaHoSoBaoTri, t.MaHoSoSuaChua, t.SoBuoc, t.MoTaBuoc, t.TrangThai, t.ThoiDiemCapNhat })
                    .ToListAsync(ct);
                s.BuocTheoHs = tienDo
                    .GroupBy(t => t.MaHoSoBaoTri != null ? $"BT#{t.MaHoSoBaoTri}" : $"SC#{t.MaHoSoSuaChua}")
                    .Select(g =>
                    {
                        var uniq = g.GroupBy(x => x.SoBuoc).Select(x => x.OrderByDescending(i => i.ThoiDiemCapNhat).First()).ToList();
                        return new BuocHsItem
                        {
                            Key = g.Key,
                            SoBuoc = uniq.Count,
                            DaXong = uniq.Count(x => x.TrangThai == "DaXong"),
                            DangLam = uniq.Count(x => x.TrangThai == "DangLam"),
                            ChiTiet = uniq.OrderBy(x => x.SoBuoc).Select(x => $"B{x.SoBuoc}:{x.MoTaBuoc}({x.TrangThai})").ToList()
                        };
                    }).Take(20).ToList();

                // Lịch BT sắp tới
                s.LichBt = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                    .Where(c => c.NgayDuKienBaoTri >= DateOnly.FromDateTime(DateTime.Now))
                    .OrderBy(c => c.NgayDuKienBaoTri).Take(20)
                    .Select(c => new LichItem
                    {
                        Ngay = c.NgayDuKienBaoTri,
                        TenTb = c.MaThietBiNavigation != null ? c.MaThietBiNavigation.TenThietBi : ("TB#" + c.MaThietBi)
                    }).ToListAsync(ct);
            }

            if (vt is "Xưởng" or "Tổ trưởng sản xuất")
            {
                s.BtChoChot = s.BtTheoTt.GetValueOrDefault("Chờ xưởng", 0)
                              + s.BtTheoTt.GetValueOrDefault("Chờ chốt ngày", 0);
                s.BtTuChoiVeXuong = s.BtTheoTt.GetValueOrDefault("Từ chối", 0);
            }

            if (vt is "Giám đốc")
            {
                s.BtChoDuyet = s.BtTheoTt.GetValueOrDefault("Chờ duyệt", 0);
                s.ScChoDuyet = s.ScTheoTt.GetValueOrDefault("Chờ duyệt", 0);
                var vtXn = s.VatTu.Where(v => v.TrangThai is "Xác nhận" or "Đã duyệt" or "Đã xác nhận").ToList();
                s.TongTienVtXn = vtXn.Sum(v => v.TongTien);
            }

            if (vt == "Nhân viên kỹ thuật")
            {
                var maNv = await _db.NhanViens.AsNoTracking()
                    .Where(n => n.MaNguoiDung == maNd)
                    .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
                s.MaNhanVien = maNv;
                if (maNv != null)
                {
                    var pc = await _db.PhanCongCongViecs.AsNoTracking()
                        .Where(p => p.MaNhanVienThucHien == maNv)
                        .OrderByDescending(p => p.NgayPhanCong).Take(40)
                        .Select(p => new PcItem
                        {
                            MaPc = p.MaPhanCong,
                            MaBt = p.MaHoSoBaoTri,
                            MaSc = p.MaHoSoSuaChua,
                            TrangThai = p.TrangThai ?? "",
                            Ngay = p.NgayPhanCong
                        }).ToListAsync(ct);
                    s.PhanCongs = pc;

                    var maBt = pc.Where(x => x.MaBt != null).Select(x => x.MaBt!.Value).Distinct().ToList();
                    var maSc = pc.Where(x => x.MaSc != null).Select(x => x.MaSc!.Value).Distinct().ToList();

                    if (maBt.Count > 0)
                        s.BtGanDay = await _db.HoSoBaoTris.AsNoTracking().Where(h => maBt.Contains(h.MaHoSoBaoTri))
                            .Select(h => new HsItem
                            {
                                Ma = h.MaHoSoBaoTri,
                                TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "",
                                TrangThai = h.TrangThai,
                                NgayTao = h.NgayTao,
                                Bd = h.ThoiDiemBatDauThucTe,
                                Ht = h.ThoiDiemKetThucThucTe
                            }).ToListAsync(ct);
                    if (maSc.Count > 0)
                        s.ScGanDay = await _db.HoSoSuaChuas.AsNoTracking().Where(h => maSc.Contains(h.MaHoSoSuaChua))
                            .Select(h => new HsItem
                            {
                                Ma = h.MaHoSoSuaChua,
                                TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "",
                                TrangThai = h.TrangThai,
                                NgayTao = h.NgayTao,
                                Bd = h.ThoiDiemBatDauThucTe,
                                Ht = h.ThoiDiemKetThucThucTe
                            }).ToListAsync(ct);

                    var tienDo = await _db.TienDoBuocQuyTrinhs.AsNoTracking()
                        .Where(t =>
                            (t.MaHoSoBaoTri != null && maBt.Contains(t.MaHoSoBaoTri.Value)) ||
                            (t.MaHoSoSuaChua != null && maSc.Contains(t.MaHoSoSuaChua.Value)))
                        .OrderByDescending(t => t.ThoiDiemCapNhat).Take(200)
                        .Select(t => new { t.MaHoSoBaoTri, t.MaHoSoSuaChua, t.SoBuoc, t.MoTaBuoc, t.TrangThai, t.ThoiDiemCapNhat })
                        .ToListAsync(ct);
                    s.BuocTheoHs = tienDo
                        .GroupBy(t => t.MaHoSoBaoTri != null ? $"BT#{t.MaHoSoBaoTri}" : $"SC#{t.MaHoSoSuaChua}")
                        .Select(g =>
                        {
                            var uniq = g.GroupBy(x => x.SoBuoc).Select(x => x.OrderByDescending(i => i.ThoiDiemCapNhat).First()).ToList();
                            return new BuocHsItem
                            {
                                Key = g.Key,
                                SoBuoc = uniq.Count,
                                DaXong = uniq.Count(x => x.TrangThai == "DaXong"),
                                DangLam = uniq.Count(x => x.TrangThai == "DangLam"),
                                ChiTiet = uniq.OrderBy(x => x.SoBuoc).Select(x => $"B{x.SoBuoc}:{x.MoTaBuoc}({x.TrangThai})").ToList()
                            };
                        }).ToList();

                    s.VatTu = await _db.HoSoSuDungVatTus.AsNoTracking()
                        .Where(h => h.MaNhanVienTH == maNv)
                        .OrderByDescending(h => h.NgayThucHien).Take(20)
                        .Select(h => new VtItem
                        {
                            MaBt = h.MaHoSoBaoTri,
                            MaSc = h.MaHoSoSuaChua,
                            TenTb = h.TenThietBi ?? "",
                            TongTien = h.TongTien,
                            TrangThai = h.TrangThai,
                            Ngay = h.NgayThucHien,
                            SoLoai = h.ChiTietSuDungVatTus.Select(c => c.TenVatTu).Distinct().Count(),
                            TongSl = h.ChiTietSuDungVatTus.Sum(c => c.SoLuong)
                        }).ToListAsync(ct);
                }
            }

            return s;
        }

        private async Task<Dictionary<string, int>> DemHsBtAsync(CancellationToken ct)
        {
            var g = await _db.HoSoBaoTris.AsNoTracking()
                .GroupBy(h => h.TrangThai ?? "(trống)")
                .Select(x => new { x.Key, So = x.Count() }).ToListAsync(ct);
            return g.ToDictionary(x => x.Key, x => x.So);
        }

        private async Task<Dictionary<string, int>> DemHsScAsync(CancellationToken ct)
        {
            var g = await _db.HoSoSuaChuas.AsNoTracking()
                .GroupBy(h => h.TrangThai ?? "(trống)")
                .Select(x => new { x.Key, So = x.Count() }).ToListAsync(ct);
            return g.ToDictionary(x => x.Key, x => x.So);
        }

        // ===================== TRẢ LỜI =====================

        private async Task<string> TraLoiTuStateVaDbAsync(
            int maNd, string vt, string ten, string q, string cauGoc, RoleState st, CancellationToken ct)
        {
            // --- Thiết bị / danh mục / lịch BT ---
            if (Co(q, "thiet bi", "danh muc", "tinh trang", "san xuat", "bom", "may "))
            {
                var tbAns = TraLoiThietBi(ten, q, st);
                if (tbAns != null) return tbAns;
            }

            // --- Vật tư ---
            if (Co(q, "vat tu", "so luong vat", "tong tien", "chi phi"))
                return TraLoiVatTu(ten, q, st, vt);

            // --- Bước quy trình ---
            if (Co(q, "buoc", "quy trinh", "tien do"))
                return TraLoiBuoc(ten, q, st);

            // --- Số hồ sơ / trạng thái HS ---
            if (Co(q, "ho so", "bao tri", "sua chua", "hoan thanh", "cho duyet", "cho xuong", "phan cong"))
            {
                var hs = TraLoiHoSo(ten, q, st, vt);
                if (hs != null) return hs;
            }

            // --- Lịch ---
            if (Co(q, "lich", "sap toi", "ke hoach", "ngay du kien"))
                return TraLoiLich(ten, st, vt);

            // --- Hướng dẫn (chỉ khi hỏi rõ cách làm) ---
            if (Co(q, "cach", "lam sao", "huong dan", "phai lam", "quy trinh tao", "phan cong nhu"))
                return HuongDanVaiTro(ten, vt, q);

            // --- Chào ngắn ---
            if (Co(q, "xin chao", "hello", "chao", "giup gi", "ban la ai"))
                return $"Dạ, chào {ten}. Vai trò «{vt}». Hỏi số liệu hoặc thao tác trong phạm vi của bạn, mình trả lời ngay từ hệ thống.";

            // Không khớp → ngắn, không lan man
            return $"Dạ, mình chưa khớp đúng dữ liệu cho câu hỏi này trong phạm vi «{vt}». "
                   + "Bạn nêu rõ tên thiết bị, mã hồ sơ (BT#/SC#) hoặc loại số liệu cần xem giúp mình.";
        }

        private string? TraLoiThietBi(string ten, string q, RoleState st)
        {
            if (st.ThietBis.Count == 0)
                return $"Dạ, phạm vi «{st.VaiTro}» hiện không có dữ liệu danh sách thiết bị.";

            // Khớp tên thiết bị
            var candidates = st.ThietBis
                .Select(tb => new { Tb = tb, Score = DiemKhopTen(q, tb.Ten) })
                .Where(x => x.Score >= 2)
                .OrderByDescending(x => x.Score)
                .Take(5)
                .ToList();

            if (candidates.Count > 0 && (Co(q, "trang thai", "tinh trang", "lich", "bao tri", "dang o", "tiep theo", "gan nhat") || candidates[0].Score >= 3))
            {
                var tb = candidates[0].Tb;
                var tt = string.IsNullOrWhiteSpace(tb.TinhTrang) ? "chưa ghi" : tb.TinhTrang;
                var gan = tb.BtGanNhat.HasValue ? tb.BtGanNhat.Value.ToString("dd/MM/yyyy") : "chưa có";
                var tiep = tb.BtTiepTheo.HasValue ? tb.BtTiepTheo.Value.ToString("dd/MM/yyyy") : "chưa có";
                return $"Dạ, thiết bị **{tb.Ten}** hiện đang ở trạng thái **{tt}**. "
                     + $"Lịch bảo trì gần nhất là **{gan}** và tiếp theo là **{tiep}** ạ.";
            }

            // Tổng quan: danh mục + 4 nhóm tình trạng
            if (Co(q, "bao nhieu", "danh muc", "tong", "co may", "tinh trang", "san xuat", "thiet bi", "tat ca"))
            {
                int DemTt(string key) =>
                    st.DemTheoTinhTrang
                        .Where(kv => kv.Key.Contains(key, StringComparison.OrdinalIgnoreCase))
                        .Sum(kv => kv.Value);
                var sx = DemTt("Sản xuất");
                var bt = DemTt("Bảo trì");
                var sc = DemTt("Sửa chữa");
                var sb = new StringBuilder();
                sb.AppendLine($"Dạ, tổng **{st.ThietBis.Count}** thiết bị (Tất cả).");
                sb.AppendLine($"• Sản xuất: **{sx}** | Bảo trì: **{bt}** | Sửa chữa: **{sc}**.");
                if (st.DemTheoLoai.Count > 0)
                {
                    sb.AppendLine("Theo danh mục:");
                    foreach (var kv in st.DemTheoLoai.OrderByDescending(x => x.Value))
                        sb.AppendLine($"• {kv.Key}: **{kv.Value}**");
                }
                // Liệt kê tên TB đang BT / SC nếu hỏi trạng thái
                if (Co(q, "bao tri", "sua chua", "dang") || bt + sc > 0)
                {
                    var dangBt = st.ThietBis.Where(x => (x.TinhTrang ?? "").Contains("Bảo trì", StringComparison.OrdinalIgnoreCase)).Take(10).ToList();
                    var dangSc = st.ThietBis.Where(x => (x.TinhTrang ?? "").Contains("Sửa chữa", StringComparison.OrdinalIgnoreCase)).Take(10).ToList();
                    if (dangBt.Count > 0)
                    {
                        sb.AppendLine("Thiết bị đang Bảo trì:");
                        foreach (var x in dangBt)
                            sb.AppendLine($"• {x.Ten}" + (x.BtTiepTheo.HasValue ? $" (BT tiếp theo {x.BtTiepTheo:dd/MM/yyyy})" : ""));
                    }
                    if (dangSc.Count > 0)
                    {
                        sb.AppendLine("Thiết bị đang Sửa chữa:");
                        foreach (var x in dangSc)
                            sb.AppendLine($"• {x.Ten}");
                    }
                }
                return sb.ToString().TrimEnd();
            }

            return null;
        }

        private static int DiemKhopTen(string q, string tenTb)
        {
            var t = ChuanHoa(tenTb);
            if (string.IsNullOrEmpty(t)) return 0;
            if (q.Contains(t)) return 10;
            var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Count(w => w.Length >= 2 && q.Contains(w));
        }

        private string TraLoiVatTu(string ten, string q, RoleState st, string vt)
        {
            if (st.VatTu.Count == 0)
                return $"Dạ, chưa có hồ sơ vật tư trong phạm vi của bạn.";

            VtItem focus;
            var mBt = Regex.Match(q, @"\bbt\s*#?\s*(\d+)\b");
            var mSc = Regex.Match(q, @"\bsc\s*#?\s*(\d+)\b");
            if (mBt.Success && int.TryParse(mBt.Groups[1].Value, out var idb))
                focus = st.VatTu.FirstOrDefault(v => v.MaBt == idb) ?? st.VatTu[0];
            else if (mSc.Success && int.TryParse(mSc.Groups[1].Value, out var ids))
                focus = st.VatTu.FirstOrDefault(v => v.MaSc == ids) ?? st.VatTu[0];
            else if (Co(q, "bao tri"))
                focus = st.VatTu.FirstOrDefault(v => v.MaBt != null) ?? st.VatTu[0];
            else if (Co(q, "sua chua"))
                focus = st.VatTu.FirstOrDefault(v => v.MaSc != null) ?? st.VatTu[0];
            else
                focus = st.VatTu[0];

            var hs = focus.MaBt != null ? $"BT#{focus.MaBt}" : $"SC#{focus.MaSc}";
            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, quy trình hồ sơ **{hs}** (thiết bị: {focus.TenTb}, {focus.Ngay:dd/MM/yyyy HH:mm}):");
            sb.AppendLine($"• **{focus.SoLoai} loại vật tư**, tổng số lượng **{focus.TongSl}**, tổng tiền **{focus.TongTien:N0} ₫**.");
            sb.AppendLine($"• Trạng thái phiếu vật tư: **{focus.TrangThai}**.");

            if (vt is "Giám đốc" && Co(q, "tong"))
            {
                sb.AppendLine();
                sb.AppendLine($"Tổng tiền vật tư đã xác nhận (trong dữ liệu gần đây): **{st.TongTienVtXn:N0} ₫**.");
            }
            return sb.ToString().TrimEnd();
        }

        private string TraLoiBuoc(string ten, string q, RoleState st)
        {
            if (st.BuocTheoHs.Count == 0)
                return $"Dạ, chưa ghi nhận bước quy trình nào trong phạm vi của bạn.";

            BuocHsItem focus;
            var mBt = Regex.Match(q, @"\bbt\s*#?\s*(\d+)\b");
            var mSc = Regex.Match(q, @"\bsc\s*#?\s*(\d+)\b");
            if (mBt.Success)
                focus = st.BuocTheoHs.FirstOrDefault(b => b.Key == $"BT#{mBt.Groups[1].Value}") ?? st.BuocTheoHs[0];
            else if (mSc.Success)
                focus = st.BuocTheoHs.FirstOrDefault(b => b.Key == $"SC#{mSc.Groups[1].Value}") ?? st.BuocTheoHs[0];
            else
                focus = st.BuocTheoHs[0];

            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, hồ sơ **{focus.Key}**: **{focus.SoBuoc} bước** đã chọn (xong: {focus.DaXong}, đang làm: {focus.DangLam}).");
            foreach (var c in focus.ChiTiet)
                sb.AppendLine($"• {c}");
            return sb.ToString().TrimEnd();
        }

        private string? TraLoiHoSo(string ten, string q, RoleState st, string vt)
        {
            if (vt == "Nhân viên kỹ thuật")
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Dạ, bạn có **{st.PhanCongs.Count}** phân công gần đây.");
                foreach (var g in st.PhanCongs.GroupBy(p => p.TrangThai))
                    sb.AppendLine($"• {g.Key}: {g.Count()}");
                if (st.BtGanDay.Count > 0)
                {
                    sb.AppendLine("Hồ sơ bảo trì liên quan:");
                    foreach (var h in st.BtGanDay.Take(8))
                        sb.AppendLine($"• BT#{h.Ma} | {h.TenTb} | {h.TrangThai}"
                                      + (h.Ht != null ? $" | HT {h.Ht:dd/MM/yyyy HH:mm}" : ""));
                }
                if (st.ScGanDay.Count > 0)
                {
                    sb.AppendLine("Hồ sơ sửa chữa liên quan:");
                    foreach (var h in st.ScGanDay.Take(8))
                        sb.AppendLine($"• SC#{h.Ma} | {h.TenTb} | {h.TrangThai}");
                }
                return sb.ToString().TrimEnd();
            }

            var uuBt = Co(q, "bao tri") || !Co(q, "sua chua");
            var dict = uuBt ? st.BtTheoTt : st.ScTheoTt;
            var loai = uuBt ? "bảo trì" : "sửa chữa";
            if (dict.Count == 0 && st.BtTheoTt.Count == 0 && st.ScTheoTt.Count == 0)
                return null;

            if (Co(q, "hoan thanh"))
            {
                var n = dict.GetValueOrDefault("Đã hoàn thành", 0);
                var sb = new StringBuilder();
                sb.AppendLine($"Dạ, có **{n}** hồ sơ {loai} **Đã hoàn thành**.");
                var list = (uuBt ? st.BtGanDay : st.ScGanDay).Where(h => h.TrangThai == "Đã hoàn thành").Take(5);
                foreach (var h in list)
                {
                    var ht = h.Ht?.ToString("dd/MM/yyyy HH:mm") ?? "chưa ghi giờ";
                    sb.AppendLine($"• {(uuBt ? "BT" : "SC")}#{h.Ma} | {h.TenTb} | hoàn thành {ht}");
                }
                return sb.ToString().TrimEnd();
            }

            if (Co(q, "cho duyet") && vt is "Giám đốc")
                return $"Dạ, chờ duyệt: BT **{st.BtChoDuyet}**, SC **{st.ScChoDuyet}**.";

            if (Co(q, "cho xuong", "chot ngay") && vt is "Xưởng" or "Tổ trưởng sản xuất")
                return $"Dạ, hồ sơ cần Xưởng xử lý: **{st.BtChoChot}**. Bị trả về (Từ chối): **{st.BtTuChoiVeXuong}**.";

            var tong = dict.Values.Sum();
            var sb2 = new StringBuilder();
            sb2.AppendLine($"Dạ, tổng hồ sơ {loai}: **{tong}**.");
            foreach (var kv in dict.OrderByDescending(x => x.Value))
                sb2.AppendLine($"• {kv.Key}: **{kv.Value}**");
            return sb2.ToString().TrimEnd();
        }

        private string TraLoiLich(string ten, RoleState st, string vt)
        {
            if (vt is "Nhân viên kỹ thuật")
                return $"Dạ, lịch kế hoạch do Tổ trưởng/Xưởng quản lý. NVKT xem việc được phân công trên tài khoản.";
            if (st.LichBt.Count == 0)
                return $"Dạ, chưa có lịch bảo trì dự kiến sắp tới.";
            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, lịch bảo trì dự kiến sắp tới:");
            foreach (var x in st.LichBt)
                sb.AppendLine($"• {x.Ngay:dd/MM/yyyy}: {x.TenTb}");
            return sb.ToString().TrimEnd();
        }

        private static string HuongDanVaiTro(string ten, string vt, string q)
        {
            return vt switch
            {
                "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" =>
                    "Dạ, Tổ trưởng: lập KH/HS bảo trì → phân công NVKT → theo dõi thiết bị & vật tư. Không duyệt hộ GĐ / không chốt ngày hộ Xưởng.",
                "Xưởng" or "Tổ trưởng sản xuất" =>
                    "Dạ, Xưởng: chốt ngày BT trong tháng → gửi GĐ; GĐ trả về thì sửa & gửi lại; xác nhận/từ chối quy trình NVKT.",
                "Giám đốc" =>
                    "Dạ, Giám đốc: duyệt/từ chối HS; xem lịch sử phê duyệt & thống kê VT đã xác nhận. Cần gợi ý lý do từ chối thì nói rõ hồ sơ.",
                "Nhân viên kỹ thuật" =>
                    "Dạ, NVKT: Tiến hành → làm từng bước + vật tư → Hoàn thành gửi Xưởng. Bị từ chối: Cập nhật quy trình theo lý do rồi gửi lại.",
                _ => $"Dạ, hỏi số liệu trong đúng vai trò «{vt}»."
            };
        }

        private static string? KiemTraVuotQuyen(string vt, string q)
        {
            // Tổ trưởng không bàn duyệt GĐ / chốt Xưởng chi tiết nghiệp vụ người khác
            if (vt is "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật")
            {
                if (Co(q, "toi duyet ho so", "giam doc duyet", "phe duyet giup") && Co(q, "thay giam doc"))
                    return "mình không thực hiện phê duyệt hộ Giám đốc được. Tổ trưởng chỉ tạo HS, phân công và theo dõi trong phạm vi của mình.";
            }
            if (vt == "Nhân viên kỹ thuật")
            {
                if (Co(q, "duyet ho so", "chot ngay", "toan bo cong ty", "tat ca nhan vien"))
                    return "phạm vi NVKT chỉ gồm công việc được phân công trên tài khoản của bạn, không xem dữ liệu toàn công ty hay phê duyệt.";
            }
            return null;
        }

        // ===================== Helpers =====================

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

        // ===================== State models =====================

        private sealed class RoleState
        {
            public string VaiTro { get; set; } = "";
            public DateTime TaiLuc { get; set; }
            public List<TbItem> ThietBis { get; set; } = new();
            public Dictionary<string, int> DemTheoTinhTrang { get; set; } = new();
            public Dictionary<string, int> DemTheoLoai { get; set; } = new();
            public Dictionary<string, int> BtTheoTt { get; set; } = new();
            public Dictionary<string, int> ScTheoTt { get; set; } = new();
            public List<HsItem> BtGanDay { get; set; } = new();
            public List<HsItem> ScGanDay { get; set; } = new();
            public List<VtItem> VatTu { get; set; } = new();
            public List<BuocHsItem> BuocTheoHs { get; set; } = new();
            public List<LichItem> LichBt { get; set; } = new();
            public List<PcItem> PhanCongs { get; set; } = new();
            public int? MaNhanVien { get; set; }
            public int BtChoChot { get; set; }
            public int BtTuChoiVeXuong { get; set; }
            public int BtChoDuyet { get; set; }
            public int ScChoDuyet { get; set; }
            public decimal TongTienVtXn { get; set; }
        }

        private sealed class TbItem
        {
            public int Ma { get; set; }
            public string Ten { get; set; } = "";
            public string Loai { get; set; } = "";
            public string ViTri { get; set; } = "";
            public string TinhTrang { get; set; } = "";
            public DateOnly? BtGanNhat { get; set; }
            public DateOnly? BtTiepTheo { get; set; }
        }

        private sealed class HsItem
        {
            public int Ma { get; set; }
            public string TenTb { get; set; } = "";
            public string TrangThai { get; set; } = "";
            public DateTime NgayTao { get; set; }
            public DateTime? Bd { get; set; }
            public DateTime? Ht { get; set; }
        }

        private sealed class VtItem
        {
            public int? MaBt { get; set; }
            public int? MaSc { get; set; }
            public string TenTb { get; set; } = "";
            public decimal TongTien { get; set; }
            public string TrangThai { get; set; } = "";
            public DateTime Ngay { get; set; }
            public int SoLoai { get; set; }
            public int TongSl { get; set; }
        }

        private sealed class BuocHsItem
        {
            public string Key { get; set; } = "";
            public int SoBuoc { get; set; }
            public int DaXong { get; set; }
            public int DangLam { get; set; }
            public List<string> ChiTiet { get; set; } = new();
        }

        private sealed class LichItem
        {
            public DateOnly Ngay { get; set; }
            public string TenTb { get; set; } = "";
        }

        private sealed class PcItem
        {
            public int MaPc { get; set; }
            public int? MaBt { get; set; }
            public int? MaSc { get; set; }
            public string TrangThai { get; set; } = "";
            public DateTime Ngay { get; set; }
        }
    }
}
