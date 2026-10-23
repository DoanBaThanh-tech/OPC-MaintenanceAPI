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
                    $"{ten} ơi, mình không chia sẻ code, mật khẩu hay cấu hình bảo mật. "
                    + "Hỏi việc trên hệ thống theo vai trò của bạn — mình trả lời từ dữ liệu thật.",
                    null);
            }

            // 1) Nạp dữ liệu đầy đủ theo vai trò (không phụ thuộc câu hỏi mẫu)
            string boDuLieu;
            try
            {
                boDuLieu = await NapDuLieuTheoVaiTroAsync(maNguoiDung, vt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi nạp dữ liệu vai trò");
                return (false, null, "Không đọc được dữ liệu hệ thống lúc này. Thử lại sau ít giây.");
            }

            // 2) Gọi AI với toàn bộ ngữ cảnh — trả lời đúng câu user hỏi
            return await GoiOllamaVoiDuLieuAsync(vt, ten, cauHoi, boDuLieu, lichSu, ct);
        }

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
                    h.NgayThucHien
                }).ToListAsync(ct);

            sb.AppendLine(chiMaNv != null
                ? "[Hồ sơ vật tư / tổng tiền quy trình — CỦA NHÂN VIÊN NÀY]"
                : "[Hồ sơ vật tư gần đây]");
            if (list.Count == 0) sb.AppendLine("  (không có)");
            foreach (var h in list)
            {
                var hs = h.MaHoSoBaoTri != null ? $"BT#{h.MaHoSoBaoTri}" : $"SC#{h.MaHoSoSuaChua}";
                sb.AppendLine($"  {hs} | {h.NgayThucHien:dd/MM/yyyy HH:mm} | {h.LoaiCongViec} | {h.TenThietBi} | {h.TongTien:N0}₫ | {h.TrangThai}");
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
            system.AppendLine("Bạn là OPC Bot — trợ lý nội bộ app quản lý bảo trì & sửa chữa thiết bị cơ điện (công ty dược OPC).");
            system.AppendLine($"Người dùng: {hoTen}. Vai trò: «{vaiTro}».");
            system.AppendLine("Xưng «mình», nói tiếng Việt rõ ràng, thân thiện.");
            system.AppendLine();
            system.AppendLine("NHIỆM VỤ:");
            system.AppendLine("- Đọc kỹ câu hỏi của user (kể cả thiếu dấu, viết tắt, câu nói miệng).");
            system.AppendLine("- Trả lời ĐÚNG ý hỏi bằng dữ liệu trong khối DỮ LIỆU HỆ THỐNG bên dưới.");
            system.AppendLine("- Số lượng, trạng thái, giờ, bước quy trình, tổng tiền, tên thiết bị, mã hồ sơ… phải lấy từ dữ liệu, nêu số/giờ cụ thể.");
            system.AppendLine("- CẤM trả lời kiểu «bạn vào app xem», «mở màn hình kiểm tra», «hệ thống chưa cập nhật» nếu dữ liệu bên dưới đã có thông tin.");
            system.AppendLine("- Chỉ khi dữ liệu bên dưới thật sự không có thông tin liên quan mới nói: chưa có trên hệ thống / chưa ghi nhận — và nêu những gì ĐANG có liên quan.");
            system.AppendLine("- Chỉ trả lời trong phạm vi vai trò «" + vaiTro + "». Hỏi ngoài phạm vi → từ chối nhẹ.");
            system.AppendLine("- CẤM lộ code, mật khẩu, connection string, JWT, API key.");
            system.AppendLine();
            system.AppendLine("DỮ LIỆU HỆ THỐNG (realtime, đã lọc theo vai trò) — đây là nguồn sự thật duy nhất:");
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
                content = "Câu hỏi (có thể thiếu dấu câu): " + cauHoi.Trim()
                          + "\n\nHãy trả lời cụ thể dựa trên DỮ LIỆU HỆ THỐNG. Không bảo tôi vào app tự xem."
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
                        num_predict = 900
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
