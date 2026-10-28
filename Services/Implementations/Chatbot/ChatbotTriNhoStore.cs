using System.Text.Encodings.Web;
using System.Text.Json;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public sealed class TriNhoChat
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string VaiTro { get; set; } = "";
        public int? MaNguoiDung { get; set; }
        public string ChuDe { get; set; } = "";
        public string CauHoiMau { get; set; } = "";
        public List<string> Tokens { get; set; } = new();
        public string Loai { get; set; } = "hieu_ngu_canh";
        public string? BaiHoc { get; set; }
        public bool DaDoiChieuDb { get; set; }
        public int? SoLuong { get; set; }
        public List<int> MaHoSoLoaiTru { get; set; } = new();
        public string Fingerprint { get; set; } = "";
        public string GhiChu { get; set; } = "";
        public DateTime Luc { get; set; } = DateTime.UtcNow;
        public int SoLanDung { get; set; }
    }

    /// <summary>Bài học chỉ là mã do code định nghĩa; số xác nhận là nhật ký, không phải nguồn đếm.</summary>
    public static class ChatbotTriNhoStore
    {
        private const int MaxTriNho = 500;
        private static readonly object Gate = new();
        private static List<TriNhoChat>? _ds;
        private static string _path = Path.Combine(AppContext.BaseDirectory, "App_Data", "chatbot_trinhi.json");
        private static readonly JsonSerializerOptions JsonOpt = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static readonly IReadOnlyDictionary<string, string> BaiHocChoPhep =
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>
            {
                ["tra_loi_ngan"] = "Trả lời ngắn, nêu kết quả trước; không bỏ ranh giới quyền hoặc mốc ngày.",
                ["chi_dem"] = "Khi chỉ hỏi số lượng, không tự liệt kê từng hồ sơ; yêu cầu liệt kê hiện tại vẫn ưu tiên.",
                ["tach_nhieu_y"] = "Tách các ý trong câu hỏi; không lấy đáp án của ý này để trả lời ý khác.",
                ["ho_so_khong_lich_su"] = "Phân biệt hồ sơ chờ phân công với lịch sử phân công; chỉ nêu PC khi người dùng hỏi lịch sử.",
                ["phan_biet_bt_sc"] = "Phân biệt bảo trì và sửa chữa; không lấy số của loại này trả lời loại kia.",
                ["ngay_khong_thang"] = "Giữ ngày cụ thể người dùng hỏi, nêu mốc ngày khớp; không tự đổi thành cả tháng."
            });

        public static void DatDuongDan(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            lock (Gate)
            {
                var full = Path.GetFullPath(path.Trim());
                if (string.Equals(_path, full, StringComparison.OrdinalIgnoreCase)) return;
                _path = full;
                _ds = null;
            }
        }

        private static List<TriNhoChat> Nap()
        {
            if (_ds != null) return _ds;
            // Không nuốt lỗi đọc rồi ghi đè file cũ: caller sẽ báo chưa lưu được.
            _ds = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<TriNhoChat>>(File.ReadAllText(_path), JsonOpt) ?? new()
                : new();
            return _ds;
        }

        private static TriNhoChat SaoChep(TriNhoChat x) =>
            JsonSerializer.Deserialize<TriNhoChat>(JsonSerializer.Serialize(x, JsonOpt), JsonOpt)!;

        public static bool GhiNhan(TriNhoChat moi)
        {
            if (moi.MaNguoiDung is not > 0 || string.IsNullOrWhiteSpace(moi.VaiTro)
                || string.IsNullOrWhiteSpace(moi.ChuDe)) return false;
            var record = SaoChep(moi);
            if (record.Loai == "hieu_ngu_canh")
            {
                if (record.BaiHoc == null || !BaiHocChoPhep.TryGetValue(record.BaiHoc, out var instruction))
                    return false;
                // Không lưu nguyên lời sửa, số liệu, mã hồ sơ, prompt hoặc yêu cầu mở quyền.
                record.GhiChu = instruction;
                record.CauHoiMau = "";
                record.Tokens.Clear();
                record.SoLuong = null;
                record.MaHoSoLoaiTru.Clear();
                record.Fingerprint = "";
            }
            else if (record.Loai == "so_lieu")
            {
                if (!record.DaDoiChieuDb || record.SoLuong is null or < 0
                    || !record.Fingerprint.StartsWith("v=4|", StringComparison.Ordinal)) return false;
                record.GhiChu = "Số đã đối chiếu DB; chỉ dùng kiểm toán, không dùng thay kết quả truy vấn.";
                record.CauHoiMau = "";
                record.Tokens.Clear();
                record.MaHoSoLoaiTru.Clear();
            }
            else return false;

            lock (Gate)
            {
                try
                {
                    var next = Nap().Select(SaoChep).ToList();
                    next.RemoveAll(x => x.MaNguoiDung == record.MaNguoiDung && x.VaiTro == record.VaiTro
                        && x.Loai == record.Loai && (record.Loai == "hieu_ngu_canh"
                            ? x.BaiHoc == record.BaiHoc : x.Fingerprint == record.Fingerprint && x.ChuDe == record.ChuDe));
                    next.Add(record);
                    if (next.Count > MaxTriNho) next.RemoveRange(0, next.Count - MaxTriNho);
                    var dir = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var tmp = _path + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(next, JsonOpt));
                    File.Move(tmp, _path, true);
                    _ds = next;
                    return true;
                }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
                catch (JsonException) { return false; }
            }
        }

        public static List<TriNhoChat> LayBaiHoc(string vaiTro, int maNguoiDung)
        {
            lock (Gate)
            {
                try
                {
                    return Nap().Where(x => x.MaNguoiDung == maNguoiDung && x.VaiTro == vaiTro
                            && x.Loai == "hieu_ngu_canh" && x.BaiHoc != null && BaiHocChoPhep.ContainsKey(x.BaiHoc))
                        .OrderByDescending(x => x.Luc).Select(SaoChep).ToList();
                }
                catch (IOException) { return new(); }
                catch (UnauthorizedAccessException) { return new(); }
                catch (JsonException) { return new(); }
            }
        }

        // API đọc legacy giữ tương thích, nhưng không cho dữ liệu chỉ gắn vai trò rò sang người khác.
        public static List<TriNhoChat> LayTheoVaiTro(string vaiTro) => new();
        public static List<TriNhoChat> LayTheoChuDe(string vaiTro, string chuDe) => new();
        public static bool CoTriNho(string vaiTro) => false;
        public static HashSet<int> LayLoaiTru(string vaiTro, string chuDe) => new();
        public static int? LaySoDaXacNhan(string vaiTro, string chuDe, string fingerprint) => null;
        public static List<string> LayGhiChu(string vaiTro, string chuDe) => new();
        public static List<string> LayGhiChuGanDung(string vaiTro, IReadOnlyCollection<string> tokens) => new();
        public static void TangSuDung(string vaiTro, string chuDe) { }
        public static void XoaTheoChuDe(string vaiTro, string chuDe) { }
        public static void XoaTheoLoai(string vaiTro, string chuDe, string loai) { }
    }
}
