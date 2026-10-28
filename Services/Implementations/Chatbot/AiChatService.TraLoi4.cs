using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class AiChatService
    {
        // ===================== LỌC HỒ SƠ THEO TRẠNG THÁI / NGÀY =====================

        /// <summary>
        /// Khoảng thời gian người dùng hỏi. Không tự gán tháng/năm khi họ không nói,
        /// để câu «ngày 27» hoặc «27/10» vẫn tìm được trong mọi tháng/năm có dữ liệu.
        /// </summary>
        private sealed record KhoangNgay(
            DateOnly? NgayChinhXac,
            DateOnly? Tu,
            DateOnly? Den,
            int? Ngay,
            int? Thang,
            int? Nam,
            string MoTa)
        {
            public bool CoNgay =>
                NgayChinhXac != null || Tu != null || Ngay != null || Thang != null || Nam != null;

            public bool LaNgayCuThe => NgayChinhXac != null || Ngay != null;
        }

        private static readonly KhoangNgay KhongKhoangNgay =
            new(null, null, null, null, null, null, "");

        private sealed record MocNgay(DateOnly Ngay, string Nhan);

        private sealed record HoSoLoc(
            string Ma,
            string TenTb,
            string TrangThai,
            List<MocNgay> Khop,
            DateOnly? NgayHienThi);

        private sealed record NhomTrangThai(string Ma, string Ten, string[] Bt, string[] Sc);

        private static readonly string[] TtChoDuyet =
        {
            "Chờ duyệt", "Chờ xưởng", "Chờ gửi", "Chờ GĐ duyệt",
            "Chờ giám đốc duyệt", "Chờ Xưởng"
        };

        private static readonly string[] TtDaDuyet =
        {
            "Đã duyệt", "Đã phê duyệt"
        };

        private static readonly string[] TtChoPhanCongBt =
        {
            "Đã duyệt", "Chờ phân công", "Đã phê duyệt"
        };

        private static readonly string[] TtChoPhanCongSc =
        {
            "Chờ phân công"
        };

        private static readonly string[] TtDangThucHien =
        {
            "Đang thực hiện", "Chờ xác nhận", "Đã phân công", "Chờ xác nhận kết quả"
        };

        private static readonly string[] TtHoanThanh =
        {
            "Đã hoàn thành", "Hoàn thành"
        };

        private static readonly string[] TtTuChoi =
        {
            "Từ chối", "Đã từ chối"
        };

        private static readonly string[] TtNhap = { "Nháp" };

        private static readonly string[] TtDaHuy = { "Đã hủy", "Đã huỷ", "Hủy" };

        /// <summary>Các trạng thái còn nằm trong luồng xử lý (dùng cho «chưa hoàn thành»).</summary>
        private static readonly string[] TtDangXuLy = TtChoDuyet
            .Concat(TtDaDuyet)
            .Concat(TtChoPhanCongBt)
            .Concat(TtDangThucHien)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        private static readonly NhomTrangThai NhomChoDuyet =
            new("cho_duyet", "Chờ duyệt", TtChoDuyet, TtChoDuyet);

        // Hỏi đích danh một trạng thái con thì trả đúng trạng thái đó,
        // không gộp cả nhóm tab «Chờ duyệt» của app.
        private static readonly NhomTrangThai NhomChoXuong =
            new("cho_xuong", "Chờ xưởng", new[] { "Chờ xưởng", "Chờ Xưởng" }, new[] { "Chờ xưởng", "Chờ Xưởng" });

        private static readonly NhomTrangThai NhomChoGui =
            new("cho_gui", "Chờ gửi", new[] { "Chờ gửi" }, new[] { "Chờ gửi" });

        private static readonly NhomTrangThai NhomChoGdDuyet =
            new("cho_gd_duyet", "Chờ GĐ duyệt",
                new[] { "Chờ GĐ duyệt", "Chờ giám đốc duyệt" },
                new[] { "Chờ GĐ duyệt", "Chờ giám đốc duyệt" });

        private static readonly NhomTrangThai NhomChoXacNhan =
            new("cho_xac_nhan", "Chờ xác nhận",
                new[] { "Chờ xác nhận", "Chờ xác nhận kết quả" },
                new[] { "Chờ xác nhận", "Chờ xác nhận kết quả" });

        private static readonly NhomTrangThai NhomDaDuyet =
            new("da_duyet", "Đã duyệt", TtDaDuyet, TtDaDuyet);

        private static readonly NhomTrangThai NhomChoPhanCong =
            new("cho_phan_cong", "Chờ phân công", TtChoPhanCongBt, TtChoPhanCongSc);

        private static readonly NhomTrangThai NhomDangThucHien =
            new("dang_thuc_hien", "Đang thực hiện", TtDangThucHien, TtDangThucHien);

        private static readonly NhomTrangThai NhomHoanThanh =
            new("hoan_thanh", "Đã hoàn thành", TtHoanThanh, TtHoanThanh);

        private static readonly NhomTrangThai NhomTuChoi =
            new("tu_choi", "Từ chối", TtTuChoi, TtTuChoi);

        private static readonly NhomTrangThai NhomNhap =
            new("nhap", "Nháp", TtNhap, TtNhap);

        private static readonly NhomTrangThai NhomDaHuy =
            new("da_huy", "Đã hủy", TtDaHuy, TtDaHuy);

        private static readonly NhomTrangThai NhomChuaHoanThanh =
            new("chua_hoan_thanh", "chưa hoàn thành", TtDangXuLy, TtDangXuLy);

        // ===================== NHẬN DIỆN CÂU HỎI =====================

        private static bool CoTu(string q, params string[] keys) =>
            keys.Any(k => Regex.IsMatch(q,
                @"(?<![\p{L}\p{N}])" + Regex.Escape(k) + @"(?![\p{L}\p{N}])"));

        private static NhomTrangThai? NhomTrangThaiCua(string q)
        {
            if (Co(q, "da huy", "bi huy", "huy ho so")) return NhomDaHuy;
            if (Co(q, "nhap")) return NhomNhap;
            if (Co(q, "tu choi", "bi tu choi", "khong duyet")) return NhomTuChoi;
            if (Co(q, "cho phan cong")) return NhomChoPhanCong;
            if (Co(q, "chua hoan thanh", "chua xong")) return NhomChuaHoanThanh;
            if (Co(q, "cho xuong")) return NhomChoXuong;
            if (Co(q, "cho gui")) return NhomChoGui;
            if (Co(q, "cho giam doc duyet", "cho gd duyet")) return NhomChoGdDuyet;
            if (Co(q, "cho xac nhan")) return NhomChoXacNhan;
            if (Co(q, "chua duyet", "chua duoc duyet", "chua phe duyet")) return NhomChoDuyet;
            if (Co(q, "cho duyet", "da gui", "gui di", "da trinh")) return NhomChoDuyet;
            if (Co(q, "dang thuc hien", "dang lam", "dang sua", "dang bao tri"))
                return NhomDangThucHien;
            if (Co(q, "hoan thanh", "da xong")) return NhomHoanThanh;
            if (Co(q, "da duyet", "duoc duyet", "phe duyet")) return NhomDaDuyet;
            return null;
        }

        private static bool LaHoiTheoTrangThaiHoSo(string q, NhomTrangThai? nhom)
        {
            if (nhom == null) return false;

            // «ở trạng thái …» luôn là hỏi trạng thái hồ sơ hiện tại.
            if (Co(q, "trang thai")) return true;

            // Các câu này thuộc lịch sử duyệt/chốt ngày, không phải trạng thái hồ sơ.
            if (Co(q, "giam doc", "lich su", "chot ngay", "len lich", "xep lich"))
                return false;

            return Co(q, "ho so", "bao tri", "sua chua", "hs bt", "hs sc",
                       "nao", "may", "bao nhieu")
                   || CoTu(q, "hs", "bt", "sc");
        }

        private static bool LaHoiTheoNgayHoSo(string q, KhoangNgay khoang)
        {
            if (!khoang.CoNgay) return false;

            // Câu về thiết bị không phải hồ sơ.
            if (Co(q, "thiet bi", "may moc")
                && !Co(q, "ho so", "sua chua", "lich bao tri", "ke hoach bao tri"))
                return false;

            // Duyệt/chốt ngày có luồng trả lời riêng.
            if (Co(q, "giam doc", "chot ngay", "len lich", "xep lich")) return false;

            // Lịch sử phân công có luồng trả lời riêng.
            if (Co(q, "lich su") && !Co(q, "ho so", "bao tri", "sua chua")) return false;

            if (Co(q, "ho so", "bao tri", "sua chua", "lich", "ke hoach", "du kien",
                    "phan cong", "thuc hien", "hoan thanh"))
                return true;

            return khoang.LaNgayCuThe
                   && Co(q, "co gi", "co khong", "nao khong", "nhung gi");
        }

        private static (bool Bt, bool Sc) LoaiHoSoTrongCau(string q)
        {
            var bt = Co(q, "bao tri") || CoTu(q, "bt");
            var sc = Co(q, "sua chua") || CoTu(q, "sc");
            if (bt && !sc) return (true, false);
            if (sc && !bt) return (false, true);
            return (true, true);
        }

        private static bool UuTienNgayKeHoach(string q) =>
            !Co(q, "lich su")
            && Co(q, "lich bao tri", "ke hoach", "du kien", "ngay bao tri", "lich");

        // ===================== PHÂN TÍCH NGÀY =====================

        private static KhoangNgay TrichKhoangNgay(string q)
        {
            var now = DateTime.Today;

            if (Co(q, "hom nay")) return NgayChinhXac(now, "hôm nay");
            if (Co(q, "hom qua")) return NgayChinhXac(now.AddDays(-1), "hôm qua");
            if (Co(q, "ngay mai")) return NgayChinhXac(now.AddDays(1), "ngày mai");

            if (Co(q, "tuan nay"))
            {
                var dau = now.AddDays(-(((int)now.DayOfWeek + 6) % 7));
                return Tuan(dau, "tuần này");
            }
            if (Co(q, "tuan truoc"))
            {
                var dau = now.AddDays(-(((int)now.DayOfWeek + 6) % 7) - 7);
                return Tuan(dau, "tuần trước");
            }

            // «ngày 27 trong tháng này / tháng trước» — phải giữ cả ngày lẫn tháng.
            var mRel = Regex.Match(q,
                @"ngay\s*(\d{1,2})\s*(?:(?:trong|cua)\s*)?thang\s*(nay|truoc)\b");
            if (mRel.Success
                && int.TryParse(mRel.Groups[1].Value, out var dRel)
                && dRel is >= 1 and <= 31)
            {
                var moc = string.Equals(mRel.Groups[2].Value, "truoc", StringComparison.Ordinal)
                    ? now.AddMonths(-1)
                    : now;
                return new KhoangNgay(null, null, null, dRel, moc.Month, moc.Year,
                    $"ngày {dRel:00}/{moc.Month:00}/{moc.Year}");
            }

            if (Co(q, "thang nay"))
                return TheoThang(now.Month, now.Year, $"tháng này ({now.Month}/{now.Year})");
            if (Co(q, "thang truoc"))
            {
                var t = now.AddMonths(-1);
                return TheoThang(t.Month, t.Year, $"tháng trước ({t.Month}/{t.Year})");
            }

            if (Co(q, "nam nay")) return TheoNam(now.Year, $"năm nay ({now.Year})");
            if (Co(q, "nam truoc")) return TheoNam(now.Year - 1, $"năm trước ({now.Year - 1})");

            // 27/10/2026
            var m = Regex.Match(q, @"\b(\d{1,2})\s*[/\-.]\s*(\d{1,2})\s*[/\-.]\s*(20\d{2})\b");
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out var d1)
                && int.TryParse(m.Groups[2].Value, out var mo1)
                && int.TryParse(m.Groups[3].Value, out var y1)
                && d1 is >= 1 and <= 31 && mo1 is >= 1 and <= 12
                && DateOnly.TryParseExact($"{d1:00}/{mo1:00}/{y1}", "dd/MM/yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var exact))
            {
                return new KhoangNgay(exact, null, null, d1, mo1, y1,
                    $"ngày {exact:dd/MM/yyyy}");
            }

            // 10/2026
            m = Regex.Match(q, @"\b(\d{1,2})\s*[/\-.]\s*(20\d{2})\b");
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out var mo2)
                && int.TryParse(m.Groups[2].Value, out var y2)
                && mo2 is >= 1 and <= 12)
                return TheoThang(mo2, y2, $"tháng {mo2}/{y2}");

            // 27/10
            m = Regex.Match(q, @"\b(\d{1,2})\s*[/\-.]\s*(\d{1,2})\b");
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out var d3)
                && int.TryParse(m.Groups[2].Value, out var mo3)
                && d3 is >= 1 and <= 31 && mo3 is >= 1 and <= 12)
            {
                return new KhoangNgay(null, null, null, d3, mo3, null,
                    $"ngày {d3:00}/{mo3:00}");
            }

            // ngày 27 tháng 10 [năm 2026] / 27 tháng 10
            m = Regex.Match(q,
                @"(?:ngay\s*)?(\d{1,2})\s*thang\s*(\d{1,2})(?:\s*nam\s*(20\d{2}))?");
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out var d4)
                && int.TryParse(m.Groups[2].Value, out var mo4)
                && d4 is >= 1 and <= 31 && mo4 is >= 1 and <= 12)
            {
                int? y4 = m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out var yy)
                    ? yy
                    : null;
                var moTa = y4 != null
                    ? $"ngày {d4:00}/{mo4:00}/{y4}"
                    : $"ngày {d4:00}/{mo4:00}";
                return new KhoangNgay(null, null, null, d4, mo4, y4, moTa);
            }

            // tháng 10 [năm 2026]
            m = Regex.Match(q, @"thang\s*(\d{1,2})(?:\s*(?:nam\s*)?(20\d{2}))?");
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out var mo5)
                && mo5 is >= 1 and <= 12)
            {
                int? y5 = m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var yy5)
                    ? yy5
                    : null;
                return TheoThang(mo5, y5,
                    y5 != null ? $"tháng {mo5}/{y5}" : $"tháng {mo5}");
            }

            // ngày 27 (không nói tháng → tìm trong mọi tháng)
            m = Regex.Match(q, @"ngay\s*(\d{1,2})\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var d6) && d6 is >= 1 and <= 31)
                return new KhoangNgay(null, null, null, d6, null, null, $"ngày {d6}");

            // năm 2026
            m = Regex.Match(q, @"(?:nam\s*)?(20\d{2})\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var y7))
                return TheoNam(y7, $"năm {y7}");

            return KhongKhoangNgay;
        }

        private static KhoangNgay NgayChinhXac(DateTime d, string ten)
        {
            var ngay = DateOnly.FromDateTime(d);
            return new KhoangNgay(ngay, null, null, ngay.Day, ngay.Month, ngay.Year,
                $"{ten} ({ngay:dd/MM/yyyy})");
        }

        private static KhoangNgay Tuan(DateTime dau, string ten)
        {
            var tu = DateOnly.FromDateTime(dau);
            var den = DateOnly.FromDateTime(dau.AddDays(6));
            return new KhoangNgay(null, tu, den, null, null, null,
                $"{ten} ({tu:dd/MM} – {den:dd/MM/yyyy})");
        }

        private static KhoangNgay TheoThang(int thang, int? nam, string moTa) =>
            new(null, null, null, null, thang, nam, moTa);

        private static KhoangNgay TheoNam(int nam, string moTa) =>
            new(null, null, null, null, null, nam, moTa);

        private static bool NgayKhop(DateOnly d, KhoangNgay k)
        {
            if (k.NgayChinhXac is { } chinhXac) return d == chinhXac;
            if (k.Tu is { } tu && k.Den is { } den) return d >= tu && d <= den;

            if (k.Ngay != null)
            {
                if (d.Day != k.Ngay) return false;
                if (k.Thang != null && d.Month != k.Thang) return false;
                if (k.Nam != null && d.Year != k.Nam) return false;
                return true;
            }

            if (k.Thang != null)
            {
                if (d.Month != k.Thang) return false;
                if (k.Nam != null && d.Year != k.Nam) return false;
                return true;
            }

            if (k.Nam != null) return d.Year == k.Nam;
            return true;
        }

        private static List<MocNgay> TaoMoc(
            DateTime ngayTao,
            DateTime? ngayDuyet,
            DateTime? batDau,
            DateTime? ketThuc,
            DateOnly? keHoach)
        {
            var ds = new List<MocNgay>();
            if (keHoach != null) ds.Add(new MocNgay(keHoach.Value, "kế hoạch"));
            if (ngayTao.Year > 1900) ds.Add(new MocNgay(DateOnly.FromDateTime(ngayTao), "ngày tạo"));
            if (ngayDuyet != null && ngayDuyet.Value.Year > 1900)
                ds.Add(new MocNgay(DateOnly.FromDateTime(ngayDuyet.Value), "duyệt"));
            if (batDau != null && batDau.Value.Year > 1900)
                ds.Add(new MocNgay(DateOnly.FromDateTime(batDau.Value), "bắt đầu"));
            if (ketThuc != null && ketThuc.Value.Year > 1900)
                ds.Add(new MocNgay(DateOnly.FromDateTime(ketThuc.Value), "hoàn thành"));
            return ds;
        }

        private static MocNgay? ChonMoc(List<MocNgay> khop, bool uuTienKeHoach, string? nhomMa)
        {
            if (khop.Count == 0) return null;

            string[] uuTien;
            if (uuTienKeHoach)
            {
                uuTien = new[] { "kế hoạch", "hoàn thành", "bắt đầu", "duyệt", "ngày tạo" };
            }
            else if (nhomMa == "hoan_thanh")
            {
                // Hỏi «đã hoàn thành» thì mốc hoàn thành là mốc đáng hiện nhất.
                uuTien = new[] { "hoàn thành", "kế hoạch", "bắt đầu", "duyệt", "ngày tạo" };
            }
            else if (nhomMa is "cho_duyet" or "cho_xuong" or "cho_gui" or "cho_gd_duyet")
            {
                uuTien = new[] { "ngày tạo", "duyệt", "kế hoạch", "bắt đầu", "hoàn thành" };
            }
            else
            {
                uuTien = new[] { "kế hoạch", "hoàn thành", "bắt đầu", "duyệt", "ngày tạo" };
            }

            foreach (var nhan in uuTien)
            {
                var hit = khop.FirstOrDefault(x => x.Nhan == nhan);
                if (hit != null) return hit;
            }
            return khop[0];
        }

        private static DateOnly? NgayDaiDien(List<MocNgay> mocs, string? nhomMa)
        {
            var uuTien = nhomMa switch
            {
                "hoan_thanh" => new[] { "hoàn thành", "kế hoạch", "bắt đầu", "duyệt", "ngày tạo" },
                "cho_duyet" or "cho_xuong" or "cho_gui" or "cho_gd_duyet"
                    => new[] { "ngày tạo", "duyệt", "kế hoạch", "bắt đầu", "hoàn thành" },
                "dang_thuc_hien" or "cho_xac_nhan"
                    => new[] { "bắt đầu", "ngày tạo", "kế hoạch", "duyệt", "hoàn thành" },
                _ => new[] { "kế hoạch", "hoàn thành", "bắt đầu", "duyệt", "ngày tạo" }
            };

            foreach (var nhan in uuTien)
            {
                var hit = mocs.FirstOrDefault(x => x.Nhan == nhan);
                if (hit != null) return hit.Ngay;
            }
            return null;
        }

        private static bool TrangThaiKhop(NhomTrangThai? nhom, string? trangThai, bool laBt)
        {
            var tt = (trangThai ?? "").Trim();
            if (nhom == null) return !LaTrangThaiAnTrenApp(tt);
            var ds = laBt ? nhom.Bt : nhom.Sc;
            return ds.Any(x => string.Equals(x, tt, StringComparison.OrdinalIgnoreCase));
        }

        // ===================== TRẢ LỜI =====================

        private async Task<KetQuaXuLy> TraLoiHoSoTheoBoLocAsync(
            int maNd,
            string vt,
            string q,
            KhoangNgay khoang,
            NhomTrangThai? nhom,
            CancellationToken ct)
        {
            var (hoiBt, hoiSc) = LoaiHoSoTrongCau(q);
            var uuTienKeHoach = UuTienNgayKeHoach(q);
            var laNvkt = vt == "Nhân viên kỹ thuật";
            var chiDem = Co(q, "bao nhieu", "co may", "tong so", "tong cong", "so luong", "dem")
                         && !Co(q, "liet ke", "danh sach", "nhung", "nao", "ten", "gom");

            HashSet<int>? btScope = null;
            HashSet<int>? scScope = null;

            if (laNvkt)
            {
                var maNv = await _db.NhanViens.AsNoTracking()
                    .Where(n => n.MaNguoiDung == maNd)
                    .Select(n => (int?)n.MaNhanVien)
                    .FirstOrDefaultAsync(ct);
                if (maNv == null)
                    return new KetQuaXuLy(true,
                        "Dạ, mình chưa xác định được tài khoản nhân viên của bạn.", "ho_so_theo_bo_loc", 0);

                var pcCuaToi = await _db.PhanCongCongViecs.AsNoTracking()
                    .Where(p => p.MaNhanVienThucHien == maNv && p.TrangThai != "Đã hủy")
                    .Select(p => new
                    {
                        MaHoSoBaoTri = p.MaHoSoBaoTri ?? (p.HoSoBaoTri != null ? (int?)p.HoSoBaoTri.MaHoSoBaoTri : null),
                        MaHoSoSuaChua = p.MaHoSoSuaChua ?? (p.HoSoSuaChua != null ? (int?)p.HoSoSuaChua.MaHoSoSuaChua : null)
                    })
                    .ToListAsync(ct);

                btScope = pcCuaToi.Where(x => x.MaHoSoBaoTri != null)
                    .Select(x => x.MaHoSoBaoTri!.Value).ToHashSet();
                scScope = pcCuaToi.Where(x => x.MaHoSoSuaChua != null)
                    .Select(x => x.MaHoSoSuaChua!.Value).ToHashSet();
            }

            // Hồ sơ «chờ phân công» = đúng trạng thái VÀ chưa có phân công còn hiệu lực.
            HashSet<int> daPhanCongBt = new();
            HashSet<int> daPhanCongSc = new();
            if (nhom?.Ma == "cho_phan_cong")
            {
                // Đọc thêm inverse legacy như endpoint app, không đổi tiêu chí loại PC.
                var pcs = await _db.PhanCongCongViecs.AsNoTracking()
                    .Select(p => new
                    {
                        MaHoSoBaoTri = p.MaHoSoBaoTri ?? (p.HoSoBaoTri != null ? (int?)p.HoSoBaoTri.MaHoSoBaoTri : null),
                        MaHoSoSuaChua = p.MaHoSoSuaChua ?? (p.HoSoSuaChua != null ? (int?)p.HoSoSuaChua.MaHoSoSuaChua : null),
                        p.TrangThai
                    })
                    .ToListAsync(ct);
                foreach (var p in pcs)
                {
                    var tt = (p.TrangThai ?? "").Trim();
                    if (LaHuy(tt) || string.Equals(tt, "Từ chối", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (p.MaHoSoBaoTri != null) daPhanCongBt.Add(p.MaHoSoBaoTri.Value);
                    if (p.MaHoSoSuaChua != null) daPhanCongSc.Add(p.MaHoSoSuaChua.Value);
                }
            }

            var plans = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Select(c => new
                {
                    c.MaHoSoBaoTri,
                    c.NgayDuKienBaoTri,
                    Ten = c.MaThietBiNavigation != null
                        ? c.MaThietBiNavigation.TenThietBi
                        : ("TB#" + c.MaThietBi)
                })
                .ToListAsync(ct);

            var dictPlan = plans
                .Where(x => x.MaHoSoBaoTri != null)
                .GroupBy(x => x.MaHoSoBaoTri!.Value)
                .ToDictionary(g => g.Key, g => g.Min(x => x.NgayDuKienBaoTri));

            // Lịch kế hoạch chưa gắn hồ sơ — vẫn phải hiện khi hỏi lịch một ngày cụ thể.
            var lichChuaGan = uuTienKeHoach && hoiBt && khoang.CoNgay
                ? plans.Where(x => x.MaHoSoBaoTri == null && NgayKhop(x.NgayDuKienBaoTri, khoang))
                    .OrderBy(x => x.NgayDuKienBaoTri)
                    .Select(x => (x.NgayDuKienBaoTri, x.Ten))
                    .ToList()
                : new List<(DateOnly NgayDuKienBaoTri, string Ten)>();

            var btList = new List<HoSoLoc>();
            var scList = new List<HoSoLoc>();

            if (hoiBt)
            {
                var bts = await _db.HoSoBaoTris.AsNoTracking()
                    .Select(h => new
                    {
                        h.MaHoSoBaoTri,
                        h.TrangThai,
                        h.NgayTao,
                        h.NgayDuyet,
                        h.ThoiDiemBatDauThucTe,
                        h.ThoiDiemKetThucThucTe,
                        TenTb = h.MaThieBiNavigation != null
                            ? h.MaThieBiNavigation.TenThietBi
                            : ""
                    })
                    .ToListAsync(ct);

                foreach (var h in bts)
                {
                    if (btScope != null && !btScope.Contains(h.MaHoSoBaoTri)) continue;
                    if (!TrangThaiKhop(nhom, h.TrangThai, true)) continue;
                    if (nhom?.Ma == "cho_phan_cong" && daPhanCongBt.Contains(h.MaHoSoBaoTri)) continue;

                    dictPlan.TryGetValue(h.MaHoSoBaoTri, out var keHoach);
                    var mocs = TaoMoc(h.NgayTao, h.NgayDuyet,
                        h.ThoiDiemBatDauThucTe, h.ThoiDiemKetThucThucTe,
                        keHoach == default ? null : keHoach);

                    var khop = new List<MocNgay>();
                    if (khoang.CoNgay)
                    {
                        khop = mocs.Where(x => NgayKhop(x.Ngay, khoang)).ToList();
                        if (uuTienKeHoach)
                            khop = khop.Where(x => x.Nhan == "kế hoạch").ToList();
                        if (khop.Count == 0) continue;
                    }

                    btList.Add(new HoSoLoc("BT#" + h.MaHoSoBaoTri, h.TenTb, h.TrangThai,
                        khop, NgayDaiDien(mocs, nhom?.Ma)));
                }
            }

            if (hoiSc)
            {
                var scs = await _db.HoSoSuaChuas.AsNoTracking()
                    .Select(h => new
                    {
                        h.MaHoSoSuaChua,
                        h.TrangThai,
                        h.NgayTao,
                        h.NgayDuyet,
                        h.ThoiDiemBatDauThucTe,
                        h.ThoiDiemKetThucThucTe,
                        TenTb = h.MaThieBiNavigation != null
                            ? h.MaThieBiNavigation.TenThietBi
                            : ""
                    })
                    .ToListAsync(ct);

                foreach (var h in scs)
                {
                    if (scScope != null && !scScope.Contains(h.MaHoSoSuaChua)) continue;
                    if (!TrangThaiKhop(nhom, h.TrangThai, false)) continue;
                    if (nhom?.Ma == "cho_phan_cong" && daPhanCongSc.Contains(h.MaHoSoSuaChua)) continue;

                    var mocs = TaoMoc(h.NgayTao, h.NgayDuyet,
                        h.ThoiDiemBatDauThucTe, h.ThoiDiemKetThucThucTe, null);

                    var khop = new List<MocNgay>();
                    if (khoang.CoNgay)
                    {
                        khop = mocs.Where(x => NgayKhop(x.Ngay, khoang)).ToList();
                        if (uuTienKeHoach) khop = new List<MocNgay>();
                        if (khop.Count == 0) continue;
                    }

                    scList.Add(new HoSoLoc("SC#" + h.MaHoSoSuaChua, h.TenTb, h.TrangThai,
                        khop, NgayDaiDien(mocs, nhom?.Ma)));
                }
            }

            btList = btList.OrderByDescending(x => x.NgayHienThi).ThenBy(x => x.Ma).ToList();
            scList = scList.OrderByDescending(x => x.NgayHienThi).ThenBy(x => x.Ma).ToList();

            var tong = btList.Count + scList.Count;
            var sb = new StringBuilder();

            if (tong == 0)
            {
                sb.Append("Dạ, ");
                sb.Append(khoang.CoNgay ? khoang.MoTa + " " : "hiện ");
                sb.Append("**không có** ");
                sb.Append(hoiBt && hoiSc
                    ? "hồ sơ bảo trì hay sửa chữa nào"
                    : hoiBt
                        ? "hồ sơ bảo trì nào"
                        : "hồ sơ sửa chữa nào");
                if (nhom != null) sb.Append($" ở trạng thái **{nhom.Ten}**");
                sb.Append('.');

                if (khoang.CoNgay)
                {
                    sb.AppendLine();
                    sb.Append("Mình đã đối chiếu ngày tạo, ngày duyệt, ngày bắt đầu, ngày hoàn thành và ngày kế hoạch.");
                }

                if (lichChuaGan.Count > 0)
                {
                    sb.AppendLine();
                    sb.Append($"Có **{lichChuaGan.Count}** lịch kế hoạch chưa gắn hồ sơ: "
                              + string.Join(" · ", lichChuaGan.Take(6)
                                  .Select(x => $"{x.Ten} ({x.NgayDuKienBaoTri:dd/MM/yyyy})"))
                              + (lichChuaGan.Count > 6 ? $" … còn {lichChuaGan.Count - 6}." : "."));
                }

                if (laNvkt)
                {
                    sb.AppendLine();
                    sb.Append("Mình chỉ tính các hồ sơ được phân công cho bạn.");
                }

                return new KetQuaXuLy(true, sb.ToString().TrimEnd(),
                    khoang.CoNgay ? "ho_so_theo_ngay" : "ho_so_theo_trang_thai", 0);
            }

            sb.Append("Dạ, ");
            sb.Append(khoang.CoNgay ? khoang.MoTa + " " : "hiện ");

            var phan = new List<string>();
            if (hoiBt) phan.Add(MoTaDem(btList.Count, "hồ sơ bảo trì", nhom, uuTienKeHoach));
            if (hoiSc) phan.Add(MoTaDem(scList.Count, "hồ sơ sửa chữa", nhom, false));
            sb.Append(string.Join("; ", phan));
            sb.Append('.');

            if (khoang.CoNgay && nhom == null && hoiBt && !uuTienKeHoach)
            {
                var soKeHoach = btList.Count(x => x.Khop.Any(m => m.Nhan == "kế hoạch"));
                if (soKeHoach > 0)
                    sb.Append(khoang.LaNgayCuThe
                        ? $" Trùng ngày kế hoạch: **{soKeHoach}** hồ sơ bảo trì."
                        : $" Có ngày kế hoạch trong kỳ: **{soKeHoach}** hồ sơ bảo trì.");
            }

            if (khoang.CoNgay && nhom == null)
            {
                if (hoiBt && btList.Count > 0)
                {
                    sb.AppendLine();
                    sb.Append("BT theo trạng thái: " + string.Join(" · ",
                        btList.GroupBy(x => x.TrangThai).OrderBy(g => g.Key)
                            .Select(g => $"{g.Key} {g.Count()}")) + ".");
                }
                if (hoiSc && scList.Count > 0)
                {
                    sb.AppendLine();
                    sb.Append("SC theo trạng thái: " + string.Join(" · ",
                        scList.GroupBy(x => x.TrangThai).OrderBy(g => g.Key)
                            .Select(g => $"{g.Key} {g.Count()}")) + ".");
                }
            }

            var canLietKe = !chiDem;
            if (canLietKe)
            {
                var caHai = hoiBt && hoiSc && btList.Count > 0 && scList.Count > 0;

                if (hoiBt && btList.Count > 0)
                {
                    sb.AppendLine();
                    if (caHai) sb.AppendLine($"Hồ sơ bảo trì ({btList.Count}):");
                    foreach (var h in btList.Take(6))
                        sb.AppendLine($"• {h.Ma} | {h.TenTb} | {h.TrangThai} | {MoTaNgay(h, khoang, uuTienKeHoach, nhom?.Ma)}");
                    if (btList.Count > 6)
                        sb.AppendLine($"… còn {btList.Count - 6} hồ sơ bảo trì khác.");
                }

                if (hoiSc && scList.Count > 0)
                {
                    sb.AppendLine();
                    if (caHai) sb.AppendLine($"Hồ sơ sửa chữa ({scList.Count}):");
                    foreach (var h in scList.Take(6))
                        sb.AppendLine($"• {h.Ma} | {h.TenTb} | {h.TrangThai} | {MoTaNgay(h, khoang, false, nhom?.Ma)}");
                    if (scList.Count > 6)
                        sb.AppendLine($"… còn {scList.Count - 6} hồ sơ sửa chữa khác.");
                }
            }

            if (uuTienKeHoach && lichChuaGan.Count > 0)
            {
                sb.AppendLine();
                sb.Append($"Ngoài ra có **{lichChuaGan.Count}** lịch kế hoạch chưa gắn hồ sơ: "
                          + string.Join(" · ", lichChuaGan.Take(6)
                              .Select(x => $"{x.Ten} ({x.NgayDuKienBaoTri:dd/MM/yyyy})"))
                          + (lichChuaGan.Count > 6 ? $" … còn {lichChuaGan.Count - 6}." : "."));
            }

            if (laNvkt)
            {
                sb.AppendLine();
                sb.Append("Mình chỉ tính các hồ sơ được phân công cho bạn.");
            }

            return new KetQuaXuLy(true, sb.ToString().TrimEnd(),
                khoang.CoNgay ? "ho_so_theo_ngay" : "ho_so_theo_trang_thai", tong);
        }

        private static string MoTaDem(
            int so, string loai, NhomTrangThai? nhom, bool uuTienKeHoach)
        {
            if (so == 0)
                return $"**không có** {loai} nào"
                       + (nhom != null ? $" ở trạng thái **{nhom.Ten}**" : "");

            return $"có **{so}** {loai}"
                   + (nhom != null ? $" **{nhom.Ten}**" : "")
                   + (uuTienKeHoach ? " theo ngày kế hoạch" : "");
        }

        private static string MoTaNgay(HoSoLoc h, KhoangNgay khoang, bool uuTienKeHoach, string? nhomMa)
        {
            if (khoang.CoNgay && h.Khop.Count > 0)
            {
                var moc = ChonMoc(h.Khop, uuTienKeHoach, nhomMa);
                if (moc != null)
                    return $"{moc.Nhan} {moc.Ngay:dd/MM/yyyy}";
            }

            return h.NgayHienThi?.ToString("dd/MM/yyyy") ?? "—";
        }
    }
}
