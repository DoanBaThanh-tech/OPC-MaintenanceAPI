using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OPC.MaintenanceAPI.Services.Implementations;

public partial class AiChatService
{
    private static readonly CultureInfo VanHoaThongKeVatTu = CultureInfo.GetCultureInfo("vi-VN");

    private sealed record KyThongKeVatTu(DateOnly Tu, DateOnly Den, string Nhan, string Loai);
    private sealed record MocThongKeVatTu(KhoangNgay Khoang, int ViTri, int KetThuc, bool NamRieng);

    // Longest date alternatives precede month/year alternatives, so a day is never counted twice.
    private const string MauKyThongKeVatTu = @"(?<![\p{L}\p{N}])(?:
        (?<rel>hom\s*(?:nay|qua)|ngay\s*mai|(?:thang|nam|tuan)\s*(?:nay|truoc))
        |(?:ngay\s*)?(?<d>\d{1,3})\s*thang\s*(?<m>\d{1,3})(?:\s*(?:nam\s*)?(?<y>\d{4,}))?
        |(?:ngay\s*)?(?<d>\d{1,3})\s*[/\.\-]\s*(?<m>\d{1,3})(?:\s*[/\.\-]\s*(?<y>\d{4,}))?
        |thang\s*(?<m>\d{1,3})(?:\s*(?:[/\.\-]\s*|nam\s*|\s+)(?<y>\d{4,}))?
        |(?<m>\d{1,3})\s*[/\.\-]\s*(?<y>\d{4,})
        |ngay\s*(?<d>\d{1,3})
        |nam\s*(?<y>\d{4,})
        |(?<y>20\d{2}|21\d{2})
        )(?![\p{L}\p{N}])";

    private static bool LaHoiThongKeVatTu(string q)
    {
        q = ChuanHoa(q);
        if (!Co(q, "vat tu")) return false;
        // Explicit document details belong to the existing scoped handler, not company aggregates.
        if (Regex.IsMatch(q, @"\b(?:hs|bt|sc|vt)\s*#?\s*\d+\b")
            || Regex.IsMatch(q, @"\bho so(?:\s+vat tu)?\s*(?:#|so|ma)\s*\d+\b"))
            return false;

        var coKy = Regex.IsMatch(q, @"\b(?:ngay|hom|thang|nam|tuan)\b|\b\d{1,2}\s*[/\.\-]\s*\d{1,4}\b");
        var coChiSo = Co(q, "so luong", "su dung", "da dung", "tieu hao", "tong tien", "chi phi",
            "thong ke", "so do", "bieu do", "so sanh", "chenh lech", "phan tram", "ty le")
            || q.Contains('%');
        // “Bao nhiêu hồ sơ vật tư [của tôi]” is still a document-scoped question even with a period;
        // only an explicit aggregate/cost/chart signal moves it to company statistics.
        if (Co(q, "ho so", "hs")
            && !Co(q, "tong tien", "chi phi", "thong ke", "so do", "bieu do", "so sanh", "chenh lech",
                "phan tram", "so luong", "da dung", "tieu hao")
            && !q.Contains('%')) return false;
        return coKy || coChiSo;
    }

    private async Task<KetQuaXuLy> TraLoiThongKeVatTuAsync(string vt, string q, CancellationToken ct)
    {
        // Defense in depth even when routing/follow-ups invoke this method directly.
        if (vt != "Giám đốc")
            return new KetQuaXuLy(true, "Bạn không có quyền xem thống kê này.", "thong_ke_vat_tu");

        q = ChuanHoa(q);
        if (!TrichCacKyThongKeVatTu(q, out var cacKy, out var soSanh, out var loi))
            return new KetQuaXuLy(true, loi, "thong_ke_vat_tu");

        var theoThucHien = Co(q, "ngay thuc hien");
        // Read each requested year once in this answer, bounded to its requested dates.
        // No conversation numbers, remembered totals, static cache, or Take limit are used.
        var namCanDoc = cacKy.SelectMany(k => Enumerable.Range(k.Tu.Year,
            k.Den.AddDays(-1).Year - k.Tu.Year + 1)).Distinct().ToList();
        var duLieu = new List<ThongKeVatTuDongBo.TheoNgay>();
        var hoSos = new List<ThongKeVatTuDongBo.HoSoThongKe>();
        // A week across New Year can be attributed to both years; count each header once within that period.
        var kyVatQuaNam = cacKy.Any(k => k.Tu.Year != k.Den.AddDays(-1).Year);
        foreach (var nam in namCanDoc)
        {
            var trongNam = cacKy.Where(k => k.Tu.Year <= nam && k.Den.AddDays(-1).Year >= nam).ToList();
            var tu = trongNam.Min(k => k.Tu);
            var den = trongNam.Max(k => k.Den);
            if (kyVatQuaNam)
                hoSos.AddRange(await ThongKeVatTuDongBo.LayHoSoAsync(_db, nam, tu, den, theoThucHien, ct));
            else
                duLieu.AddRange(await ThongKeVatTuDongBo.LayTheoNgayAsync(_db, nam, tu, den, theoThucHien, ct));
        }

        var tong = cacKy.Select(k => new
        {
            Ky = k,
            SoLuong = kyVatQuaNam
                ? hoSos.Where(x => TrongKyVatTu(x, k, theoThucHien))
                    .GroupBy(x => x.MaHoSoVatTu).Sum(g => g.First().SoLuong)
                : duLieu.Where(x => x.Ngay >= k.Tu && x.Ngay < k.Den).Sum(x => x.SoLuong),
            TongTien = kyVatQuaNam
                ? hoSos.Where(x => TrongKyVatTu(x, k, theoThucHien))
                    .GroupBy(x => x.MaHoSoVatTu).Sum(g => g.First().TongTien)
                : duLieu.Where(x => x.Ngay >= k.Tu && x.Ngay < k.Den).Sum(x => x.TongTien)
        }).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Thống kê vật tư theo dữ liệu hiện tại:");
        foreach (var x in tong)
            sb.AppendLine($"- {x.Ky.Nhan}: số lượng {DinhDangVatTu(x.SoLuong, false)}; tổng tiền {DinhDangVatTu(x.TongTien, true)} đồng.");

        if (soSanh)
        {
            sb.AppendLine($"Kỳ gốc (nêu trước): {tong[0].Ky.Nhan}; kỳ sau (nêu sau): {tong[1].Ky.Nhan}.");
            sb.AppendLine(SoSanhChiSoVatTu("Số lượng", tong[0].SoLuong, tong[1].SoLuong, false));
            sb.AppendLine(SoSanhChiSoVatTu("Tổng tiền", tong[0].TongTien, tong[1].TongTien, true));
            sb.AppendLine("Công thức riêng cho từng chỉ số: (kỳ sau - kỳ gốc) / kỳ gốc × 100%; làm tròn 6 chữ số thập phân.");
        }

        sb.Append(theoThucHien
            ? "Lọc theo ngày thực hiện ghi trên hồ sơ vật tư, không phải mốc thống kê của biểu đồ Giám đốc; vì vậy có thể khác biểu đồ tháng."
            : "Theo mốc thống kê của biểu đồ Giám đốc, không khẳng định đây là ngày thực hiện vật tư: BT ưu tiên ngày kế hoạch theo năm đang xem, nếu ngoài năm đó dùng ngày hoàn thành/duyệt/tạo; SC dùng ngày hoàn thành/duyệt/tạo; hồ sơ không liên kết dùng ngày gửi GĐ hoặc ngày thực hiện.");
        sb.Append(" Chỉ tính hồ sơ vật tư đã được GĐ xác nhận/duyệt và liên kết đủ điều kiện hoàn thành; cộng số lượng chi tiết và tổng tiền lưu trên hồ sơ, không đếm loại vật tư hay tính lại đơn giá.");
        return new KetQuaXuLy(true, sb.ToString().TrimEnd(), "thong_ke_vat_tu");
    }

    private static bool TrongKyVatTu(ThongKeVatTuDongBo.HoSoThongKe hoSo, KyThongKeVatTu ky, bool theoThucHien)
    {
        var ngay = theoThucHien ? hoSo.NgayThucHien : hoSo.NgayThongKe;
        return ngay >= ky.Tu && ngay < ky.Den;
    }

    private static string DinhDangVatTu(decimal giaTri, bool tien) =>
        giaTri.ToString(tien ? "N2" : "N0", VanHoaThongKeVatTu);

    private static string SoSanhChiSoVatTu(string nhan, decimal goc, decimal sau, bool tien)
    {
        var delta = sau - goc;
        var chenhLech = (delta > 0 ? "+" : "") + DinhDangVatTu(delta, tien) + (tien ? " đồng" : "");
        var thayDoi = delta == 0 ? "không thay đổi" : delta > 0 ? "tăng" : "giảm";
        if (goc == 0)
            return $"- {nhan}: {thayDoi}, chênh lệch {chenhLech}; không xác định % vì kỳ gốc bằng 0.";
        var phanTram = decimal.Round(delta / goc * 100m, 6, MidpointRounding.AwayFromZero);
        return $"- {nhan}: {thayDoi}, chênh lệch {chenhLech}; {(phanTram > 0 ? "+" : "")}{phanTram.ToString("N6", VanHoaThongKeVatTu)}%.";
    }

    private static bool TrichCacKyThongKeVatTu(
        string q, out List<KyThongKeVatTu> cacKy, out bool soSanh, out string loi)
    {
        cacKy = new();
        soSanh = Co(q, "so sanh", "chenh lech", "phan tram", "ty le", "tang giam", "tang bao nhieu", "giam bao nhieu")
                 || q.Contains('%');
        loi = "Bạn muốn thống kê vật tư tháng nào/năm nào hoặc ngày cụ thể nào? Vui lòng ghi rõ kỳ cần xem.";
        // 05/08/27 only matches 05/08; the short year must not silently become the current year.
        foreach (Match shortYear in Regex.Matches(q, @"\b\d{1,2}\s*[/\.\-]\s*\d{1,2}\s*[/\.\-]\s*(\d{1,3})\b"))
        {
            if (shortYear.Groups[1].Value.Length < 4)
            {
                loi = "Năm phải ghi đủ bốn chữ số, ví dụ 2027. Mình không tự đổi năm viết tắt sang năm hiện tại.";
                return false;
            }
        }
        var matches = Regex.Matches(q, MauKyThongKeVatTu, RegexOptions.IgnorePatternWhitespace);
        var mocs = new List<MocThongKeVatTu>();
        foreach (Match match in matches)
        {
            KhoangNgay k;
            if (match.Groups["rel"].Success)
                k = TrichKhoangNgay(match.Value);
            else
            {
                int? d = match.Groups["d"].Success ? int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture) : null;
                int? m = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : null;
                int? y = null;
                if (match.Groups["y"].Success)
                {
                    if (!int.TryParse(match.Groups["y"].Value, out var year) || year is < 2026 or > 2100)
                    {
                        loi = "Năm thống kê vật tư phải nằm trong khoảng 2026–2100. Vui lòng ghi lại năm hợp lệ; không tự đổi sang năm hiện tại.";
                        return false;
                    }
                    y = year;
                }
                if (d is < 1 or > 31 || m is < 1 or > 12)
                {
                    loi = "Ngày/tháng không hợp lệ. Vui lòng ghi một ngày có thật hoặc tháng từ 1 đến 12 và năm 2026–2100.";
                    return false;
                }
                k = new KhoangNgay(null, null, null, d, m, y, "");
            }
            if (k.Nam is < 2026 or > 2100 || k.Tu?.Year is < 2026 or > 2100 || k.Den?.Year is < 2026 or > 2100)
            {
                loi = "Kỳ thống kê phải thuộc các năm 2026–2100. Vui lòng chọn kỳ hợp lệ.";
                return false;
            }
            mocs.Add(new MocThongKeVatTu(k, match.Index, match.Index + match.Length,
                k.Nam != null && k.Thang == null && k.Ngay == null && k.Tu == null));
        }

        // A separate trailing year is only a qualifier across whitespace / “của” / “trong”,
        // never across “với”, “và”, “đến”, which would silently swallow another requested period.
        if (mocs.Count >= 2 && mocs[^1].NamRieng && mocs[^2].Khoang.Nam == null)
        {
            var gap = q[mocs[^2].KetThuc..mocs[^1].ViTri];
            if (Regex.IsMatch(gap, @"^\s*(?:(?:cua|trong)\s*)?$"))
            {
                var year = mocs[^1].Khoang.Nam;
                mocs.RemoveAt(mocs.Count - 1);
                for (var i = 0; i < mocs.Count; i++)
                    if (mocs[i].Khoang.Nam == null)
                        mocs[i] = mocs[i] with { Khoang = mocs[i].Khoang with { Nam = year } };
            }
        }

        soSanh |= mocs.Count > 1 && Co(q, "voi", "va", "giua");
        if (mocs.Count > 2 || (soSanh && mocs.Count != 2) || (!soSanh && mocs.Count != 1))
        {
            if (soSanh || mocs.Count > 1)
                loi = "Vui lòng nêu đúng hai kỳ so sánh, đủ tháng/ngày và năm (ví dụ: tháng 8 với tháng 9 năm 2026). Kỳ nêu trước là kỳ gốc; không tự đoán kỳ còn thiếu.";
            return false;
        }

        var residue = Regex.Replace(q, MauKyThongKeVatTu, " ", RegexOptions.IgnorePatternWhitespace);
        residue = Regex.Replace(residue, @"\b(?:ngay thuc hien|theo ngay)\b", " ");
        if (Regex.IsMatch(residue, @"\b(?:ngay|thang|nam|tuan|hom)\b"))
        {
            loi = "Chưa xác định đầy đủ kỳ thống kê. Vui lòng ghi rõ ngày/tháng/năm; nếu so sánh, ghi đúng hai kỳ.";
            return false;
        }
        // “tháng 8 với tháng 9 năm 2026”: the trailing year applies to both months.
        // An explicit year on the FIRST period does not license guessing the SECOND year.
        if (soSanh && mocs[0].Khoang.Nam == null && mocs[1].Khoang.Nam != null
            && mocs[1].Khoang.Tu == null)
            mocs[0] = mocs[0] with { Khoang = mocs[0].Khoang with { Nam = mocs[1].Khoang.Nam } };

        foreach (var moc in mocs)
        {
            var k = moc.Khoang;
            if (k.Nam == null && k.Tu == null)
            {
                if (soSanh)
                {
                    loi = "Vui lòng ghi năm của cả hai kỳ so sánh (hoặc dùng tháng này/tháng trước). Không tự đoán năm.";
                    return false;
                }
                k = k with { Nam = DateTime.Today.Year };
            }
            if (!TaoKyThongKeVatTu(k, out var ky))
            {
                loi = "Kỳ chưa đầy đủ hoặc ngày không tồn tại. Vui lòng ghi rõ tháng/năm hoặc ngày/tháng/năm hợp lệ trong 2026–2100.";
                return false;
            }
            cacKy.Add(ky!);
        }
        if (soSanh && cacKy[0].Loai != cacKy[1].Loai)
        {
            loi = "Vui lòng nêu hai kỳ cùng loại để so sánh rõ ràng (hai tháng, hai ngày, hai tuần hoặc hai năm), đủ ngày/tháng/năm.";
            return false;
        }
        return true;
    }

    private static bool TaoKyThongKeVatTu(KhoangNgay k, out KyThongKeVatTu? ky)
    {
        ky = null;
        if (k.Tu is { } tu && k.Den is { } den)
        {
            if (tu.Year is < 2026 or > 2100 || den.Year is < 2026 or > 2100) return false;
            ky = new(tu, den.AddDays(1), $"tuần {tu:dd/MM/yyyy}–{den:dd/MM/yyyy}", "tuan");
            return true;
        }
        if (k.Nam is not { } nam || nam is < 2026 or > 2100) return false;
        if (k.Ngay is { } d)
        {
            if (k.Thang is not { } m || m is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(nam, m)) return false;
            var ngay = new DateOnly(nam, m, d);
            ky = new(ngay, ngay.AddDays(1), $"ngày {ngay:dd/MM/yyyy}", "ngay");
        }
        else if (k.Thang is { } thang)
        {
            if (thang is < 1 or > 12) return false;
            var dau = new DateOnly(nam, thang, 1);
            ky = new(dau, dau.AddMonths(1), $"tháng {thang}/{nam}", "thang");
        }
        else
        {
            var dau = new DateOnly(nam, 1, 1);
            ky = new(dau, dau.AddYears(1), $"năm {nam}", "nam");
        }
        return true;
    }
}
