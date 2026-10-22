using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    /// <summary>
    /// AI local — trả lời SỐ LIỆU / HƯỚNG DẪN cụ thể theo vai trò, không đẩy user tự tìm trong app.
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

            if (LaCauHoiNhayCam(cauHoi))
            {
                return (true,
                    "Mình không thể chia sẻ code, mật khẩu, connection string, token hay cấu hình bảo mật của hệ thống nhé. "
                    + "Bạn hỏi việc trên app (hồ sơ, quy trình, số liệu, cách thao tác…) mình trả lời cụ thể ngay.",
                    null);
            }

            var soLieu = await LaySoLieuTheoVaiTroAsync(maNguoiDung, vaiTro, ct);
            var system = BuildSystemPrompt(vaiTro, hoTen, soLieu);

            var messages = new List<object> { new { role = "system", content = system } };

            if (lichSu != null)
            {
                foreach (var (role, content) in lichSu.TakeLast(10))
                {
                    if (string.IsNullOrWhiteSpace(content)) continue;
                    var r = role is "assistant" or "user" ? role : "user";
                    messages.Add(new { role = r, content = content.Trim() });
                }
            }

            messages.Add(new { role = "user", content = cauHoi.Trim() });

            try
            {
                var client = _httpFactory.CreateClient("Ollama");
                var payload = new
                {
                    model = Model,
                    stream = false,
                    messages,
                    options = new { temperature = 0.35, num_predict = 700 }
                };

                using var res = await client.PostAsJsonAsync($"{BaseUrl}/api/chat", payload, ct);
                var raw = await res.Content.ReadAsStringAsync(ct);
                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Ollama chat fail {Code}: {Body}", (int)res.StatusCode, raw);
                    return (false, null, "AI local chưa sẵn sàng. Kiểm tra Ollama và model.");
                }

                using var doc = JsonDocument.Parse(raw);
                string? text = null;
                if (doc.RootElement.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var contentEl))
                    text = contentEl.GetString();

                if (string.IsNullOrWhiteSpace(text))
                    return (false, null, "Mình chưa trả lời được lúc này, bạn thử hỏi lại nhé.");

                return (true, text.Trim(), null);
            }
            catch (TaskCanceledException)
            {
                return (false, null, "Câu trả lời hơi lâu. Bạn thử hỏi ngắn hơn giúp mình nhé.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi gọi Ollama");
                return (false, null, "Không gọi được AI local. Mở Ollama rồi thử lại.");
            }
        }

        private static bool LaCauHoiNhayCam(string cauHoi)
        {
            var t = cauHoi.ToLowerInvariant();
            string[] cam =
            {
                "mật khẩu", "mat khau", "password", "connection string", "connectionstring",
                "jwt", "secret key", "api key", "apikey", "token secret", "source code",
                "mã nguồn", "ma nguon", "file .cs", "appsettings", "sql server password",
                "sa password", "private key", "ngrok token"
            };
            return cam.Any(k => t.Contains(k));
        }

        private async Task<string> LaySoLieuTheoVaiTroAsync(
            int maNguoiDung, string vaiTro, CancellationToken ct)
        {
            var now = DateTime.Now;
            var sb = new StringBuilder();
            sb.AppendLine($"Thời điểm hệ thống: {now:dd/MM/yyyy HH:mm}.");
            sb.AppendLine($"Vai trò đang hỗ trợ: «{vaiTro}» — CHỈ dùng số liệu khối này để trả lời.");

            try
            {
                var maNv = await _db.NhanViens.AsNoTracking()
                    .Where(n => n.MaNguoiDung == maNguoiDung)
                    .Select(n => (int?)n.MaNhanVien)
                    .FirstOrDefaultAsync(ct);

                switch (vaiTro.Trim())
                {
                    case "Nhân viên kỹ thuật":
                        await AppendNvktAsync(sb, maNv, ct);
                        break;
                    case "Tổ trưởng cơ điện":
                    case "Tổ trưởng kỹ thuật":
                        await AppendToTruongAsync(sb, maNv, ct);
                        break;
                    case "Xưởng":
                    case "Tổ trưởng sản xuất":
                        await AppendXuongAsync(sb, ct);
                        break;
                    case "Giám đốc":
                        await AppendGiamDocAsync(sb, now, ct);
                        break;
                    case "Admin hệ thống":
                        await AppendAdminAsync(sb, ct);
                        break;
                    default:
                        sb.AppendLine("Không có bảng số liệu nghiệp vụ cho vai trò này.");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi lấy số liệu theo vai trò");
                sb.AppendLine("Không tải được số liệu lúc này.");
            }

            return sb.ToString();
        }

        /// <summary>Đếm hồ sơ theo trạng thái — khớp tab/lọc trên giao diện app.</summary>
        private static async Task AppendDemTrangThaiAsync(
            OPCDbContext db, StringBuilder sb, string tieuDe, bool baoTri, CancellationToken ct)
        {
            if (baoTri)
            {
                var groups = await db.HoSoBaoTris.AsNoTracking()
                    .GroupBy(h => h.TrangThai ?? "(trống)")
                    .Select(g => new { Tt = g.Key, So = g.Count() })
                    .ToListAsync(ct);
                var tong = groups.Sum(x => x.So);
                sb.AppendLine($"{tieuDe} — TỔNG: {tong} hồ sơ.");
                foreach (var g in groups.OrderByDescending(x => x.So))
                    sb.AppendLine($"  • {g.Tt}: {g.So}");

                // Các trạng thái hay hỏi — nêu rõ số 0 nếu không có
                int Dem(string ten) => groups.FirstOrDefault(x => x.Tt == ten)?.So ?? 0;
                sb.AppendLine($"  → Đã hoàn thành = {Dem("Đã hoàn thành")}");
                sb.AppendLine($"  → Đang thực hiện = {Dem("Đang thực hiện")}");
                sb.AppendLine($"  → Chờ duyệt / Chờ xưởng = {Dem("Chờ duyệt") + Dem("Chờ xưởng")}");
                sb.AppendLine($"  → Đã duyệt = {Dem("Đã duyệt")}");
                sb.AppendLine($"  → Từ chối = {Dem("Từ chối")}");
            }
            else
            {
                var groups = await db.HoSoSuaChuas.AsNoTracking()
                    .GroupBy(h => h.TrangThai ?? "(trống)")
                    .Select(g => new { Tt = g.Key, So = g.Count() })
                    .ToListAsync(ct);
                var tong = groups.Sum(x => x.So);
                sb.AppendLine($"{tieuDe} — TỔNG: {tong} hồ sơ.");
                foreach (var g in groups.OrderByDescending(x => x.So))
                    sb.AppendLine($"  • {g.Tt}: {g.So}");
                int Dem(string ten) => groups.FirstOrDefault(x => x.Tt == ten)?.So ?? 0;
                sb.AppendLine($"  → Đã hoàn thành = {Dem("Đã hoàn thành")}");
                sb.AppendLine($"  → Đang thực hiện = {Dem("Đang thực hiện")}");
            }
        }

        private async Task AppendNvktAsync(StringBuilder sb, int? maNv, CancellationToken ct)
        {
            sb.AppendLine("=== SỐ LIỆU CỦA CHÍNH BẠN (NVKT) ===");
            if (maNv == null)
            {
                sb.AppendLine("Tài khoản chưa gắn mã nhân viên — chưa có công việc.");
                return;
            }

            sb.AppendLine($"Mã NV của bạn: {maNv}.");

            var pc = await _db.PhanCongCongViecs.AsNoTracking()
                .Where(p => p.MaNhanVienThucHien == maNv)
                .OrderByDescending(p => p.NgayPhanCong)
                .Take(20)
                .Select(p => new { p.MaHoSoBaoTri, p.MaHoSoSuaChua, p.TrangThai, p.NgayPhanCong })
                .ToListAsync(ct);

            sb.AppendLine($"Số lệnh phân công của bạn (gần đây, tối đa 20): {pc.Count}");
            var pcGroup = pc.GroupBy(x => x.TrangThai ?? "(trống)")
                .Select(g => new { Tt = g.Key, So = g.Count() });
            foreach (var g in pcGroup)
                sb.AppendLine($"  • Phân công «{g.Tt}»: {g.So}");

            foreach (var p in pc.Take(8))
            {
                var loai = p.MaHoSoBaoTri != null ? $"BT#{p.MaHoSoBaoTri}" : $"SC#{p.MaHoSoSuaChua}";
                sb.AppendLine($"  - {loai} | {p.TrangThai} | {p.NgayPhanCong:dd/MM/yyyy}");
            }

            var vt = await _db.HoSoSuDungVatTus.AsNoTracking()
                .Where(h => h.MaNhanVienTH == maNv)
                .OrderByDescending(h => h.NgayThucHien)
                .Take(10)
                .Select(h => new
                {
                    h.TenThietBi, h.LoaiCongViec, h.TongTien, h.TrangThai, h.NgayThucHien,
                    h.MaHoSoBaoTri, h.MaHoSoSuaChua
                })
                .ToListAsync(ct);

            if (vt.Count == 0)
                sb.AppendLine("Bạn chưa có hồ sơ vật tư / tổng tiền quy trình nào.");
            else
            {
                var g = vt[0];
                sb.AppendLine("Quy trình GẦN NHẤT của bạn:");
                sb.AppendLine(
                    $"  Ngày {g.NgayThucHien:dd/MM/yyyy} | {g.LoaiCongViec} | {g.TenThietBi} | "
                    + $"Tổng tiền {g.TongTien:N0} ₫ | TT {g.TrangThai}");
                foreach (var h in vt.Skip(1).Take(5))
                    sb.AppendLine($"  - {h.NgayThucHien:dd/MM/yyyy} | {h.TongTien:N0} ₫ | {h.TrangThai}");
                sb.AppendLine($"Tổng tiền {vt.Count} hồ sơ VT gần đây của bạn: {vt.Sum(x => x.TongTien):N0} ₫");
            }

            sb.AppendLine("Cách vận hành NVKT: nhận việc → vào chi tiết → làm từng bước quy trình → chọn vật tư/số lượng → hoàn thành bước → đủ bước thì gửi xưởng xác nhận.");
            sb.AppendLine("CẤM nêu số liệu toàn công ty hoặc của NV khác.");
        }

        private async Task AppendToTruongAsync(StringBuilder sb, int? maNv, CancellationToken ct)
        {
            sb.AppendLine("=== SỐ LIỆU TRÊN APP (Tổ trưởng cơ điện) — khớp danh sách hồ sơ ===");

            // Toàn bộ BT/SC theo trạng thái (đúng số app hiển thị trên list)
            await AppendDemTrangThaiAsync(_db, sb, "HỒ SƠ BẢO TRÌ", true, ct);
            await AppendDemTrangThaiAsync(_db, sb, "HỒ SƠ SỬA CHỮA", false, ct);

            if (maNv != null)
            {
                var btTao = await _db.HoSoBaoTris.AsNoTracking()
                    .Where(h => h.MaNhanVienTao == maNv)
                    .GroupBy(h => h.TrangThai ?? "(trống)")
                    .Select(g => new { Tt = g.Key, So = g.Count() })
                    .ToListAsync(ct);
                sb.AppendLine($"Hồ sơ BT do BẠN tạo (MaNV={maNv}): tổng {btTao.Sum(x => x.So)}");
                foreach (var g in btTao)
                    sb.AppendLine($"  • {g.Tt}: {g.So}");
            }

            var soTb = await _db.ThietBis.AsNoTracking().CountAsync(ct);
            sb.AppendLine($"Số thiết bị trong hệ thống: {soTb}");

            var sapToi = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.NgayDuKienBaoTri >= DateOnly.FromDateTime(DateTime.Now))
                .OrderBy(c => c.NgayDuKienBaoTri)
                .Take(10)
                .Select(c => new
                {
                    c.NgayDuKienBaoTri,
                    Ten = c.MaThietBiNavigation != null
                        ? c.MaThietBiNavigation.TenThietBi
                        : ("TB#" + c.MaThietBi)
                })
                .ToListAsync(ct);
            sb.AppendLine("Lịch bảo trì dự kiến sắp tới:");
            if (sapToi.Count == 0) sb.AppendLine("  • Chưa có ngày sắp tới.");
            else foreach (var x in sapToi)
                sb.AppendLine($"  • {x.NgayDuKienBaoTri:dd/MM/yyyy}: {x.Ten}");

            sb.AppendLine("Cách vận hành Tổ trưởng:");
            sb.AppendLine("  1) Lập/xem kế hoạch BT theo năm-tháng.");
            sb.AppendLine("  2) Tạo hồ sơ BT → gửi xưởng chốt ngày.");
            sb.AppendLine("  3) Xưởng gửi GĐ duyệt; GĐ duyệt xong mới phân công NVKT.");
            sb.AppendLine("  4) Phân công nhiều NV; NV bận thì cập nhật lại trên hệ thống.");
            sb.AppendLine("Ràng buộc: 1 thiết bị chỉ BT 1 lần/tháng; đang BT chưa xong thì chưa tạo SC; không 2 SC cùng TB trong cùng 1 ngày.");
        }

        private async Task AppendXuongAsync(StringBuilder sb, CancellationToken ct)
        {
            sb.AppendLine("=== SỐ LIỆU TRÊN APP (Xưởng) ===");
            await AppendDemTrangThaiAsync(_db, sb, "HỒ SƠ BẢO TRÌ", true, ct);
            await AppendDemTrangThaiAsync(_db, sb, "HỒ SƠ SỬA CHỮA", false, ct);

            var vtCho = await _db.HoSoSuDungVatTus.AsNoTracking()
                .Where(h => h.TrangThai == "Chờ gửi" || h.TrangThai == "Chờ xác nhận"
                            || h.TrangThai == "Chờ duyệt" || h.TrangThai == "Đã gửi GĐ")
                .OrderByDescending(h => h.NgayThucHien)
                .Take(8)
                .Select(h => new { h.TenThietBi, h.TongTien, h.TrangThai, h.NgayThucHien, h.LoaiCongViec })
                .ToListAsync(ct);
            sb.AppendLine($"Hồ sơ vật tư cần xưởng quan tâm: {vtCho.Count} dòng gần nhất");
            foreach (var h in vtCho)
                sb.AppendLine($"  • {h.NgayThucHien:dd/MM/yyyy} | {h.LoaiCongViec} | {h.TenThietBi} | {h.TongTien:N0} ₫ | {h.TrangThai}");

            sb.AppendLine("Cách vận hành Xưởng:");
            sb.AppendLine("  1) Nhận HS BT từ tổ trưởng → chốt ngày BT trong tháng → gửi GĐ duyệt.");
            sb.AppendLine("  2) GĐ từ chối → xưởng chỉnh sửa HS rồi gửi lại.");
            sb.AppendLine("  3) NVKT gửi quy trình/vật tư → xưởng xác nhận hoặc từ chối.");
            sb.AppendLine("  4) Hồ sơ SC: tạo/theo dõi theo quy trình xưởng khi thiết bị hư đột xuất.");
        }

        private async Task AppendGiamDocAsync(StringBuilder sb, DateTime now, CancellationToken ct)
        {
            sb.AppendLine("=== SỐ LIỆU TRÊN APP (Giám đốc) ===");
            await AppendDemTrangThaiAsync(_db, sb, "HỒ SƠ BẢO TRÌ", true, ct);
            await AppendDemTrangThaiAsync(_db, sb, "HỒ SƠ SỬA CHỮA", false, ct);

            var nam = now.Year;
            var thang = now.Month;
            var vt = await _db.HoSoSuDungVatTus.AsNoTracking()
                .Where(h => h.TrangThai == "Xác nhận" || h.TrangThai == "Đã duyệt")
                .Select(h => new { h.TongTien, h.NgayThucHien })
                .ToListAsync(ct);
            var tongThang = vt.Where(x => x.NgayThucHien.Year == nam && x.NgayThucHien.Month == thang).Sum(x => x.TongTien);
            var tongNam = vt.Where(x => x.NgayThucHien.Year == nam).Sum(x => x.TongTien);
            var tongAll = vt.Sum(x => x.TongTien);
            sb.AppendLine($"Tổng tiền VT đã xác nhận tháng {thang}/{nam}: {tongThang:N0} ₫");
            sb.AppendLine($"Tổng tiền VT đã xác nhận năm {nam}: {tongNam:N0} ₫");
            sb.AppendLine($"Tổng tiền VT đã xác nhận (mọi thời điểm): {tongAll:N0} ₫");

            sb.AppendLine("Cách vận hành Giám đốc:");
            sb.AppendLine("  1) Duyệt / từ chối hồ sơ bảo trì do xưởng gửi.");
            sb.AppendLine("  2) Xem thống kê số lượng BT/SC và chi phí vật tư theo tháng/năm.");
        }

        private async Task AppendAdminAsync(StringBuilder sb, CancellationToken ct)
        {
            var soUser = await _db.QuanLyNguoiDungs.AsNoTracking().CountAsync(ct);
            var soVt = await _db.VaiTros.AsNoTracking().CountAsync(ct);
            sb.AppendLine($"Số tài khoản: {soUser}");
            sb.AppendLine($"Số vai trò: {soVt}");
            sb.AppendLine("Hỗ trợ: tạo/sửa user, phân vai trò, xem nhật ký hệ thống — không trả số liệu BT/SC chi tiết của user khác.");
        }

        internal static string BuildSystemPrompt(string vaiTro, string hoTen, string soLieu)
        {
            var ten = string.IsNullOrWhiteSpace(hoTen) ? "bạn" : hoTen.Trim();
            var vt = string.IsNullOrWhiteSpace(vaiTro) ? "Người dùng" : vaiTro.Trim();

            var sb = new StringBuilder();
            sb.AppendLine("Bạn là OPC Bot — đồng nghiệp hỗ trợ tận tâm trong app quản lý bảo trì–sửa chữa thiết bị cơ điện (công ty CP dược phẩm OPC).");
            sb.AppendLine($"User: {ten}. Vai trò: «{vt}».");
            sb.AppendLine("Xưng «mình», nói chuyện ấm áp, rõ ràng, tiếng Việt.");
            sb.AppendLine();
            sb.AppendLine("=== QUY TẮC TRẢ LỜI (BẮT BUỘC) ===");
            sb.AppendLine("1) Khi user hỏi SỐ LƯỢNG / TRẠNG THÁI / NGÀY / TỔNG TIỀN / THỐNG KÊ: trả lời NGAY bằng con số cụ thể lấy từ khối DỮ LIỆU bên dưới.");
            sb.AppendLine("   Ví dụ đúng: «Hiện có 12 hồ sơ bảo trì ở trạng thái Đã hoàn thành.»");
            sb.AppendLine("   Ví dụ SAI (CẤM): «Bạn vào app xem tab Đã hoàn thành nhé», «Mình không có số liệu, bạn tự kiểm tra».");
            sb.AppendLine("2) CẤM đẩy user tự tìm trong app nếu số liệu đã có trong khối DỮ LIỆU.");
            sb.AppendLine("3) Khi hỏi CÁCH LÀM / QUY TRÌNH / CHỨC NĂNG: giải thích từng bước cụ thể theo «Cách vận hành» trong dữ liệu.");
            sb.AppendLine("4) CHỈ dùng dữ liệu trong phạm vi «" + vt + "». Hỏi ngoài phạm vi → từ chối nhẹ, nói rõ chỉ hỗ trợ việc của vai trò này.");
            sb.AppendLine("5) Không bịa số. Không có trong dữ liệu thì nói «Trên hệ thống hiện chưa có / bằng 0».");
            sb.AppendLine("6) CẤM lộ code, mật khẩu, connection string, JWT, API key, cấu hình bảo mật.");
            sb.AppendLine();
            sb.AppendLine("=== DỮ LIỆU THỰC TẾ (cập nhật realtime) ===");
            sb.AppendLine(soLieu);
            return sb.ToString();
        }
    }
}
