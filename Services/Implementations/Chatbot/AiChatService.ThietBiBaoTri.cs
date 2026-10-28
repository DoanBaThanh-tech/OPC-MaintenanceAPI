using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class AiChatService
    {
        // ===================== THIẾT BỊ THEO LỊCH BẢO TRÌ =====================

        private static bool LaHoiThietBiCanBaoTriTheoKy(string q, KhoangNgay khoang)
        {
            if (!khoang.CoNgay) return false;
            if (!Co(q, "thiet bi", "may moc")) return false;
            if (Co(q, "ho so", "hs", "vat tu", "quy trinh", "buoc")) return false;
            if (!Co(q, "bao tri", "can bao tri", "lich bao tri", "ke hoach bao tri", "den lich"))
                return false;
            return true;
        }

        private async Task<KetQuaXuLy> TraLoiThietBiCanBaoTriTheoKyAsync(
            string q, KhoangNgay khoang, CancellationToken ct)
        {
            var query = _db.ChiTietKeHoachBaoTris.AsNoTracking().AsQueryable();
            if (khoang.Tu is { } tu && khoang.Den is { } den)
                query = query.Where(c => c.NgayDuKienBaoTri >= tu && c.NgayDuKienBaoTri <= den);
            else
            {
                if (khoang.Ngay != null) query = query.Where(c => c.NgayDuKienBaoTri.Day == khoang.Ngay);
                if (khoang.Thang != null) query = query.Where(c => c.NgayDuKienBaoTri.Month == khoang.Thang);
                if (khoang.Nam != null) query = query.Where(c => c.NgayDuKienBaoTri.Year == khoang.Nam);
            }

            var rows = await query
                .Select(c => new
                {
                    c.MaThietBi,
                    Ten = c.MaThietBiNavigation != null ? c.MaThietBiNavigation.TenThietBi : ("TB#" + c.MaThietBi),
                    c.NgayDuKienBaoTri,
                    c.MaHoSoBaoTri,
                    TrangThai = c.MaHoSoBaoTriNavigation != null ? c.MaHoSoBaoTriNavigation.TrangThai : null
                })
                .ToListAsync(ct);

            // Giữ đúng thiết bị khi người dùng nêu tên hoặc TB#.
            var qc = ChuanHoa(q);
            var idMatch = Regex.Match(qc, @"\btb\s*#?\s*(\d+)\b");
            if (idMatch.Success && int.TryParse(idMatch.Groups[1].Value, out var maTb))
                rows = rows.Where(x => x.MaThietBi == maTb).ToList();
            else
            {
                var cleaned = qc;
                foreach (var word in new[]
                         {
                             "hom nay", "hom qua", "ngay mai", "tuan nay", "tuan truoc", "nam nay", "nam truoc",
                             "thiet bi", "may moc", "bao tri", "can", "lich", "ke hoach", "den", "trong",
                             "nam", "thang", "ngay", "co", "nao", "khong", "hay", "khong co", "cho toi", "xem",
                             "chi tiet", "cua", "va"
                         })
                    cleaned = Regex.Replace(cleaned, @"\b" + Regex.Escape(word) + @"\b", " ");
                cleaned = Regex.Replace(cleaned, @"\b20\d{2}\b", " ");
                var ten = TrichTenThietBi(cleaned);
                if (ten != null)
                {
                    var dsTen = rows.Select(x => x.Ten).Distinct()
                        .Select(t => new { Ten = t, Diem = DiemKhop(ten, t) })
                        .Where(x => x.Diem >= 2).OrderByDescending(x => x.Diem).ToList();
                    if (dsTen.Count == 0 || dsTen.Count > 1 && dsTen[0].Diem == dsTen[1].Diem)
                        return new(true,
                            "Bạn muốn kiểm tra thiết bị nào? Gửi TB# hoặc tên thiết bị giúp mình nhé.",
                            "thiet_bi_bao_tri_theo_ky");
                    var tenCan = dsTen[0].Ten;
                    rows = rows.Where(x => x.Ten == tenCan).ToList();
                }
            }

            var nhan = khoang.MoTa;
            if (rows.Count == 0)
                return new(true,
                    $"Dạ, theo lịch kế hoạch bảo trì {nhan}, **không có** thiết bị nào cần bảo trì.",
                    "thiet_bi_bao_tri_theo_ky", 0);

            var theoThietBi = rows
                .GroupBy(x => new { x.MaThietBi, x.Ten })
                .Select(g => new
                {
                    g.Key.MaThietBi,
                    g.Key.Ten,
                    Ngay = g.OrderBy(x => x.NgayDuKienBaoTri).First().NgayDuKienBaoTri,
                    SoMoc = g.Select(x => x.NgayDuKienBaoTri).Distinct().Count(),
                    HoSo = g.Where(x => x.MaHoSoBaoTri != null)
                        .OrderBy(x => x.NgayDuKienBaoTri).FirstOrDefault()
                })
                .OrderBy(x => x.Ngay).ThenBy(x => x.Ten)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, theo lịch kế hoạch bảo trì {nhan}, có **{theoThietBi.Count}** thiết bị cần bảo trì.");
            foreach (var x in theoThietBi)
                sb.AppendLine($"• TB#{x.MaThietBi} {x.Ten} — {x.Ngay:dd/MM/yyyy}"
                              + (x.SoMoc > 1 ? $" và {x.SoMoc - 1} mốc khác" : "")
                              + (x.HoSo?.MaHoSoBaoTri != null
                                  ? $" | BT#{x.HoSo.MaHoSoBaoTri}"
                                    + (string.IsNullOrWhiteSpace(x.HoSo.TrangThai) ? "" : $" ({x.HoSo.TrangThai})")
                                  : " | chưa gắn hồ sơ"));
            sb.Append("Mốc này lấy từ ngày dự kiến bảo trì trong kế hoạch, không phải trạng thái hiện tại của thiết bị.");
            return new(true, sb.ToString().TrimEnd(), "thiet_bi_bao_tri_theo_ky", theoThietBi.Count);
        }
    }
}
