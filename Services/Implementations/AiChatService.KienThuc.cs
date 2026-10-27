using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class AiChatService
    {
        // ===================== KIẾN THỨC + PHẠM VI =====================

        private static string XayDungKienThucHeThong(string vt)
        {
            var chung =
                "APP: 2 chức năng chính — Bảo trì định kỳ và Sửa chữa sự cố thiết bị cơ điện.\n"
                + "VAI TRÒ: Tổ trưởng cơ điện | Xưởng | Giám đốc (Phó giám đốc) | Nhân viên kỹ thuật.\n"
                + "LUỒNG BẢO TRÌ: Tổ trưởng tạo hồ sơ → gửi Xưởng chốt ngày bảo trì trong tháng → Xưởng gửi Giám đốc duyệt "
                + "→ Giám đốc từ chối thì Xưởng chỉnh sửa; duyệt thì Tổ trưởng mở khóa phân công "
                + "→ Tổ trưởng chọn một hoặc nhiều NVKT → NVKT nhận việc, chọn các bước đã làm, đủ thì bấm hoàn thành gửi Xưởng "
                + "→ Xưởng xác nhận hoàn thành.\n"
                + "LUỒNG SỬA CHỮA: Xưởng tạo hồ sơ → Giám đốc duyệt → Tổ trưởng phân công NVKT → NVKT thực hiện các bước "
                + "→ gửi Xưởng xác nhận.\n"
                + "TRẠNG THÁI BT thường gặp: Chờ xưởng, Chờ duyệt, Từ chối, Đã duyệt, Đang thực hiện, Chờ xác nhận, Đã hoàn thành (ẩn: Nháp, Đã hủy).\n"
                + "MÀN HÌNH: danh sách thiết bị; danh sách HS bảo trì; chi tiết HS bảo trì (thiết bị, nội dung, thời gian, trạng thái, người duyệt, phân công, bước, vật tư); "
                + "danh sách HS sửa chữa; chi tiết HS sửa chữa; phân công; thống kê.\n"
                + "CÁCH ĐẾM THỐNG KÊ: HS bảo trì Đã hoàn thành tính theo THÁNG NGÀY KẾ HOẠCH bảo trì; HS sửa chữa Đã hoàn thành tính theo THÁNG HOÀN THÀNH thực tế.\n";

            var theoRole = vt switch
            {
                "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" =>
                    "TỔ TRƯỞNG CƠ ĐIỆN: tạo HS bảo trì, gửi Xưởng; sau khi Giám đốc duyệt mới phân công NVKT; "
                    + "phân công bao nhiêu nhân viên cũng được và cập nhật lại khi nhân viên bận; theo dõi bước, vật tư, thiết bị, lịch bảo trì. "
                    + "Không chốt ngày thay Xưởng, không duyệt thay Giám đốc.\n",
                "Xưởng" or "Tổ trưởng sản xuất" =>
                    "XƯỞNG: nhận HS bảo trì từ Tổ trưởng, chốt ngày bảo trì trong tháng, gửi Giám đốc; "
                    + "Giám đốc từ chối thì chỉnh sửa và gửi lại; tạo HS sửa chữa; xác nhận kết quả khi NVKT hoàn thành. "
                    + "Không phân công NVKT, không phê duyệt cuối thay Giám đốc.\n",
                "Giám đốc" or "Phó giám đốc" =>
                    "GIÁM ĐỐC: duyệt hoặc từ chối HS bảo trì/sửa chữa/vật tư; xem thống kê và lịch sử phê duyệt. "
                    + "Không phân công NVKT, không chốt ngày thay Xưởng.\n",
                "Nhân viên kỹ thuật" =>
                    "NHÂN VIÊN KỸ THUẬT: chỉ thấy việc được phân công cho mình; chọn các bước đã thực hiện; "
                    + "khi đủ bước thì bấm hoàn thành để gửi Xưởng; ghi nhận vật tư sử dụng. "
                    + "Không xem toàn công ty, không duyệt, không chốt ngày, không phân công người khác.\n",
                _ => "Chỉ trả lời thông tin gắn tài khoản đang đăng nhập.\n"
            };

            return chung + theoRole;
        }

        private static string MoTaPhamVi(string vt) => vt switch
        {
            "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" =>
                "Được hỏi: HS bảo trì, HS chờ phân công, phân công NVKT, nhân viên đang làm, bước quy trình, vật tư, thiết bị, lịch bảo trì, thống kê liên quan tổ.\n"
                + "Không làm hộ: duyệt của Giám đốc, chốt ngày của Xưởng.",
            "Xưởng" or "Tổ trưởng sản xuất" =>
                "Được hỏi: HS chờ xưởng, chốt ngày bảo trì, HS bị từ chối, HS sửa chữa, xác nhận hoàn thành.\n"
                + "Không làm hộ: phân công NVKT, phê duyệt cuối của Giám đốc.",
            "Giám đốc" or "Phó giám đốc" =>
                "Được hỏi: HS chờ duyệt, lịch sử duyệt/từ chối, thống kê toàn công ty, trạng thái BT/SC/vật tư.\n"
                + "Không làm hộ: phân công NVKT, chốt ngày của Xưởng.",
            "Nhân viên kỹ thuật" =>
                "Chỉ dữ liệu của tài khoản này: phân công của tôi, hồ sơ của tôi, bước còn lại, vật tư của tôi.\n"
                + "Không xem toàn công ty, không duyệt, không chốt ngày, không phân công người khác.",
            _ => "Chỉ thông tin gắn tài khoản đang đăng nhập."
        };

        private static string? TuChoiVuotVaiTro(string vt, string q)
        {
            if (vt == "Nhân viên kỹ thuật")
            {
                if (Co(q, "toan bo", "tat ca nhan vien", "toan cong ty"))
                    return "Dạ, tài khoản NVKT chỉ xem công việc được phân công cho bạn.";
                if (Co(q, "duyet") && Co(q, "ho so", "giam doc"))
                    return "Dạ, phê duyệt hồ sơ thuộc Giám đốc — NVKT không thực hiện được.";
                if (Co(q, "chot ngay"))
                    return "Dạ, chốt ngày bảo trì thuộc Xưởng.";
                if (Co(q, "phan cong") && Co(q, "nhan vien khac", "to truong"))
                    return "Dạ, phân công nhân viên thuộc Tổ trưởng cơ điện.";
            }
            if (vt is "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật")
            {
                if (Co(q, "thay giam doc") || (Co(q, "toi duyet", "duyet ho") && Co(q, "giam doc")))
                    return "Dạ, Tổ trưởng không duyệt hộ Giám đốc. Sau khi Giám đốc duyệt, bạn mới phân công NVKT.";
                if (Co(q, "chot ngay") && Co(q, "thay", "ho toi", "giup toi"))
                    return "Dạ, chốt ngày bảo trì trong tháng thuộc Xưởng.";
            }
            if (vt is "Xưởng" or "Tổ trưởng sản xuất")
            {
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

        /// <summary>Câu hỏi về quy trình/chức năng — trả lời bằng kiến thức, không cần số liệu.</summary>
        private static string? TraLoiKienThuc(string vt, string q)
        {
            if (!Co(q, "quy trinh", "cac buoc", "lam the nao", "toi can lam gi", "minh can lam gi",
                    "vai tro cua toi", "chuc nang", "su dung app", "huong dan"))
                return null;

            if (Co(q, "quy trinh", "cac buoc") && Co(q, "bao tri", "sua chua"))
            {
                return "Dạ, quy trình bảo trì đang chạy trên app như sau:\n"
                       + "1) Tổ trưởng tạo hồ sơ bảo trì và gửi Xưởng.\n"
                       + "2) Xưởng chốt ngày bảo trì trong tháng rồi gửi Giám đốc.\n"
                       + "3) Giám đốc duyệt hoặc từ chối; nếu từ chối, Xưởng chỉnh sửa và gửi lại.\n"
                       + "4) Sau khi duyệt, Tổ trưởng phân công một hoặc nhiều NVKT.\n"
                       + "5) NVKT nhận việc, chọn các bước đã làm; đủ bước thì bấm hoàn thành gửi Xưởng.\n"
                       + "6) Xưởng xác nhận hoàn thành hồ sơ.\n"
                       + "Sửa chữa tương tự, nhưng Xưởng tạo hồ sơ và Giám đốc duyệt trước khi Tổ trưởng phân công.";
            }

            return vt switch
            {
                "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" =>
                    "Dạ, việc của Tổ trưởng cơ điện gồm: tạo hồ sơ bảo trì và gửi Xưởng; theo dõi hồ sơ đã được Giám đốc duyệt; "
                    + "phân công một hoặc nhiều NVKT và cập nhật lại khi nhân viên bận; theo dõi bước quy trình, vật tư và lịch bảo trì. "
                    + "Bước chốt ngày do Xưởng phụ trách, bước duyệt do Giám đốc phụ trách.",
                "Xưởng" or "Tổ trưởng sản xuất" =>
                    "Dạ, việc của Xưởng gồm: nhận hồ sơ bảo trì từ Tổ trưởng, chốt ngày bảo trì trong tháng, gửi Giám đốc duyệt; "
                    + "chỉnh sửa và gửi lại khi Giám đốc từ chối; tạo hồ sơ sửa chữa; xác nhận hoàn thành khi NVKT gửi kết quả.",
                "Giám đốc" or "Phó giám đốc" =>
                    "Dạ, việc của Giám đốc gồm: duyệt hoặc từ chối hồ sơ bảo trì/sửa chữa/vật tư; theo dõi lịch sử phê duyệt và thống kê. "
                    + "Bước chốt ngày do Xưởng phụ trách, bước phân công do Tổ trưởng phụ trách.",
                "Nhân viên kỹ thuật" =>
                    "Dạ, việc của NVKT gồm: mở việc được phân công, chọn các bước đã thực hiện, ghi nhận vật tư nếu có; "
                    + "khi thấy đủ bước thì bấm hoàn thành để gửi Xưởng xác nhận.",
                _ => "Dạ, bạn có thể hỏi mình về hồ sơ bảo trì, hồ sơ sửa chữa, thiết bị, phân công và tiến độ công việc."
            };
        }

        // ===================== CHUẨN HÓA + NHẬN DIỆN =====================

        private static string ChuanHoa(string input)
        {
            var s = (input ?? "").Trim().ToLowerInvariant();
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

        /// <summary>Người dùng thực sự báo "câu trả lời sai" (không nhầm với "xem lại giúp tôi").</summary>
        private static bool LaBaoCauTraLoiSai(string q, string cauGoc)
        {
            if (Co(q, "sai roi", "tra loi sai", "cau tra loi sai", "ban sai", "sai mat",
                    "sai so", "sai du lieu", "so lieu sai", "khong dung", "chua dung",
                    "nham roi", "lech roi", "bi lech", "dem sai", "tra loi nham"))
                return true;

            if (Co(q, "kiem tra lai", "doi chieu lai", "xem lai", "coi lai")
                && Co(q, "sai", "nham", "lech", "khong dung", "chua dung"))
                return true;

            var g = (cauGoc ?? "").Trim().ToLowerInvariant();
            return g.Contains("sai rồi") || g.Contains("trả lời sai") || g.Contains("đếm sai")
                   || g.Contains("không đúng") || g.Contains("sai số") || g.Contains("bị lệch");
        }

        private static string? TrichCauHoiTruocTuLichSu(
            IReadOnlyList<(string role, string content)>? lichSu)
        {
            if (lichSu == null || lichSu.Count == 0) return null;
            for (var i = lichSu.Count - 1; i >= 0; i--)
            {
                var r = (lichSu[i].role ?? "").Trim().ToLowerInvariant();
                if (r is not ("user" or "người dùng" or "human")) continue;
                var c = lichSu[i].content?.Trim();
                if (string.IsNullOrWhiteSpace(c)) continue;
                var qc = ChuanHoa(c);
                if (LaBaoCauTraLoiSai(qc, c)) continue;
                if (Co(qc, "cau tra loi vua roi", "hay kiem tra lai so lieu", "bao sai"))
                    continue;
                return c;
            }
            return null;
        }

        private static (int? Ngay, int? Thang, int? Nam) TrichKy(string q)
        {
            int? ngay = null, thang = null, nam = null;
            TrichNgayThangNam(q, ref ngay, ref thang, ref nam);
            var now = DateTime.Now;

            if (Co(q, "thang nay", "trong thang", "thang hien tai", "thang hien tai"))
            {
                thang = now.Month;
                nam = now.Year;
            }
            else if (Co(q, "thang truoc"))
            {
                var t = now.AddMonths(-1);
                thang = t.Month;
                nam = t.Year;
            }
            else if (Co(q, "nam nay"))
            {
                nam = now.Year;
            }

            return (ngay, thang, nam);
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
                ngay = d1;
                thang = mo1;
                nam = y1;
                return;
            }

            m = Regex.Match(q, @"\b(\d{1,2})\s*[/\-]\s*(20\d{2})\b");
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out var mo2)
                && int.TryParse(m.Groups[2].Value, out var y2)
                && mo2 is >= 1 and <= 12)
            {
                thang = mo2;
                nam = y2;
            }

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

        private static bool LaTrangThaiAnTrenApp(string? tt)
        {
            if (string.IsNullOrWhiteSpace(tt)) return false;
            var t = tt.Trim();
            return t.Equals("Nháp", StringComparison.OrdinalIgnoreCase)
                   || t.Equals("Đã hủy", StringComparison.OrdinalIgnoreCase)
                   || t.Equals("Hủy", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LaHuy(string? tt) =>
            !string.IsNullOrWhiteSpace(tt)
            && (tt.Trim().Equals("Đã hủy", StringComparison.OrdinalIgnoreCase)
                || tt.Trim().Equals("Hủy", StringComparison.OrdinalIgnoreCase));

        private static int? TrichMa(string q, string prefix)
        {
            var m = Regex.Match(q, $@"\b{prefix}\s*#?\s*(\d+)\b");
            return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
        }

        private static int DiemKhop(string q, string ten)
        {
            var t = ChuanHoa(ten);
            if (string.IsNullOrEmpty(t)) return 0;
            if (q.Contains(t) || t.Contains(q)) return 10;
            return t.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Count(w => w.Length >= 2 && q.Contains(w));
        }

        private static string? TrichTenThietBi(string q)
        {
            var s = q;
            string[] bo =
            {
                "thiet bi", "trang thai", "tinh trang", "dang o", "la gi", "bao tri", "sua chua",
                "gan nhat", "tiep theo", "lich", "ngay", "cua", "bao nhieu", "co may", "danh muc",
                "thang", "nam", "ho so", "da", "duoc", "hien", "tai", "va", "cho", "toi", "minh",
                "sap toi", "nao", "dang", "may", "he thong", "co", "the nao", "nhu the nao",
                "ra sao", "tinh hinh", "cho biet", "xem", "hien tai", "bay gio"
            };
            foreach (var b in bo)
                s = Regex.Replace(s, $@"\b{Regex.Escape(b)}\b", " ");
            s = Regex.Replace(s, @"\d{4}", " ");
            s = Regex.Replace(s, @"[^\p{L}\p{N}\s]", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length < 2 || Regex.IsMatch(s, @"^\d{1,2}$")) return null;
            return s;
        }
    }
}
