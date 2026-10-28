using System.Text.Json;
using System.Text.Encodings.Web;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    /// <summary>Một thay đổi trên cách hiểu câu hỏi: "trường X trước đây hiểu là A, đúng phải là B".</summary>
    public class ThayDoiTruong
    {
        public string Truong { get; set; } = "";
        public string Truoc { get; set; } = "";
        public string Sau { get; set; } = "";
    }

    /// <summary>
    /// Bài học khi người dùng báo sai. CHỈ lưu cách hiểu (loại hồ sơ, trạng thái, cách tính…),
    /// KHÔNG lưu con số/câu trả lời — số liệu luôn được tính lại từ DB mỗi lần hỏi.
    /// </summary>
    public class BaiHoc
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string VaiTro { get; set; } = "";
        public string CauGoc { get; set; } = "";
        public string? LoiSua { get; set; }
        public int? SoMongDoi { get; set; }
        public List<string> Tokens { get; set; } = new();
        public List<ThayDoiTruong> ThayDoi { get; set; } = new();
        /// <summary>true = đã tìm ra cách hiểu đúng và được áp dụng; false = chỉ ghi nhận để dev xem lại.</summary>
        public bool DaGiaiQuyet { get; set; }
        public int SoLanDung { get; set; }
        public DateTime Luc { get; set; } = DateTime.Now;
    }

    public static class ChatbotBaiHocStore
    {
        private const int MaxBaiHoc = 300;
        private static readonly object _lock = new();
        private static List<BaiHoc>? _ds;
        private static string _path = Path.Combine(AppContext.BaseDirectory, "App_Data", "chatbot_baihoc.json");

        private static readonly JsonSerializerOptions JsonOpt = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>Đổi đường dẫn file (cấu hình Chatbot:BaiHocPath). Bỏ trống = App_Data/chatbot_baihoc.json.</summary>
        public static void DatDuongDan(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            lock (_lock)
            {
                if (_path == path) return;
                _path = path.Trim();
                _ds = null;
            }
        }

        private static List<BaiHoc> Nap()
        {
            if (_ds != null) return _ds;
            try
            {
                if (File.Exists(_path))
                    _ds = JsonSerializer.Deserialize<List<BaiHoc>>(File.ReadAllText(_path), JsonOpt) ?? new();
            }
            catch { /* file hỏng → bắt đầu lại từ rỗng */ }
            return _ds ??= new List<BaiHoc>();
        }

        private static void Luu()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_ds, JsonOpt));
                File.Move(tmp, _path, true);
            }
            catch { /* không ghi được thì vẫn giữ trong RAM */ }
        }

        public static List<BaiHoc> TatCa()
        {
            lock (_lock) return Nap().ToList();
        }

        public static void Them(BaiHoc b)
        {
            lock (_lock)
            {
                var ds = Nap();
                if (b.DaGiaiQuyet)
                {
                    // Cùng vai trò + cùng bộ từ khóa đã giải quyết → thay bằng bài học mới nhất
                    var tk = new HashSet<string>(b.Tokens);
                    ds.RemoveAll(x => x.DaGiaiQuyet && x.VaiTro == b.VaiTro && tk.SetEquals(x.Tokens));
                }
                ds.Add(b);
                if (ds.Count > MaxBaiHoc)
                    ds.RemoveRange(0, ds.Count - MaxBaiHoc);
                Luu();
            }
        }

        public static void TangSuDung(string id)
        {
            lock (_lock)
            {
                var b = Nap().FirstOrDefault(x => x.Id == id);
                if (b == null) return;
                b.SoLanDung++;
                Luu();
            }
        }
    }
}