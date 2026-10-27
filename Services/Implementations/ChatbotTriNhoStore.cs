using System.Text.Encodings.Web;
using System.Text.Json;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    /// <summary>
    /// Một điều chatbot ghi nhớ sau khi người dùng chỉnh lại cách hiểu.
    /// KHÔNG lưu nguyên câu trả lời — chỉ lưu chủ đề, số đã xác nhận, hồ sơ cần loại trừ
    /// và ghi chú cách hiểu. Số liệu luôn được tính lại từ DB trước khi trả lời.
    /// </summary>
    public sealed class TriNhoChat
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string VaiTro { get; set; } = "";
        /// <summary>Khóa chủ đề ổn định, ví dụ: cho_phan_cong_bt, thiet_bi_tong.</summary>
        public string ChuDe { get; set; } = "";
        public string CauHoiMau { get; set; } = "";
        public List<string> Tokens { get; set; } = new();
        /// <summary>
        /// loai_tru   = bỏ các hồ sơ này khỏi danh sách của chủ đề.
        /// so_lieu    = số người dùng đã xác nhận (chỉ dùng khi fingerprint còn khớp).
        /// hieu_ngu_canh = ghi chú cách hiểu để tham khảo.
        /// </summary>
        public string Loai { get; set; } = "hieu_ngu_canh";
        public int? SoLuong { get; set; }
        public List<int> MaHoSoLoaiTru { get; set; } = new();
        public string Fingerprint { get; set; } = "";
        public string GhiChu { get; set; } = "";
        public DateTime Luc { get; set; } = DateTime.Now;
        public int SoLanDung { get; set; }
    }

    /// <summary>
    /// Bộ nhớ bền vững của OPC Bot (JSON, thread-safe).
    /// Mục tiêu: người dùng sửa một lần thì lần sau bot nhớ cách hiểu,
    /// nhưng tuyệt đối không phát lại câu trả lời cũ cho câu hỏi khác.
    /// </summary>
    public static class ChatbotTriNhoStore
    {
        private const int MaxTriNho = 500;
        private static readonly object _lock = new();
        private static List<TriNhoChat>? _ds;
        private static string _path = Path.Combine(
            AppContext.BaseDirectory, "App_Data", "chatbot_trinhi.json");

        private static readonly JsonSerializerOptions JsonOpt = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>Đổi file lưu (cấu hình Chatbot:TriNhoPath).</summary>
        public static void DatDuongDan(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            lock (_lock)
            {
                var moi = path.Trim();
                if (string.Equals(_path, moi, StringComparison.OrdinalIgnoreCase)) return;
                _path = moi;
                _ds = null;
            }
        }

        private static List<TriNhoChat> Nap()
        {
            if (_ds != null) return _ds;
            try
            {
                if (File.Exists(_path))
                    _ds = JsonSerializer.Deserialize<List<TriNhoChat>>(
                              File.ReadAllText(_path), JsonOpt) ?? new();
            }
            catch
            {
                // File hỏng: bắt đầu lại, không làm sập chatbot.
            }
            return _ds ??= new List<TriNhoChat>();
        }

        private static void Luu()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_ds ?? new(), JsonOpt));
                File.Move(tmp, _path, true);
            }
            catch
            {
                // Không ghi được file vẫn giữ bộ nhớ trong RAM cho phiên hiện tại.
            }
        }

        public static List<TriNhoChat> LayTheoVaiTro(string vaiTro)
        {
            lock (_lock)
            {
                var vt = (vaiTro ?? "").Trim();
                return Nap()
                    .Where(x => string.Equals(x.VaiTro, vt, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Luc)
                    .ToList();
            }
        }

        public static List<TriNhoChat> LayTheoChuDe(string vaiTro, string chuDe)
        {
            lock (_lock)
            {
                var vt = (vaiTro ?? "").Trim();
                var cd = (chuDe ?? "").Trim();
                return Nap()
                    .Where(x => string.Equals(x.VaiTro, vt, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.ChuDe, cd, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Luc)
                    .ToList();
            }
        }

        public static bool CoTriNho(string vaiTro)
        {
            lock (_lock)
            {
                var vt = (vaiTro ?? "").Trim();
                return Nap().Any(x =>
                    string.Equals(x.VaiTro, vt, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Ghi nhận một chỉnh sửa. Cùng vai trò + chủ đề + loại thì bản mới thay bản cũ
        /// để tránh mâu thuẫn và tránh phình file.
        /// </summary>
        public static void GhiNhan(TriNhoChat moi)
        {
            if (moi == null || string.IsNullOrWhiteSpace(moi.VaiTro)
                || string.IsNullOrWhiteSpace(moi.ChuDe))
                return;

            lock (_lock)
            {
                var ds = Nap();
                ds.RemoveAll(x =>
                    string.Equals(x.VaiTro, moi.VaiTro, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.ChuDe, moi.ChuDe, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Loai, moi.Loai, StringComparison.OrdinalIgnoreCase));
                ds.Add(moi);
                if (ds.Count > MaxTriNho)
                    ds.RemoveRange(0, ds.Count - MaxTriNho);
                Luu();
            }
        }

        public static void XoaTheoChuDe(string vaiTro, string chuDe)
        {
            lock (_lock)
            {
                var ds = Nap();
                var vt = (vaiTro ?? "").Trim();
                var cd = (chuDe ?? "").Trim();
                var removed = ds.RemoveAll(x =>
                    string.Equals(x.VaiTro, vt, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.ChuDe, cd, StringComparison.OrdinalIgnoreCase));
                if (removed > 0) Luu();
            }
        }

        /// <summary>Xóa riêng một loại ghi nhớ (ví dụ so_lieu) nhưng giữ loai_tru của người dùng.</summary>
        public static void XoaTheoLoai(string vaiTro, string chuDe, string loai)
        {
            lock (_lock)
            {
                var ds = Nap();
                var vt = (vaiTro ?? "").Trim();
                var cd = (chuDe ?? "").Trim();
                var l = (loai ?? "").Trim();
                var removed = ds.RemoveAll(x =>
                    string.Equals(x.VaiTro, vt, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.ChuDe, cd, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Loai, l, StringComparison.OrdinalIgnoreCase));
                if (removed > 0) Luu();
            }
        }

        /// <summary>Các hồ sơ người dùng đã loại khỏi một chủ đề.</summary>
        public static HashSet<int> LayLoaiTru(string vaiTro, string chuDe)
        {
            lock (_lock)
            {
                var vt = (vaiTro ?? "").Trim();
                var cd = (chuDe ?? "").Trim();
                return Nap()
                    .Where(x => string.Equals(x.VaiTro, vt, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.ChuDe, cd, StringComparison.OrdinalIgnoreCase)
                                && x.Loai == "loai_tru")
                    .SelectMany(x => x.MaHoSoLoaiTru)
                    .ToHashSet();
            }
        }

        /// <summary>
        /// Số người dùng đã xác nhận — chỉ trả về khi dữ liệu chưa đổi (fingerprint khớp).
        /// Nhờ vậy không bao giờ dùng lại con số cũ sau khi DB thay đổi.
        /// </summary>
        public static int? LaySoDaXacNhan(string vaiTro, string chuDe, string fingerprint)
        {
            lock (_lock)
            {
                var vt = (vaiTro ?? "").Trim();
                var cd = (chuDe ?? "").Trim();
                var fp = (fingerprint ?? "").Trim();
                var hit = Nap()
                    .Where(x => string.Equals(x.VaiTro, vt, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.ChuDe, cd, StringComparison.OrdinalIgnoreCase)
                                && x.Loai == "so_lieu"
                                && x.SoLuong != null
                                && string.Equals(x.Fingerprint, fp, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Luc)
                    .FirstOrDefault();
                return hit?.SoLuong;
            }
        }

        /// <summary>Ghi chú cách hiểu đã học (dùng làm ngữ cảnh cho Groq, không phải đáp án).</summary>
        public static List<string> LayGhiChu(string vaiTro, string chuDe)
        {
            lock (_lock)
            {
                var vt = (vaiTro ?? "").Trim();
                var cd = (chuDe ?? "").Trim();
                return Nap()
                    .Where(x => string.Equals(x.VaiTro, vt, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.ChuDe, cd, StringComparison.OrdinalIgnoreCase)
                                && x.Loai == "hieu_ngu_canh"
                                && !string.IsNullOrWhiteSpace(x.GhiChu))
                    .OrderByDescending(x => x.Luc)
                    .Select(x => x.GhiChu.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList();
            }
        }

        /// <summary>Tìm ghi chú gần đúng theo token khi không xác định được chủ đề chính xác.</summary>
        public static List<string> LayGhiChuGanDung(string vaiTro, IReadOnlyCollection<string> tokens)
        {
            if (tokens == null || tokens.Count == 0) return new List<string>();
            lock (_lock)
            {
                var vt = (vaiTro ?? "").Trim();
                var ungVien = Nap()
                    .Where(x => string.Equals(x.VaiTro, vt, StringComparison.OrdinalIgnoreCase)
                                && x.Loai == "hieu_ngu_canh"
                                && !string.IsNullOrWhiteSpace(x.GhiChu))
                    .Select(x => new
                    {
                        x.GhiChu,
                        Diem = x.Tokens.Count(t =>
                            tokens.Contains(t, StringComparer.OrdinalIgnoreCase))
                    })
                    .Where(x => x.Diem >= 2)
                    .OrderByDescending(x => x.Diem)
                    .Take(3)
                    .Select(x => x.GhiChu.Trim())
                    .ToList();
                return ungVien;
            }
        }

        public static void TangSuDung(string vaiTro, string chuDe)
        {
            lock (_lock)
            {
                var vt = (vaiTro ?? "").Trim();
                var cd = (chuDe ?? "").Trim();
                var ds = Nap().Where(x =>
                    string.Equals(x.VaiTro, vt, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.ChuDe, cd, StringComparison.OrdinalIgnoreCase)).ToList();
                if (ds.Count == 0) return;
                foreach (var x in ds) x.SoLanDung++;
                Luu();
            }
        }
    }
}
