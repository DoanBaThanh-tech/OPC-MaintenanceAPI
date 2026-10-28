using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class AiChatService
    {
        private static bool LaHoiYeuCauPhanCong(string q) => Co(q, "phan cong")
            && Co(q, "yeu cau", "lich su") && !Co(q, "buoc", "vat tu");

        private static bool LaHoiDuyetHoSoCuThe(string q) => Co(q, "duyet", "phe duyet", "tu choi", "trang thai")
            && !Co(q, "phan cong", "vat tu", "chot ngay", "lich su phe duyet", "huong dan", "cach duyet")
            && (Regex.IsMatch(q, @"\b(?:bt|sc|hs|ho so(?:\s+(?:bao tri|sua chua))?)\s*#?\s*\d+\b")
                || Co(q, "ho so", "hs") && Co(q, "cua toi", "toi tao", "toi co tao", "minh tao", "toi lap", "do toi tao")
                || Co(q, "ho so", "hs") && Co(q, "gan day", "moi tao", "vua tao", "moi day")
                   && Co(q, "toi tao", "cua toi", "minh tao", "toi lap"));

        /// <summary>Hỏi cả ngày/tháng: hồ sơ bảo trì được tạo trong kỳ đã duyệt hay chưa.</summary>
        private static bool LaHoiDuyetHoSoTheoKy(string q, string vt, KhoangNgay khoang) =>
            khoang.CoNgay
            && vt != "Nhân viên kỹ thuật"
            && Co(q, "ho so", "hs")
            && Co(q, "duyet", "phe duyet", "tu choi")
            && Co(q, "hay chua", "chua", "da duoc duyet", "duoc duyet chua", "phe duyet chua",
                "co duoc duyet khong", "duoc duyet khong", "da duyet khong")
            && !Co(q, "phan cong", "vat tu", "chot ngay", "lich su phe duyet", "huong dan", "cach duyet");

        private static bool NgayNhapKhongHopLe(string q)
        {
            foreach (Match m in Regex.Matches(q, @"\b(\d{1,2})\s*[/\-.]\s*(\d{1,2})\s*[/\-.]\s*(20\d{2})\b"))
                if (!DateOnly.TryParseExact($"{m.Groups[1].Value.PadLeft(2, '0')}/{m.Groups[2].Value.PadLeft(2, '0')}/{m.Groups[3].Value}",
                    "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _)) return true;
            foreach (Match m in Regex.Matches(q, @"\bngay\s*(\d{1,2})\s*(?:trong\s*)?thang\s*(\d{1,2})(?:\s*nam\s*(20\d{2}))?"))
            {
                var day = int.Parse(m.Groups[1].Value);
                var month = int.Parse(m.Groups[2].Value);
                var year = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 2000;
                if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return true;
            }
            return false;
        }

        private async Task<KetQuaXuLy> TraLoiDuyetHoSoCuTheAsync(
            int maNd, string vt, string q, KhoangNgay khoang, CancellationToken ct)
        {
            if (NgayNhapKhongHopLe(q))
                return new(true, "Ngày bạn nhập không hợp lệ; bạn gửi lại ngày/tháng/năm giúp mình nhé.", "duyet_ho_so_ca_nhan");
            // “Gần đây” không phải một ngày cụ thể: dùng cửa sổ 7 ngày và nói rõ mốc đã lọc.
            var ganDay = !khoang.CoNgay && Co(q, "gan day", "moi tao", "vua tao", "moi day")
                         && Co(q, "toi tao", "cua toi", "minh tao", "toi lap");
            if (ganDay)
            {
                var homNay = DateOnly.FromDateTime(DateTime.Today);
                khoang = new KhoangNgay(null, homNay.AddDays(-6), homNay, null, null, null, "7 ngày gần đây");
            }
            var generic = Regex.Match(q, @"\b(?:hs|ho so)(?:\s+(?:bao tri|sua chua))?\s*#?\s*(\d+)\b");
            var maBt = TrichMa(q, "bt");
            var maSc = TrichMa(q, "sc");
            var hoiSc = Co(q, "sua chua", "sc");
            if (generic.Success && maBt == null && maSc == null)
            {
                if (!int.TryParse(generic.Groups[1].Value, out var id))
                    return new(true, "Bạn gửi mã hồ sơ hợp lệ giúp mình nhé.", "duyet_ho_so_ca_nhan");
                if (hoiSc) maSc = id;
                else if (vt == "Tổ trưởng cơ điện" || Co(q, "bao tri", "bt")) maBt = id;
                else return new(true, "Bạn muốn tra hồ sơ bảo trì BT# hay sửa chữa SC#?", "duyet_ho_so_ca_nhan");
            }
            if (maBt == null && maSc == null && !khoang.CoNgay)
                return new(true, "Bạn gửi mã BT#/SC# hoặc ngày tạo hồ sơ muốn kiểm tra giúp mình nhé.", "duyet_ho_so_ca_nhan");
            if (maBt != null && maSc != null)
                return new(true, "Bạn muốn kiểm tra hồ sơ BT# hay SC# nào trước?", "duyet_ho_so_ca_nhan");

            // HS# được hiểu thành loại rõ ràng trước guard; không dùng mã chung để bỏ qua scope NVKT.
            var guardQ = q + (maBt != null ? $" bt#{maBt}" : maSc != null ? $" sc#{maSc}" : "");
            var denied = await KiemTraPhamViAsync(maNd, vt, guardQ, ct);
            if (denied != null) return new(true, denied, "chan_pham_vi");
            if (maBt == null && maSc == null && (Co(q, "bao tri", "bt") && hoiSc
                || vt != "Tổ trưởng cơ điện" && !Co(q, "bao tri", "bt") && !hoiSc))
                return new(true, "Bạn muốn kiểm tra hồ sơ bảo trì hay sửa chữa được tạo trong ngày đó?", "duyet_ho_so_ca_nhan");
            var caNhan = Co(q, "toi tao", "toi co tao", "minh tao", "toi lap", "do toi tao")
                || vt != "Nhân viên kỹ thuật" && Co(q, "cua toi");
            var laBt = maSc == null && !hoiSc;
            var nvCuaToi = _db.NhanViens.AsNoTracking().Where(n => n.MaNguoiDung == maNd).Select(n => n.MaNhanVien);

            IQueryable<HoSoDuyetHienTai> query;
            if (laBt)
            {
                var bt = _db.HoSoBaoTris.AsNoTracking().AsQueryable();
                if (maBt != null) bt = bt.Where(h => h.MaHoSoBaoTri == maBt);
                if (caNhan) bt = bt.Where(h => nvCuaToi.Contains(h.MaNhanVienTao));
                if (vt == "Nhân viên kỹ thuật") bt = bt.Where(h => _db.PhanCongCongViecs.Any(p =>
                    nvCuaToi.Contains(p.MaNhanVienThucHien) && p.TrangThai != "Đã hủy"
                    && (p.MaHoSoBaoTri == h.MaHoSoBaoTri || h.MaPhanCong == p.MaPhanCong)));
                query = bt.Select(h => new HoSoDuyetHienTai
                {
                    Ma = h.MaHoSoBaoTri, TrangThai = h.TrangThai, NgayTao = h.NgayTao,
                    NgayDuyet = h.NgayDuyet, NguoiDuyet = h.MaNhanVienDuyetNavigation != null ? h.MaNhanVienDuyetNavigation.HoTen : null,
                    LyDo = h.LyDoTuChoi,
                    TenThietBi = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                });
            }
            else
            {
                var sc = _db.HoSoSuaChuas.AsNoTracking().AsQueryable();
                if (maSc != null) sc = sc.Where(h => h.MaHoSoSuaChua == maSc);
                if (caNhan) sc = sc.Where(h => nvCuaToi.Contains(h.MaNhanVienTao));
                if (vt == "Nhân viên kỹ thuật") sc = sc.Where(h => _db.PhanCongCongViecs.Any(p =>
                    nvCuaToi.Contains(p.MaNhanVienThucHien) && p.TrangThai != "Đã hủy"
                    && (p.MaHoSoSuaChua == h.MaHoSoSuaChua || h.MaPhanCong == p.MaPhanCong)));
                query = sc.Select(h => new HoSoDuyetHienTai
                {
                    Ma = h.MaHoSoSuaChua, TrangThai = h.TrangThai, NgayTao = h.NgayTao,
                    NgayDuyet = h.NgayDuyet, NguoiDuyet = h.MaNhanVienDuyetNavigation != null ? h.MaNhanVienDuyetNavigation.HoTen : null,
                    MaNguoiDuyet = h.MaNhanVienDuyet,
                    QuyetDinhCuoi = _db.LichSuPheDuyets.Where(l => l.MaHoSoSuaChua == h.MaHoSoSuaChua)
                        .OrderByDescending(l => l.NgayDuyet).ThenByDescending(l => l.MaPheDuyet).Select(l => l.QuyetDinh).FirstOrDefault(),
                    LyDo = h.LyDoTuChoi,
                    TenThietBi = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                });
            }
            // Khi nói 'tôi tạo ngày...' chỉ lọc NgayTao, không trộn ngày kế hoạch/ngày duyệt.
            if (khoang.CoNgay)
            {
                if (khoang.Tu is { } tu && khoang.Den is { } den)
                {
                    var start = tu.ToDateTime(TimeOnly.MinValue);
                    var end = den.AddDays(1).ToDateTime(TimeOnly.MinValue);
                    query = query.Where(h => h.NgayTao >= start && h.NgayTao < end);
                }
                else
                {
                    if (khoang.Ngay != null) query = query.Where(h => h.NgayTao.Day == khoang.Ngay);
                    if (khoang.Thang != null) query = query.Where(h => h.NgayTao.Month == khoang.Thang);
                    if (khoang.Nam != null) query = query.Where(h => h.NgayTao.Year == khoang.Nam);
                }
            }
            var rows = await query.OrderByDescending(h => h.NgayTao).ThenBy(h => h.Ma).ToListAsync(ct);
            if (rows.Count == 0)
                return new(true, "Mình không tìm thấy hồ sơ khớp mã/ngày tạo trong phạm vi tài khoản của bạn.", "duyet_ho_so_ca_nhan", 0);
            var type = laBt ? "BT" : "SC";
            var sb = new StringBuilder();
            if (rows.Count > 1) sb.AppendLine($"Có **{rows.Count}** hồ sơ {type}{(caNhan ? " do bạn tạo" : "")}{(khoang.CoNgay ? " trong " + khoang.MoTa + " (lọc ngày tạo)" : "")}:");
            var soCanLietKe = TrichSoLuongLietKe(q);
            var hien = Math.Min(rows.Count, soCanLietKe ?? rows.Count);
            foreach (var h in rows.Take(hien))
            {
                var state = ChuanHoa(h.TrangThai);
                var approved = laBt
                    ? Co(state, "da duyet", "da phe duyet", "cho phan cong", "dang thuc hien", "da phan cong", "cho xac nhan", "da hoan thanh", "hoan thanh")
                    : Co(state, "da duyet", "da phe duyet") || h.MaNguoiDuyet != null && h.NgayDuyet != null
                        && h.QuyetDinhCuoi is "Duyệt" or "Đã duyệt";
                var rejected = Co(state, "tu choi", "da tu choi");
                var cancelled = LaHuy(h.TrangThai);
                var decision = cancelled ? "Hồ sơ đã hủy, không còn hiệu lực xử lý."
                    : rejected ? "Giám đốc đã từ chối hồ sơ, không phải đã duyệt."
                    : approved ? "Hồ sơ đã qua bước Giám đốc duyệt."
                    : !laBt ? "Chưa có bằng chứng Giám đốc duyệt hồ sơ này. Luồng tạo sửa chữa hiện gửi thẳng tới Tổ trưởng phân công, không qua Giám đốc."
                    : "Hồ sơ chưa được Giám đốc duyệt ở lượt xử lý hiện tại.";
                sb.AppendLine($"• {type}#{h.Ma} | {h.TenThietBi} | tạo {h.NgayTao:dd/MM/yyyy HH:mm} | trạng thái hiện tại: **{h.TrangThai}**.");
                sb.AppendLine(decision);
                if ((approved || rejected) && h.NgayDuyet != null)
                    sb.AppendLine($"{(rejected ? "Ngày từ chối" : "Ngày duyệt")}: {h.NgayDuyet:dd/MM/yyyy HH:mm}{(string.IsNullOrWhiteSpace(h.NguoiDuyet) ? "" : " — " + h.NguoiDuyet)}.");
                if (rejected && !string.IsNullOrWhiteSpace(h.LyDo)) sb.AppendLine("Lý do: " + h.LyDo);
            }
            if (soCanLietKe == null && rows.Count > hien)
                sb.AppendLine($"… còn {rows.Count - hien} hồ sơ khác.");
            if (soCanLietKe != null && soCanLietKe > hien)
                sb.AppendLine($"Hiện chỉ có {hien} hồ sơ phù hợp, ít hơn {soCanLietKe} hồ sơ bạn yêu cầu.");
            if (khoang.CoNgay) sb.AppendLine("Ngày được đối chiếu là ngày tạo hiện đang lưu trên app, không phải ngày duyệt hoặc ngày kế hoạch.");
            return new(true, sb.ToString().TrimEnd(), "duyet_ho_so_ca_nhan", rows.Count);
        }

        private sealed class HoSoDuyetHienTai
        {
            public int Ma { get; init; }
            public string TrangThai { get; init; } = "";
            public DateTime NgayTao { get; init; }
            public DateTime? NgayDuyet { get; init; }
            public string? NguoiDuyet { get; init; }
            public int? MaNguoiDuyet { get; init; }
            public string? QuyetDinhCuoi { get; init; }
            public string? LyDo { get; init; }
            public string TenThietBi { get; init; } = "";
        }
    }
}
