using System.Text;
using Microsoft.EntityFrameworkCore;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class AiChatService
    {
        private sealed record KetQuaXuLy(
            bool DaXuLy,
            string? TraLoi,
            string ChuDe = "chung",
            int? SoLuong = null,
            string? MaHoSo = null);

        private sealed class HsTomTat
        {
            public string Ma { get; init; } = "";
            public string TenTb { get; init; } = "";
            public string TrangThai { get; init; } = "";
            public DateTime? Ngay { get; init; }
            public DateOnly? NgayKeHoach { get; init; }
            public bool TuChoiPhanCong { get; init; }
        }

        // ===================== ĐỊNH TUYẾN Ý ĐỊNH =====================

        private async Task<KetQuaXuLy> XuLyYDinhAsync(
            int maNd,
            string vt,
            string ten,
            string q,
            string cauGoc,
            CancellationToken ct,
            bool boQuaTriNho = false)
        {
            var (ngay, thang, nam) = TrichKy(q);
            var laNvkt = vt == "Nhân viên kỹ thuật";
            var maBt = TrichMa(q, "bt");
            var maSc = TrichMa(q, "sc");

            // Vật tư phải đứng trước "hồ sơ" để không nhầm hồ sơ vật tư với hồ sơ bảo trì.
            if (LaHoiVatTu(q))
                return await TraLoiVatTuAsync(maNd, vt, maBt, maSc, q, ct);

            var hoiPending = LaHoiChoPhanCong(q);
            var hoiTienDo = Co(q, "dang thuc hien", "dang lam", "chua xong", "tien do", "con bao nhieu buoc");

            // Câu nhiều vế: vừa chờ phân công vừa hỏi bước còn lại.
            if (hoiPending && hoiTienDo && Co(q, "buoc"))
                return await TraLoiChoPhanCongVaBuocAsync(vt, ngay, thang, nam, ct);

            // Ranh giới: duyệt của Giám đốc + chốt ngày của Xưởng trong cùng câu.
            if (LaHoiPheDuyetVaChotNgay(q))
                return await TraLoiPheDuyetChotNgayAsync(vt, thang, nam, ct);

            // Từ tổng quát → chi tiết (màn danh sách → màn chi tiết).
            if (LaHoiChiTietHoSo(q) && Co(q, "ho so", "bao tri", "sua chua"))
                return await TraLoiChiTietHoSoAsync(vt, ngay, thang, nam, ct);

            if (!laNvkt && hoiPending)
                return await TraLoiChoPhanCongAsync(vt, ngay, thang, nam, q, ct, boQuaTriNho);

            if (!laNvkt && LaHoiNhanVienDangPhanCong(q))
                return await TraLoiNhanVienDangPhanCongAsync(vt, q, ct);

            // Thiết bị: chỉ số / danh sách / một máy cụ thể.
            if (Co(q, "thiet bi", "may moc") && !Co(q, "ho so", "vat tu"))
                return await TraLoiThietBiAsync(q, ct);

            // Hoàn thành theo tháng (khớp thống kê app).
            if (Co(q, "hoan thanh") && (thang != null || Co(q, "thang nay", "thang truoc", "thang")))
                return await TraLoiDemHoanThanhAsync(q, thang, nam, vt, boQuaTriNho, ct);

            // Bước quy trình.
            if (Co(q, "buoc", "quy trinh")
                && (maBt != null || maSc != null
                    || Co(q, "con bao nhieu", "chua xong", "tien do", "dang thuc hien")))
                return await TraLoiBuocAsync(maNd, vt, maBt, maSc, q, ct);

            // Duyệt / từ chối (không kèm chốt ngày).
            if (!laNvkt && LaHoiPheDuyet(q))
                return await TraLoiPheDuyetAsync(vt, thang, nam, q, ct);

            if (!laNvkt && LaHoiLichBaoTri(q))
                return await TraLoiLichBaoTriAsync(ct);

            // Chỉ khi hỏi rõ lịch sử phân công mới liệt kê PC#.
            if (LaHoiLichSuPhanCong(q))
                return await TraLoiLichSuPhanCongAsync(vt, ct);

            if (!laNvkt && LaHoiHoSoBtSc(q))
                return await TraLoiHoSoBtScAsync(vt, ngay, thang, nam, q, ct);

            if (laNvkt && Co(q, "viec", "phan cong", "ho so", "dang lam", "buoc", "tien do"))
                return await TraLoiViecCuaToiAsync(maNd, q, ct);

            return new KetQuaXuLy(false, null);
        }

        // ===================== NHẬN DIỆN Ý ĐỊNH =====================

        private static bool LaHoiVatTu(string q) =>
            Co(q, "vat tu", "ho so vat tu", "hs vat tu", "nguyen vat lieu", "vat lieu")
            || (Co(q, "tong tien") && Co(q, "vat", "tien vat"));

        /// <summary>Hỏi danh sách/số lượng HỒ SƠ đang chờ Tổ trưởng phân công (không phải lịch sử PC).</summary>
        private static bool LaHoiChoPhanCong(string q)
        {
            if (Co(q, "cho phan cong", "cho to truong phan cong", "da duyet cho phan cong",
                    "hs cho phan cong", "ho so cho phan cong"))
                return true;

            if (Co(q, "da duyet") && Co(q, "phan cong")) return true;

            if (Co(q, "phan cong")
                && Co(q, "ho so bao tri", "hs bt", "ho so bt")
                && !Co(q, "lich su", "ai duoc", "nhan vien"))
                return true;

            return false;
        }

        private static bool LaHoiChiTietHoSo(string q) =>
            Co(q, "chi tiet", "mo ho so", "mo ra", "thay nhung thong tin", "gom nhung thong tin",
                "co nhung gi", "bao gom nhung gi", "man hinh", "danh sach co gi");

        private static bool LaHoiNhanVienDangPhanCong(string q)
        {
            if (Co(q, "nhan vien")
                && Co(q, "dang duoc phan cong", "duoc phan cong", "dang lam", "dang thuc hien",
                    "nhan viec", "duoc giao"))
                return true;
            if (Co(q, "co nhan vien nao") && Co(q, "bao tri", "sua chua"))
                return true;
            if (Co(q, "phan cong them", "cach phan cong", "lam the nao de phan cong"))
                return true;
            return false;
        }

        private static bool LaHoiPheDuyetVaChotNgay(string q)
        {
            var duyet = Co(q, "duyet", "phe duyet");
            var chot = Co(q, "chot ngay", "chot lich", "len lich", "xep lich");
            return (duyet && chot) || (duyet && Co(q, "giam doc") && Co(q, "xuong") && chot);
        }

        private static bool LaHoiPheDuyet(string q) =>
            Co(q, "duyet", "phe duyet", "tu choi")
            && Co(q, "ho so", "bao tri", "sua chua", "giam doc", "thang");

        private static bool LaHoiLichBaoTri(string q)
        {
            if (Co(q, "lich su")) return false;
            return Co(q, "lich bao tri", "lich du kien", "lich sap toi")
                   || (Co(q, "lich") && Co(q, "bao tri", "sap toi", "du kien"));
        }

        private static bool LaHoiLichSuPhanCong(string q)
        {
            if (LaHoiChoPhanCong(q)) return false;
            return Co(q, "lich su phan cong", "ai duoc giao", "nv duoc giao", "nhan vien duoc giao",
                "phan cong cho ai", "da phan cong");
        }

        private static bool LaHoiHoSoBtSc(string q)
        {
            if (LaHoiVatTu(q) || LaHoiLichBaoTri(q) || LaHoiLichSuPhanCong(q)) return false;
            if (Co(q, "ho so bao tri", "ho so sua chua", "hs bao tri", "hs sua chua", "ho so bt", "ho so sc"))
                return true;
            if (Co(q, "ho so") && Co(q, "bao tri", "sua chua"))
                return true;
            if (Co(q, "bao tri", "sua chua")
                && Co(q, "bao nhieu", "co may", "tong", "hoan thanh", "cho duyet"))
                return true;
            return false;
        }

        // ===================== HỒ SƠ CHỜ PHÂN CÔNG =====================

        /// <summary>
        /// Hồ sơ bảo trì đã được Giám đốc duyệt nhưng CHƯA có ai nhận việc.
        /// PC «Từ chối» hoặc «Đã hủy» không tính là đã phân công.
        /// </summary>
        private async Task<List<HsTomTat>> LayChoPhanCongAsync(
            int? ngay, int? thang, int? nam, string vt, bool boQuaTriNho, CancellationToken ct)
        {
            var hs = await _db.HoSoBaoTris.AsNoTracking()
                .Where(h => h.TrangThai == "Đã duyệt"
                            || h.TrangThai == "Chờ phân công"
                            || h.TrangThai == "Đã phê duyệt")
                .Select(h => new
                {
                    h.MaHoSoBaoTri,
                    h.TrangThai,
                    h.NgayDuyet,
                    h.NgayTao,
                    TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                })
                .ToListAsync(ct);
            if (hs.Count == 0) return new List<HsTomTat>();

            var ids = hs.Select(x => x.MaHoSoBaoTri).ToList();

            var pcs = await _db.PhanCongCongViecs.AsNoTracking()
                .Where(p => p.MaHoSoBaoTri != null && ids.Contains(p.MaHoSoBaoTri.Value))
                .Select(p => new { p.MaHoSoBaoTri, p.TrangThai })
                .ToListAsync(ct);

            var plans = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.MaHoSoBaoTri != null && ids.Contains(c.MaHoSoBaoTri.Value))
                .Select(c => new { c.MaHoSoBaoTri, c.NgayDuKienBaoTri })
                .ToListAsync(ct);
            var dictPlan = plans
                .GroupBy(x => x.MaHoSoBaoTri!.Value)
                .ToDictionary(g => g.Key, g => g.Min(x => x.NgayDuKienBaoTri));

            var loaiTru = boQuaTriNho
                ? new HashSet<int>()
                : ChatbotTriNhoStore.LayLoaiTru(vt, "cho_phan_cong_bt");

            var y = nam ?? DateTime.Now.Year;
            var kq = new List<HsTomTat>();

            foreach (var h in hs)
            {
                if (loaiTru.Contains(h.MaHoSoBaoTri)) continue;

                var dsPc = pcs.Where(p => p.MaHoSoBaoTri == h.MaHoSoBaoTri).ToList();

                // Đã có người nhận việc (bất kỳ PC không hủy/không từ chối) → không còn chờ.
                var coNguoiNhan = dsPc.Any(p =>
                    !LaHuy(p.TrangThai)
                    && !string.Equals((p.TrangThai ?? "").Trim(), "Từ chối", StringComparison.OrdinalIgnoreCase));
                if (coNguoiNhan) continue;

                var coTuChoi = dsPc.Any(p =>
                    string.Equals((p.TrangThai ?? "").Trim(), "Từ chối", StringComparison.OrdinalIgnoreCase));

                DateOnly? plan = null;
                DateTime ngayLoc;
                if (dictPlan.TryGetValue(h.MaHoSoBaoTri, out var pd))
                {
                    plan = pd;
                    ngayLoc = pd.ToDateTime(TimeOnly.MinValue);
                }
                else
                {
                    ngayLoc = h.NgayDuyet ?? h.NgayTao;
                }

                if (thang != null && !(ngayLoc.Month == thang.Value && ngayLoc.Year == y)) continue;
                if (ngay != null && ngayLoc.Day != ngay.Value) continue;

                kq.Add(new HsTomTat
                {
                    Ma = "BT#" + h.MaHoSoBaoTri,
                    TenTb = h.TenTb,
                    TrangThai = h.TrangThai,
                    Ngay = ngayLoc,
                    NgayKeHoach = plan,
                    TuChoiPhanCong = coTuChoi
                });
            }

            return kq.OrderByDescending(x => x.Ngay).ThenBy(x => x.Ma).ToList();
        }

        private async Task<KetQuaXuLy> TraLoiChoPhanCongAsync(
            string vt, int? ngay, int? thang, int? nam, string q,
            CancellationToken ct, bool boQuaTriNho)
        {
            var ds = await LayChoPhanCongAsync(ngay, thang, nam, vt, boQuaTriNho, ct);
            var soNho = await LaySoNhoAsync(vt, "cho_phan_cong_bt", boQuaTriNho, ct);
            var so = soNho ?? ds.Count;
            var ky = thang != null ? $" trong tháng {thang}/{nam ?? DateTime.Now.Year}" : "";
            var chiDem = Co(q, "bao nhieu", "co may", "tong")
                         && !Co(q, "liet ke", "danh sach", "nhung", "nao", "ten", "gom");

            if (so == 0)
            {
                var sb = new StringBuilder();
                sb.Append($"Dạ, hiện **không có** hồ sơ bảo trì nào đang chờ phân công{ky}.");
                if (soNho == null && thang == null)
                {
                    var tt = await ThongKeTrangThaiBtAsync(ct);
                    if (tt.Count > 0)
                        sb.Append("\nTrên app hiện có: "
                                  + string.Join(" · ", tt.Select(x => $"{x.Key}: {x.Value}")) + ".");
                }
                sb.Append("\nHồ sơ chỉ vào diện chờ phân công sau khi Giám đốc duyệt.");
                return new KetQuaXuLy(true, sb.ToString(), "cho_phan_cong_bt", 0);
            }

            if (chiDem)
                return new KetQuaXuLy(true,
                    $"Dạ, {ky.TrimStart()} có **{so}** hồ sơ bảo trì đang chờ phân công.",
                    "cho_phan_cong_bt", so);

            var sbList = new StringBuilder();
            sbList.AppendLine($"Dạ, có **{so}** hồ sơ bảo trì đang chờ phân công{ky}:");
            foreach (var h in ds.Take(5))
            {
                var ngayTxt = h.NgayKeHoach?.ToString("dd/MM/yyyy")
                              ?? h.Ngay?.ToString("dd/MM/yyyy") ?? "—";
                sbList.AppendLine(
                    $"• {h.Ma} | {h.TenTb} | {h.TrangThai} | {ngayTxt}"
                    + (h.TuChoiPhanCong ? " | NV từ chối, cần phân công lại" : ""));
            }
            if (ds.Count > 5)
                sbList.AppendLine($"… còn {ds.Count - 5} hồ sơ khác.");
            return new KetQuaXuLy(true, sbList.ToString().TrimEnd(), "cho_phan_cong_bt", so);
        }

        private async Task<List<KeyValuePair<string, int>>> ThongKeTrangThaiBtAsync(CancellationToken ct)
        {
            var rows = await _db.HoSoBaoTris.AsNoTracking()
                .Select(h => h.TrangThai)
                .ToListAsync(ct);
            return rows
                .Where(x => !LaTrangThaiAnTrenApp(x))
                .GroupBy(x => string.IsNullOrWhiteSpace(x) ? "(trống)" : x)
                .OrderBy(g => g.Key)
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
                .ToList();
        }

        // ===================== DANH SÁCH → CHI TIẾT =====================

        private async Task<KetQuaXuLy> TraLoiChiTietHoSoAsync(
            string vt, int? ngay, int? thang, int? nam, CancellationToken ct)
        {
            var pending = await LayChoPhanCongAsync(ngay, thang, nam, vt, false, ct);
            var sb = new StringBuilder();
            sb.AppendLine("Dạ, màn danh sách HS bảo trì chờ phân công hiển thị gọn:");
            sb.AppendLine("• Mã hồ sơ (BT#…), tên thiết bị, trạng thái, ngày kế hoạch/ngày duyệt.");

            if (pending.Count > 0)
            {
                var vd = pending[0];
                sb.AppendLine($"Hiện có **{pending.Count}** hồ sơ, ví dụ **{vd.Ma}** — {vd.TenTb} ({vd.TrangThai}).");
            }
            else
            {
                sb.AppendLine("Hiện **không có** hồ sơ nào ở diện chờ phân công.");
            }

            sb.AppendLine("Mở chi tiết một hồ sơ sẽ thấy:");
            sb.AppendLine("• Thiết bị: mã, tên, vị trí lắp đặt.");
            sb.AppendLine("• Nội dung công việc và thời gian dự kiến (giờ bắt đầu/kết thúc).");
            sb.AppendLine("• Trạng thái, người duyệt và ngày duyệt.");
            sb.AppendLine("• Danh sách nhân viên được phân công.");
            sb.AppendLine("• Các bước quy trình đã chọn và vật tư đã dùng.");
            return new KetQuaXuLy(true, sb.ToString().TrimEnd(),
                "chi_tiet_ho_so", pending.Count);
        }

        // ===================== NHÂN VIÊN ĐANG ĐƯỢC PHÂN CÔNG =====================

        private async Task<KetQuaXuLy> TraLoiNhanVienDangPhanCongAsync(
            string vt, string q, CancellationToken ct)
        {
            var pcs = await _db.PhanCongCongViecs.AsNoTracking()
                .Select(p => new
                {
                    p.MaPhanCong,
                    p.MaNhanVienThucHien,
                    TenNv = p.MaNhanVienThucHienNavigation != null
                        ? p.MaNhanVienThucHienNavigation.HoTen
                        : ("NV#" + p.MaNhanVienThucHien),
                    p.MaHoSoBaoTri,
                    p.MaHoSoSuaChua,
                    p.TrangThai
                })
                .ToListAsync(ct);

            var activePc = pcs.Where(p =>
                !LaHuy(p.TrangThai)
                && !string.Equals((p.TrangThai ?? "").Trim(), "Từ chối", StringComparison.OrdinalIgnoreCase)
                && !string.Equals((p.TrangThai ?? "").Trim(), "Hoàn thành", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var btIds = activePc.Where(p => p.MaHoSoBaoTri != null)
                .Select(p => p.MaHoSoBaoTri!.Value).Distinct().ToList();
            var scIds = activePc.Where(p => p.MaHoSoSuaChua != null)
                .Select(p => p.MaHoSoSuaChua!.Value).Distinct().ToList();

            var bts = await _db.HoSoBaoTris.AsNoTracking()
                .Where(h => btIds.Contains(h.MaHoSoBaoTri))
                .Select(h => new { h.MaHoSoBaoTri, h.TrangThai })
                .ToListAsync(ct);
            var scs = await _db.HoSoSuaChuas.AsNoTracking()
                .Where(h => scIds.Contains(h.MaHoSoSuaChua))
                .Select(h => new { h.MaHoSoSuaChua, h.TrangThai })
                .ToListAsync(ct);
            var dictBt = bts.ToDictionary(x => x.MaHoSoBaoTri, x => x.TrangThai);
            var dictSc = scs.ToDictionary(x => x.MaHoSoSuaChua, x => x.TrangThai);

            bool DangLam(string? tt) =>
                tt is "Đang thực hiện" or "Chờ xác nhận" or "Đã phân công" or "Chờ xác nhận kết quả";

            var active = activePc.Where(p =>
            {
                if (p.MaHoSoBaoTri != null && dictBt.TryGetValue(p.MaHoSoBaoTri.Value, out var tt))
                    return DangLam(tt);
                if (p.MaHoSoSuaChua != null && dictSc.TryGetValue(p.MaHoSoSuaChua.Value, out var tt2))
                    return DangLam(tt2);
                return false;
            }).ToList();

            var sb = new StringBuilder();
            if (active.Count == 0)
            {
                sb.AppendLine("Dạ, hiện **không có** nhân viên nào đang được phân công việc bảo trì.");
            }
            else
            {
                var nhom = active
                    .GroupBy(p => p.TenNv)
                    .Select(g => new
                    {
                        Ten = g.Key,
                        SoViec = g.Select(x => x.MaHoSoBaoTri != null
                            ? "BT#" + x.MaHoSoBaoTri
                            : "SC#" + x.MaHoSoSuaChua).Distinct().Count()
                    })
                    .OrderByDescending(x => x.SoViec).ThenBy(x => x.Ten)
                    .ToList();

                sb.AppendLine($"Dạ, hiện có **{nhom.Count}** nhân viên đang được phân công việc bảo trì:");
                foreach (var n in nhom.Take(8))
                    sb.AppendLine($"• {n.Ten} — {n.SoViec} việc");
                if (nhom.Count > 8)
                    sb.AppendLine($"… còn {nhom.Count - 8} nhân viên khác.");
            }

            if (Co(q, "lam the nao", "phan cong them", "cach", "nhu the nao"))
            {
                sb.AppendLine("Cách phân công thêm:");
                sb.AppendLine("• Mở hồ sơ bảo trì đã được Giám đốc duyệt.");
                sb.AppendLine("• Vào mục Phân công, chọn một hoặc nhiều NVKT.");
                sb.AppendLine("• Gửi phân công để nhân viên nhận việc.");
                sb.AppendLine("• Khi nhân viên bận, mở lại phân công cũ để cập nhật người thay thế.");
            }

            sb.AppendLine("Bước chốt ngày do Xưởng phụ trách, bước duyệt do Giám đốc phụ trách — mình chỉ tổng hợp trong phạm vi phân công của tổ.");
            return new KetQuaXuLy(true, sb.ToString().TrimEnd(), "nhan_vien_dang_phan_cong", active.Count);
        }

        // ===================== THIẾT BỊ =====================

        private async Task<KetQuaXuLy> TraLoiThietBiAsync(string q, CancellationToken ct)
        {
            var tbs = await _db.ThietBis.AsNoTracking()
                .Select(t => new
                {
                    t.MaThietBi,
                    t.TenThietBi,
                    t.LoaiThietBi,
                    t.ViTriLapDat,
                    t.TinhTrangHienTai,
                    t.NgayBaoTriGanNhat,
                    t.NgayBaoTriTiepTheo
                })
                .ToListAsync(ct);

            int Dem(string k) => tbs.Count(x =>
                (x.TinhTrangHienTai ?? "").Contains(k, StringComparison.OrdinalIgnoreCase));

            var sx = Dem("Sản xuất");
            var bt = Dem("Bảo trì");
            var sc = Dem("Sửa chữa");

            var hoiTong = Co(q, "bao nhieu", "co may", "tong cong", "tong so", "so luong", "dem");
            var hoiDanhSach = Co(q, "liet ke", "danh sach", "ten", "gom", "nhung may", "may nao", "tung may");

            // Hỏi tổng/đếm → trả chỉ số, không đoán ra một thiết bị cụ thể.
            if (hoiTong)
                return new KetQuaXuLy(true,
                    $"Dạ, hiện có **{tbs.Count}** thiết bị: Sản xuất **{sx}** · Bảo trì **{bt}** · Sửa chữa **{sc}**.",
                    "thiet_bi", tbs.Count);

            // Hỏi một thiết bị cụ thể.
            var tenGoi = TrichTenThietBi(q);
            if (tenGoi != null)
            {
                var hit = tbs
                    .Select(x => new { x, sc = DiemKhop(tenGoi, x.TenThietBi) })
                    .Where(x => x.sc >= 2)
                    .OrderByDescending(x => x.sc)
                    .Select(x => x.x)
                    .FirstOrDefault();
                if (hit != null)
                {
                    var tt = string.IsNullOrWhiteSpace(hit.TinhTrangHienTai) ? "chưa ghi" : hit.TinhTrangHienTai;
                    var gan = hit.NgayBaoTriGanNhat?.ToString("dd/MM/yyyy") ?? "chưa có";
                    var tiep = hit.NgayBaoTriTiepTheo?.ToString("dd/MM/yyyy") ?? "chưa có";
                    var viTri = string.IsNullOrWhiteSpace(hit.ViTriLapDat) ? "chưa ghi" : hit.ViTriLapDat;
                    return new KetQuaXuLy(true,
                        $"Dạ, **{hit.TenThietBi}** (TB#{hit.MaThietBi}).\n"
                        + $"• Loại: {hit.LoaiThietBi ?? "—"}\n"
                        + $"• Vị trí: {viTri}\n"
                        + $"• Trạng thái: **{tt}**\n"
                        + $"• BT gần nhất: {gan} | BT tiếp theo: {tiep}",
                        "thiet_bi", null, "TB#" + hit.MaThietBi);
                }
            }

            var hoiTrangThaiBaoTri = Co(q, "trang thai bao tri", "dang bao tri", "dang o trang thai bao tri");
            if (hoiTrangThaiBaoTri)
                return new KetQuaXuLy(true,
                    $"Dạ, hiện **{bt}** thiết bị đang ở trạng thái bảo trì "
                    + $"(tổng **{tbs.Count}** thiết bị: Sản xuất {sx} · Sửa chữa {sc}).",
                    "thiet_bi", bt);

            if (!hoiDanhSach)
                return new KetQuaXuLy(true,
                    $"Dạ, hiện có **{tbs.Count}** thiết bị: Sản xuất **{sx}** · Bảo trì **{bt}** · Sửa chữa **{sc}**.",
                    "thiet_bi", tbs.Count);

            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, hiện có **{tbs.Count}** thiết bị: Sản xuất {sx} · Bảo trì {bt} · Sửa chữa {sc}.");
            foreach (var t in tbs.OrderBy(x => x.TenThietBi).Take(8))
                sb.AppendLine($"• TB#{t.MaThietBi} {t.TenThietBi} — {t.TinhTrangHienTai ?? "—"}");
            if (tbs.Count > 8)
                sb.AppendLine($"… còn {tbs.Count - 8} thiết bị khác.");
            return new KetQuaXuLy(true, sb.ToString().TrimEnd(), "thiet_bi", tbs.Count);
        }

        // ===================== NHIỀU VẾ: CHỜ PHÂN CÔNG + BƯỚC CÒN LẠI =====================

        private async Task<KetQuaXuLy> TraLoiChoPhanCongVaBuocAsync(
            string vt, int? ngay, int? thang, int? nam, CancellationToken ct)
        {
            var pending = await LayChoPhanCongAsync(ngay, thang, nam, vt, false, ct);
            var tienDo = await LayTienDoDangThucHienAsync(ct);

            var sb = new StringBuilder();
            sb.AppendLine("Dạ, mình tách 2 ý:");
            sb.AppendLine($"1) Hồ sơ chờ phân công: **{pending.Count}**"
                          + (pending.Count == 0 ? " — hiện không có hồ sơ nào ở diện này." : "."));

            if (tienDo.Count == 0)
            {
                sb.AppendLine("2) Hồ sơ đang thực hiện: **0** — hiện không có hồ sơ nào đang thực hiện nên không có bước nào chưa xong.");
            }
            else
            {
                sb.AppendLine($"2) Hồ sơ đang thực hiện: **{tienDo.Count}**");
                foreach (var t in tienDo.Take(5))
                    sb.AppendLine($"• {t.Ma}: còn **{t.Con}**/{t.Tong} bước chưa xong.");
                if (tienDo.Count > 5)
                    sb.AppendLine($"… còn {tienDo.Count - 5} hồ sơ khác.");
            }

            return new KetQuaXuLy(true, sb.ToString().TrimEnd(), "cho_phan_cong_bt", pending.Count);
        }
    }
}
