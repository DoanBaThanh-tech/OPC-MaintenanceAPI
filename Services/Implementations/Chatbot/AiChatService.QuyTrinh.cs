using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class AiChatService
    {
        // ===================== QUY TRÌNH THIẾT BỊ / HỒ SƠ =====================

        private static bool LaHoiQuyTrinh(string q) =>
            Co(q, "quy trinh", "quy trinh lam viec", "cac buoc", "cong doan", "buoc cong viec",
                "bao nhieu buoc", "may buoc", "so buoc", "buoc thuc hien", "buoc da lam",
                "buoc da chon", "buoc lam");

        /// <summary>Quy trình mẫu của thiết bị (QuyTrinhThietBi), không phải tiến độ một hồ sơ.</summary>
        private static bool LaHoiQuyTrinhThietBi(string q)
        {
            if (!LaHoiQuyTrinh(q)) return false;
            if (LaHoiQuyTrinhHoSo(q)) return false;
            var qc = ChuanHoa(q);
            return !Co(q, "ho so", "hs")
                   && TrichMa(qc, "bt") == null && TrichMa(qc, "sc") == null;
        }

        /// <summary>Quy trình đã chọn / tiến độ bên trong một hồ sơ BT hoặc SC.</summary>
        private static bool LaHoiQuyTrinhHoSo(string q)
        {
            var hoiNguoi = Co(q, "nhan vien nao", "ai dam nhan", "ai thuc hien", "ai lam",
                "do ai", "bao nhieu nguoi duoc phan cong", "may nguoi duoc phan cong");
            if (!LaHoiQuyTrinh(q) && !hoiNguoi) return false;
            var qc = ChuanHoa(q);
            var coMa = TrichMa(qc, "bt") != null || TrichMa(qc, "sc") != null
                       || Regex.IsMatch(qc, @"\b(?:bt|sc)\s*(?:so|ma)\s*\d+\b");
            var coHoSo = Co(q, "ho so", "hs") || coMa;
            // Hỏi “nhân viên nào” mà không nêu hồ sơ là câu hỏi phân công chung, không phải quy trình.
            if (hoiNguoi) return coHoSo;
            return coHoSo || Co(q, "nhan vien", "nguoi duoc phan cong", "duoc phan cong");
        }

        private static (int? Bt, int? Sc) TrichMaQuyTrinh(string q, string vt)
        {
            var qc = ChuanHoa(q);
            var bt = TrichMa(qc, "bt");
            var sc = TrichMa(qc, "sc");
            if (bt == null)
            {
                var m = Regex.Match(qc, @"\bbt\s*(?:so|ma)\s*(\d+)\b");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var id)) bt = id;
            }
            if (sc == null)
            {
                var m = Regex.Match(qc, @"\bsc\s*(?:so|ma)\s*(\d+)\b");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var id)) sc = id;
            }
            if (bt == null && sc == null)
            {
                var m = Regex.Match(qc,
                    @"\b(?:ho so|hs)(?:\s+(?<loai>bao tri|sua chua))?\s*(?:#|so|ma)?\s*(?<id>\d+)\b");
                if (m.Success && int.TryParse(m.Groups["id"].Value, out var id))
                {
                    var loai = m.Groups["loai"].Value;
                    if (loai.Contains("sua chua", StringComparison.Ordinal)) sc = id;
                    else if (loai.Contains("bao tri", StringComparison.Ordinal)) bt = id;
                    else if (vt == "Tổ trưởng cơ điện") bt = id;
                }
            }
            return (bt, sc);
        }

        private async Task<KetQuaXuLy> TraLoiQuyTrinhThietBiAsync(string vt, string q, CancellationToken ct)
        {
            var tbs = await _db.ThietBis.AsNoTracking()
                .Select(t => new { t.MaThietBi, t.TenThietBi })
                .ToListAsync(ct);

            var qc = ChuanHoa(q);
            var idMatch = Regex.Match(qc, @"\btb\s*#?\s*(\d+)\b");
            var tb = idMatch.Success && int.TryParse(idMatch.Groups[1].Value, out var maTb)
                ? tbs.FirstOrDefault(x => x.MaThietBi == maTb)
                : null;

            if (tb == null)
            {
                var cleaned = qc;
                foreach (var word in new[]
                         {
                             "quy trinh", "thuc hien", "cac buoc", "bao nhieu buoc", "may buoc", "so buoc",
                             "chi tiet", "phan", "cua", "thiet bi", "may moc", "gom", "co", "la", "gi",
                             "trong", "hien", "cho toi", "xem", "nha may"
                         })
                    cleaned = Regex.Replace(cleaned, @"\b" + Regex.Escape(word) + @"\b", " ");
                var ten = TrichTenThietBi(cleaned);
                if (ten != null)
                {
                    var hit = tbs.Select(x => new { x, diem = DiemKhop(ten, x.TenThietBi) })
                        .Where(x => x.diem >= 2)
                        .OrderByDescending(x => x.diem)
                        .ToList();
                    if (hit.Count > 0 && (hit.Count == 1 || hit[0].diem > hit[1].diem))
                        tb = hit[0].x;
                }
            }

            if (tb == null)
            {
                // Câu hỏi quy trình chung không nêu thiết bị: trả lại cho nhánh kiến thức.
                if (!Co(q, "thiet bi", "may moc") && !idMatch.Success)
                    return new(false, null);
                return new(true,
                    "Bạn muốn xem quy trình của thiết bị nào? Bạn gửi TB# hoặc tên thiết bị giúp mình nhé.",
                    "quy_trinh_thiet_bi");
            }

            var quyTrinh = await _db.QuyTrinhThietBis.AsNoTracking()
                .Where(x => x.MaThietBi == tb.MaThietBi)
                .OrderBy(x => x.LoaiCongViec).ThenBy(x => x.SoBuoc)
                .Select(x => new { x.LoaiCongViec, x.SoBuoc, x.MoTaBuoc })
                .ToListAsync(ct);

            if (quyTrinh.Count == 0)
                return new(true,
                    $"Dạ, thiết bị **{tb.TenThietBi}** (TB#{tb.MaThietBi}) chưa được cấu hình bước quy trình bảo trì/sửa chữa.",
                    "quy_trinh_thiet_bi", 0, "TB#" + tb.MaThietBi);

            var (hoiBt, hoiSc) = LoaiHoSoTrongCau(q);
            var coChiLoai = Co(q, "bao tri", "bt", "sua chua", "sc");
            var nhom = quyTrinh
                .GroupBy(x => ChuanHoa(x.LoaiCongViec) == "sua chua" ? "Sửa chữa" : "Bảo trì")
                .Where(g => !coChiLoai || (hoiBt && g.Key == "Bảo trì") || (hoiSc && g.Key == "Sửa chữa"))
                .OrderBy(g => g.Key == "Bảo trì" ? 0 : 1)
                .ToList();

            if (nhom.Count == 0)
                return new(true,
                    $"Dạ, thiết bị **{tb.TenThietBi}** (TB#{tb.MaThietBi}) chưa có quy trình đúng loại bạn hỏi.",
                    "quy_trinh_thiet_bi", 0, "TB#" + tb.MaThietBi);

            var lietKe = LaYeuCauLietKe(q) || Co(q, "cac buoc", "gom nhung buoc", "buoc gi",
                "nhung buoc", "noi dung tung buoc");
            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, thiết bị **{tb.TenThietBi}** (TB#{tb.MaThietBi}) có quy trình thực hiện:");
            var tong = 0;
            foreach (var g in nhom)
            {
                var buoc = g.GroupBy(x => x.SoBuoc).Select(x => x.First())
                    .OrderBy(x => x.SoBuoc).ToList();
                tong += buoc.Count;
                sb.AppendLine($"• {g.Key}: **{buoc.Count}** bước.");
                if (lietKe)
                    foreach (var b in buoc.Take(12))
                        sb.AppendLine($"  B{b.SoBuoc}: {b.MoTaBuoc}");
            }
            if (nhom.Sum(g => g.GroupBy(x => x.SoBuoc).Count()) > 12 && lietKe)
                sb.AppendLine("… chỉ hiển thị tối đa 12 bước mỗi loại; phần còn lại xem trong chi tiết thiết bị.");
            sb.Append("Đây là quy trình mẫu gắn với thiết bị, không phải số bước đã thực hiện trong một hồ sơ.");
            return new(true, sb.ToString().TrimEnd(), "quy_trinh_thiet_bi", tong, "TB#" + tb.MaThietBi);
        }

        private async Task<KetQuaXuLy> TraLoiQuyTrinhHoSoAsync(
            int maNd, string vt, string q, CancellationToken ct)
        {
            var (maBt, maSc) = TrichMaQuyTrinh(q, vt);
            if (maBt != null && maSc != null)
                return new(true, "Bạn muốn kiểm tra quy trình của hồ sơ BT# hay SC# nào trước?",
                    "quy_trinh_ho_so");
            if (maBt == null && maSc == null)
            {
                if (Co(q, "hoan thanh", "da xong"))
                    return await TraLoiQuyTrinhHoanThanhAsync(maNd, vt, q, ct);
                return new(true,
                    "Bạn gửi mã hồ sơ cụ thể (ví dụ BT#12 hoặc SC#5) để mình xem quy trình, người thực hiện và số nhân viên được phân công nhé.",
                    "quy_trinh_ho_so");
            }

            var laBt = maBt != null;
            var ma = laBt ? maBt!.Value : maSc!.Value;

            if (vt == "Nhân viên kỹ thuật")
            {
                var duocGiao = await _db.PhanCongCongViecs.AsNoTracking().AnyAsync(p =>
                    p.TrangThai != "Đã hủy" && (laBt
                        ? p.MaHoSoBaoTri == ma || p.HoSoBaoTri != null && p.HoSoBaoTri.MaHoSoBaoTri == ma
                        : p.MaHoSoSuaChua == ma || p.HoSoSuaChua != null && p.HoSoSuaChua.MaHoSoSuaChua == ma), ct);
                if (!duocGiao)
                    return new(true, "Mình chỉ tra hồ sơ được phân công cho bạn.", "chan_pham_vi");
            }

            var thongTin = laBt
                ? await _db.HoSoBaoTris.AsNoTracking().Where(h => h.MaHoSoBaoTri == ma)
                    .Select(h => new
                    {
                        TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "",
                        h.TrangThai
                    }).FirstOrDefaultAsync(ct)
                : await _db.HoSoSuaChuas.AsNoTracking().Where(h => h.MaHoSoSuaChua == ma)
                    .Select(h => new
                    {
                        TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "",
                        h.TrangThai
                    }).FirstOrDefaultAsync(ct);

            if (thongTin == null)
                return new(true, "Mình không tìm thấy hồ sơ khớp mã trong phạm vi tài khoản của bạn.",
                    "quy_trinh_ho_so", 0);

            var buoc = await _db.TienDoBuocQuyTrinhs.AsNoTracking()
                .Where(t => laBt ? t.MaHoSoBaoTri == ma : t.MaHoSoSuaChua == ma)
                .Select(t => new
                {
                    t.SoBuoc, t.MoTaBuoc, t.MaNhanVien, t.TenNhanVien,
                    t.TrangThai, t.ThoiDiemCapNhat
                })
                .ToListAsync(ct);

            var uniq = buoc.GroupBy(x => x.SoBuoc)
                .Select(g => g.OrderByDescending(x => x.ThoiDiemCapNhat).First())
                .OrderBy(x => x.SoBuoc).ToList();

            var maNvCanTen = uniq.Where(x => x.MaNhanVien > 0 && string.IsNullOrWhiteSpace(x.TenNhanVien))
                .Select(x => x.MaNhanVien).Distinct().ToList();
            var tenNv = await _db.NhanViens.AsNoTracking()
                .Where(n => maNvCanTen.Contains(n.MaNhanVien))
                .Select(n => new { n.MaNhanVien, n.HoTen })
                .ToDictionaryAsync(x => x.MaNhanVien, x => x.HoTen, ct);

            var pc = await _db.PhanCongCongViecs.AsNoTracking()
                .Where(p => laBt
                    ? p.MaHoSoBaoTri == ma || p.HoSoBaoTri != null && p.HoSoBaoTri.MaHoSoBaoTri == ma
                    : p.MaHoSoSuaChua == ma || p.HoSoSuaChua != null && p.HoSoSuaChua.MaHoSoSuaChua == ma)
                .Select(p => new
                {
                    p.MaNhanVienThucHien,
                    Ten = _db.NhanViens.Where(n => n.MaNhanVien == p.MaNhanVienThucHien)
                        .Select(n => n.HoTen).FirstOrDefault(),
                    p.TrangThai,
                    p.MaPhanCong
                })
                .ToListAsync(ct);

            var nguoiPhanCong = pc
                .Where(p => !LaHuy(p.TrangThai))
                .GroupBy(p => p.MaNhanVienThucHien)
                .Select(g =>
                {
                    var p = g.OrderByDescending(x => x.MaPhanCong).First();
                    return new
                    {
                        p.MaNhanVienThucHien,
                        Ten = string.IsNullOrWhiteSpace(p.Ten) ? "NV#" + p.MaNhanVienThucHien : p.Ten,
                        p.TrangThai,
                        p.MaPhanCong
                    };
                })
                .OrderBy(x => x.Ten)
                .ToList();

            var xong = uniq.Count(x => LaBuocXong(x.TrangThai));
            var con = uniq.Count(x => LaBuocCon(x.TrangThai));
            var nvktNhan = uniq.Count(x => x.MaNhanVien > 0);
            var nvktXong = uniq.Count(x => x.MaNhanVien > 0 && LaBuocXong(x.TrangThai));

            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, hồ sơ **{(laBt ? "BT" : "SC")}#{ma}**"
                          + (string.IsNullOrWhiteSpace(thongTin.TenTb) ? "" : $" — {thongTin.TenTb}")
                          + $", trạng thái **{thongTin.TrangThai}**.");
            sb.AppendLine($"• Quy trình: **{uniq.Count}** bước; đã xong **{xong}**, còn **{con}** bước chưa có kết quả.");
            if (con > 0 && (thongTin.TrangThai == "Đã hoàn thành" || thongTin.TrangThai == "Hoàn thành"))
                sb.AppendLine($"Hồ sơ đang ở trạng thái hoàn thành nhưng còn {con} bước chưa ghi kết quả; mình phản ánh đúng dữ liệu đang lưu trên app.");
            sb.AppendLine($"• NVKT đã nhận: **{nvktNhan}** bước, trong đó đã xong **{nvktXong}** bước.");

            var theoNv = uniq.Where(x => x.MaNhanVien > 0)
                .GroupBy(x => x.MaNhanVien)
                .Select(g => new
                {
                    Ten = g.Select(x => string.IsNullOrWhiteSpace(x.TenNhanVien)
                            ? tenNv.GetValueOrDefault(x.MaNhanVien, "NV#" + x.MaNhanVien)
                            : x.TenNhanVien)
                        .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? ("NV#" + g.Key),
                    SoBuoc = g.Count(),
                    DaXong = g.Count(x => LaBuocXong(x.TrangThai)),
                    Buoc = g.Select(x => x.SoBuoc).OrderBy(x => x).ToList()
                })
                .OrderByDescending(x => x.SoBuoc).ThenBy(x => x.Ten)
                .ToList();

            if (theoNv.Count == 0)
                sb.AppendLine("• Chưa có NVKT nào nhận bước; các bước hiện chỉ ở diện Tổ trưởng chọn sẵn.");
            else
                foreach (var n in theoNv)
                    sb.AppendLine($"• {n.Ten}: **{n.SoBuoc}** bước (đã xong {n.DaXong})"
                                  + $" — B{string.Join(", B", n.Buoc)}.");

            var soNguoi = nguoiPhanCong.Count;
            var tuChoi = nguoiPhanCong.Count(x =>
                string.Equals((x.TrangThai ?? "").Trim(), "Từ chối", StringComparison.OrdinalIgnoreCase));
            if (soNguoi == 0)
                sb.AppendLine("• Phân công: chưa có nhân viên nào còn hiệu lực (không tính PC đã hủy).");
            else
                sb.AppendLine($"• Phân công: **{soNguoi}** người được phân công"
                              + (tuChoi > 0 ? $" (trong đó {tuChoi} người đã từ chối)" : "")
                              + $": {string.Join(", ", nguoiPhanCong.Select(x => x.Ten))}.");

            if (uniq.Count > 0)
                foreach (var b in uniq.Take(16))
                {
                    var ten = b.MaNhanVien > 0
                        ? string.IsNullOrWhiteSpace(b.TenNhanVien)
                            ? tenNv.GetValueOrDefault(b.MaNhanVien, "NV#" + b.MaNhanVien)
                            : b.TenNhanVien
                        : null;
                    sb.AppendLine($"B{b.SoBuoc}: {b.MoTaBuoc} ({MoTaTrangThaiBuoc(b.TrangThai)}"
                                  + (ten != null ? $" — {ten}" : "") + ")");
                }
            if (uniq.Count > 16) sb.AppendLine($"… còn {uniq.Count - 16} bước khác.");
            return new(true, sb.ToString().TrimEnd(), "quy_trinh_ho_so", uniq.Count,
                (laBt ? "BT#" : "SC#") + ma);
        }

        /// <summary>
        /// Không chỉ định mã: tổng hợp hồ sơ đã hoàn thành nhưng vẫn trả đúng ý định đếm/lịch.
        /// </summary>
        private async Task<KetQuaXuLy> TraLoiQuyTrinhHoanThanhAsync(
            int maNd, string vt, string q, CancellationToken ct)
        {
            var (hoiBt, hoiSc) = LoaiHoSoTrongCau(q);
            var bts = await _db.HoSoBaoTris.AsNoTracking()
                .Where(h => h.TrangThai == "Đã hoàn thành" || h.TrangThai == "Hoàn thành")
                .Select(h => new
                {
                    h.MaHoSoBaoTri,
                    Ten = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "",
                    h.TrangThai
                }).ToListAsync(ct);
            var scs = await _db.HoSoSuaChuas.AsNoTracking()
                .Where(h => h.TrangThai == "Đã hoàn thành" || h.TrangThai == "Hoàn thành")
                .Select(h => new
                {
                    h.MaHoSoSuaChua,
                    Ten = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "",
                    h.TrangThai
                }).ToListAsync(ct);

            HashSet<int>? btScope = null;
            HashSet<int>? scScope = null;
            if (vt == "Nhân viên kỹ thuật")
            {
                var maNv = await _db.NhanViens.AsNoTracking().Where(n => n.MaNguoiDung == maNd)
                    .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
                if (maNv == null)
                    return new(true, "Mình chưa xác định được tài khoản nhân viên của bạn.", "quy_trinh_ho_so", 0);
                var pcs = await _db.PhanCongCongViecs.AsNoTracking()
                    .Where(p => p.MaNhanVienThucHien == maNv && p.TrangThai != "Đã hủy")
                    .Select(p => new
                    {
                        MaBt = p.MaHoSoBaoTri ?? (p.HoSoBaoTri != null ? (int?)p.HoSoBaoTri.MaHoSoBaoTri : null),
                        MaSc = p.MaHoSoSuaChua ?? (p.HoSoSuaChua != null ? (int?)p.HoSoSuaChua.MaHoSoSuaChua : null)
                    }).ToListAsync(ct);
                btScope = pcs.Where(x => x.MaBt != null).Select(x => x.MaBt!.Value).ToHashSet();
                scScope = pcs.Where(x => x.MaSc != null).Select(x => x.MaSc!.Value).ToHashSet();
                bts = bts.Where(x => btScope.Contains(x.MaHoSoBaoTri)).ToList();
                scs = scs.Where(x => scScope.Contains(x.MaHoSoSuaChua)).ToList();
            }

            if (!hoiBt) bts.Clear();
            if (!hoiSc) scs.Clear();
            if (!hoiBt && !hoiSc)
            {
                // Câu nói chung “hồ sơ bảo trì hoặc sửa chữa”: giữ cả hai loại.
                bts = await _db.HoSoBaoTris.AsNoTracking()
                    .Where(h => h.TrangThai == "Đã hoàn thành" || h.TrangThai == "Hoàn thành")
                    .Select(h => new { h.MaHoSoBaoTri, Ten = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "", h.TrangThai })
                    .ToListAsync(ct);
                scs = await _db.HoSoSuaChuas.AsNoTracking()
                    .Where(h => h.TrangThai == "Đã hoàn thành" || h.TrangThai == "Hoàn thành")
                    .Select(h => new { h.MaHoSoSuaChua, Ten = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "", h.TrangThai })
                    .ToListAsync(ct);
                if (btScope != null) bts = bts.Where(x => btScope.Contains(x.MaHoSoBaoTri)).ToList();
                if (scScope != null) scs = scs.Where(x => scScope.Contains(x.MaHoSoSuaChua)).ToList();
            }

            var idsBt = bts.Select(x => x.MaHoSoBaoTri).ToList();
            var idsSc = scs.Select(x => x.MaHoSoSuaChua).ToList();
            var buoc = await _db.TienDoBuocQuyTrinhs.AsNoTracking()
                .Where(t => (t.MaHoSoBaoTri != null && idsBt.Contains(t.MaHoSoBaoTri.Value))
                            || (t.MaHoSoSuaChua != null && idsSc.Contains(t.MaHoSoSuaChua.Value)))
                .Select(t => new { t.MaTienDo, t.MaHoSoBaoTri, t.MaHoSoSuaChua, t.SoBuoc, t.MaNhanVien, t.TrangThai })
                .ToListAsync(ct);

            var dong = new List<(string Ma, string Ten, int Tong, int Nv, int Xong)>();
            foreach (var h in bts)
            {
                var g = buoc.Where(x => x.MaHoSoBaoTri == h.MaHoSoBaoTri)
                    .GroupBy(x => x.SoBuoc).Select(x => x.OrderByDescending(y => y.MaTienDo).First()).ToList();
                dong.Add(("BT#" + h.MaHoSoBaoTri, h.Ten, g.Count,
                    g.Count(x => x.MaNhanVien > 0), g.Count(x => x.MaNhanVien > 0 && LaBuocXong(x.TrangThai))));
            }
            foreach (var h in scs)
            {
                var g = buoc.Where(x => x.MaHoSoSuaChua == h.MaHoSoSuaChua)
                    .GroupBy(x => x.SoBuoc).Select(x => x.OrderByDescending(y => y.MaTienDo).First()).ToList();
                dong.Add(("SC#" + h.MaHoSoSuaChua, h.Ten, g.Count,
                    g.Count(x => x.MaNhanVien > 0), g.Count(x => x.MaNhanVien > 0 && LaBuocXong(x.TrangThai))));
            }

            var lietKe = LaYeuCauLietKe(q);
            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, có **{dong.Count}** hồ sơ đã hoàn thành"
                          + (btScope != null ? " trong phạm vi được phân công cho bạn" : "") + "."
                          + $" Tổng **{dong.Sum(x => x.Nv)}** bước do NVKT nhận, trong đó **{dong.Sum(x => x.Xong)}** bước đã xong.");
            if (lietKe)
                foreach (var x in dong.OrderByDescending(x => x.Nv).ThenBy(x => x.Ma).Take(8))
                    sb.AppendLine($"• {x.Ma} | {x.Ten} | {x.Nv}/{x.Tong} bước NVKT nhận, {x.Xong} đã xong.");
            if (dong.Count > 8 && lietKe) sb.AppendLine($"… còn {dong.Count - 8} hồ sơ khác.");
            if (dong.Count == 0) sb.Append("Chưa có hồ sơ hoàn thành nào.");
            else if (dong.Sum(x => x.Nv) == 0) sb.Append("Các hồ sơ này chưa có bước nào do NVKT nhận.");
            sb.Append("Gửi mã BT#/SC# để xem chi tiết từng bước và nhân viên phụ trách.");
            return new(true, sb.ToString().TrimEnd(), "quy_trinh_ho_so", dong.Count);
        }
    }
}
