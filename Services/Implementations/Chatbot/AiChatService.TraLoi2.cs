using System.Text;
using Microsoft.EntityFrameworkCore;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class AiChatService
    {
        // ===================== DUYỆT + CHỐT NGÀY (RANH GIỚI VAI TRÒ) =====================

        private async Task<KetQuaXuLy> TraLoiPheDuyetChotNgayAsync(
            string vt, int? thang, int? nam, CancellationToken ct)
        {
            var now = DateTime.Now;
            var m = thang ?? now.Month;
            var y = nam ?? now.Year;

            var ls = await _db.LichSuPheDuyets.AsNoTracking()
                .Select(p => new { p.MaHoSoBaoTri, p.MaHoSoSuaChua, p.QuyetDinh, p.NgayDuyet })
                .ToListAsync(ct);
            var rows = ls.Where(p => p.NgayDuyet.Year == y && p.NgayDuyet.Month == m).ToList();

            static bool LaDuyet(string? qd)
            {
                var t = ChuanHoa(qd ?? "");
                return (t.Contains("duyet") || t.Contains("dong y"))
                       && !t.Contains("tu choi") && !t.Contains("khong duyet");
            }

            static bool LaTuChoi(string? qd)
            {
                var t = ChuanHoa(qd ?? "");
                return t.Contains("tu choi") || t.Contains("khong duyet");
            }

            var duyetBt = rows
                .Where(p => p.MaHoSoBaoTri != null && LaDuyet(p.QuyetDinh))
                .Select(p => p.MaHoSoBaoTri!.Value).Distinct().Count();
            var duyetSc = rows
                .Where(p => p.MaHoSoSuaChua != null && LaDuyet(p.QuyetDinh))
                .Select(p => p.MaHoSoSuaChua!.Value).Distinct().Count();
            var tuChoi = rows
                .Where(p => LaTuChoi(p.QuyetDinh))
                .Select(p => p.MaHoSoBaoTri != null ? "BT#" + p.MaHoSoBaoTri
                    : p.MaHoSoSuaChua != null ? "SC#" + p.MaHoSoSuaChua : "?")
                .Distinct().Count();

            var kh = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.MaHoSoBaoTri != null)
                .Select(c => new
                {
                    c.MaHoSoBaoTri,
                    c.NgayDuKienBaoTri,
                    Ten = c.MaThietBiNavigation != null
                        ? c.MaThietBiNavigation.TenThietBi
                        : ("TB#" + c.MaThietBi)
                })
                .ToListAsync(ct);

            var daChot = kh
                .Where(c => c.NgayDuKienBaoTri.Year == y && c.NgayDuKienBaoTri.Month == m)
                .GroupBy(c => c.MaHoSoBaoTri!.Value)
                .Select(g => g.OrderBy(x => x.NgayDuKienBaoTri).First())
                .OrderBy(x => x.NgayDuKienBaoTri)
                .ThenBy(x => x.MaHoSoBaoTri)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, tháng {m}/{y} (toàn công ty):");
            sb.AppendLine($"1) Giám đốc đã duyệt **{duyetBt}** hồ sơ bảo trì"
                          + (duyetSc > 0 ? $" và **{duyetSc}** hồ sơ sửa chữa" : "")
                          + (tuChoi > 0 ? $" ({tuChoi} hồ sơ bị từ chối)." : "."));

            if (daChot.Count == 0)
            {
                sb.AppendLine("2) Xưởng chưa chốt ngày hồ sơ bảo trì nào trong tháng này.");
            }
            else
            {
                sb.AppendLine($"2) Xưởng đã chốt ngày cho **{daChot.Count}** hồ sơ:");
                foreach (var c in daChot.Take(8))
                    sb.AppendLine($"• BT#{c.MaHoSoBaoTri} — {c.NgayDuKienBaoTri:dd/MM/yyyy} · {c.Ten}");
                if (daChot.Count > 8)
                    sb.AppendLine($"… còn {daChot.Count - 8} hồ sơ khác.");
            }

            sb.AppendLine(PhamViPheDuyetChotNgay(vt));
            return new KetQuaXuLy(true, sb.ToString().TrimEnd(), "phe_duyet_chot_ngay", duyetBt);
        }

        private static string PhamViPheDuyetChotNgay(string vt) => vt switch
        {
            "Tổ trưởng cơ điện" or "Tổ trưởng kỹ thuật" =>
                "Bước duyệt do Giám đốc phụ trách; bước chốt ngày do Xưởng phụ trách — mình chỉ tổng hợp số liệu liên quan tổ.",
            "Xưởng" or "Tổ trưởng sản xuất" =>
                "Bước duyệt do Giám đốc phụ trách — mình chỉ tổng hợp số liệu ngày bảo trì của Xưởng.",
            "Giám đốc" or "Phó giám đốc" =>
                "Bước chốt ngày do Xưởng phụ trách — mình chỉ tổng hợp số liệu duyệt.",
            _ =>
                "Số liệu duyệt/chốt ngày toàn công ty nằm ngoài phạm vi tài khoản NVKT."
        };

        // ===================== ĐÃ HOÀN THÀNH THEO THÁNG =====================

        private async Task<KetQuaXuLy> TraLoiDemHoanThanhAsync(
            string q, int? thang, int? nam, string vt, bool boQuaTriNho, CancellationToken ct)
        {
            var now = DateTime.Now;
            var m = thang ?? now.Month;
            var y = nam ?? now.Year;

            var hoiSc = Co(q, "sua chua") || CoTu(q, "sc");
            var hoiBt = Co(q, "bao tri") || !hoiSc;

            var sb = new StringBuilder();
            var tongTraLoi = 0;
            var chuDe = "bt_hoan_thanh_thang";

            if (hoiBt)
            {
                var btDone = await _db.HoSoBaoTris.AsNoTracking()
                    .Where(h => h.TrangThai == "Đã hoàn thành")
                    .Select(h => new
                    {
                        h.MaHoSoBaoTri,
                        h.ThoiDiemKetThucThucTe,
                        h.NgayDuyet,
                        h.NgayTao
                    })
                    .ToListAsync(ct);

                // Đếm theo NGÀY HOÀN THÀNH thực tế (không phải ngày kế hoạch).
                var btThang = btDone.Count(h =>
                {
                    var d = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao;
                    return d.Month == m && d.Year == y;
                });

                var soNho = await LaySoNhoAsync(vt, "bt_hoan_thanh_thang", boQuaTriNho, ct);
                var so = soNho ?? btThang;

                sb.Append($"Dạ, tháng {m}/{y} có **{so}** hồ sơ bảo trì **Đã hoàn thành** "
                          + "(tính theo ngày hoàn thành thực tế).");
                sb.Append($"\nTrên app hiện có tổng **{btDone.Count}** hồ sơ bảo trì Đã hoàn thành.");
                tongTraLoi = so;
            }

            if (hoiSc)
            {
                var scDone = await _db.HoSoSuaChuas.AsNoTracking()
                    .Where(h => h.TrangThai == "Đã hoàn thành")
                    .Select(h => new { h.ThoiDiemKetThucThucTe, h.NgayDuyet, h.NgayTao })
                    .ToListAsync(ct);
                var scThang = scDone.Count(h =>
                {
                    var d = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao;
                    return d.Month == m && d.Year == y;
                });

                if (sb.Length > 0)
                {
                    sb.AppendLine();
                    sb.Append($"Tháng {m}/{y} có **{scThang}** hồ sơ sửa chữa **Đã hoàn thành** "
                              + "(tính theo ngày hoàn thành thực tế).");
                }
                else
                {
                    sb.Append($"Dạ, tháng {m}/{y} có **{scThang}** hồ sơ sửa chữa **Đã hoàn thành** "
                              + "(tính theo ngày hoàn thành thực tế).");
                }
                if (!hoiBt)
                {
                    chuDe = "sc_hoan_thanh_thang";
                    tongTraLoi = scThang;
                }
            }

            return new KetQuaXuLy(true, sb.ToString().TrimEnd(), chuDe, tongTraLoi);
        }

        // ===================== BƯỚC QUY TRÌNH =====================

        private async Task<KetQuaXuLy> TraLoiBuocAsync(
            int maNd, string vt, int? maBt, int? maSc, string q, CancellationToken ct)
        {
            IQueryable<Core.Entities.TienDoBuocQuyTrinh> query =
                _db.TienDoBuocQuyTrinhs.AsNoTracking();

            if (maBt != null) query = query.Where(t => t.MaHoSoBaoTri == maBt);
            else if (maSc != null) query = query.Where(t => t.MaHoSoSuaChua == maSc);
            else
            {
                // Không chỉ định hồ sơ → chỉ lấy hồ sơ đang thực hiện (đúng phạm vi vai trò).
                var activeBt = await _db.HoSoBaoTris.AsNoTracking()
                    .Where(h => h.TrangThai == "Đang thực hiện" || h.TrangThai == "Chờ xác nhận")
                    .Select(h => h.MaHoSoBaoTri).ToListAsync(ct);
                var activeSc = await _db.HoSoSuaChuas.AsNoTracking()
                    .Where(h => h.TrangThai == "Đang thực hiện" || h.TrangThai == "Chờ xác nhận")
                    .Select(h => h.MaHoSoSuaChua).ToListAsync(ct);

                if (vt == "Nhân viên kỹ thuật")
                {
                    var maNv = await _db.NhanViens.AsNoTracking()
                        .Where(n => n.MaNguoiDung == maNd)
                        .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
                    if (maNv == null)
                        return new KetQuaXuLy(true, "Dạ, mình chưa xác định được tài khoản nhân viên của bạn.", "buoc_quy_trinh", 0);

                    var myBt = await _db.PhanCongCongViecs.AsNoTracking()
                        .Where(p => p.MaNhanVienThucHien == maNv && p.MaHoSoBaoTri != null)
                        .Select(p => p.MaHoSoBaoTri!.Value).Distinct().ToListAsync(ct);
                    var mySc = await _db.PhanCongCongViecs.AsNoTracking()
                        .Where(p => p.MaNhanVienThucHien == maNv && p.MaHoSoSuaChua != null)
                        .Select(p => p.MaHoSoSuaChua!.Value).Distinct().ToListAsync(ct);
                    activeBt = activeBt.Where(myBt.Contains).ToList();
                    activeSc = activeSc.Where(mySc.Contains).ToList();
                }

                if (activeBt.Count == 0 && activeSc.Count == 0)
                    return new KetQuaXuLy(true,
                        "Dạ, hiện không có hồ sơ nào đang thực hiện nên không có bước nào chưa xong.",
                        "buoc_quy_trinh", 0);

                query = query.Where(t =>
                    (t.MaHoSoBaoTri != null && activeBt.Contains(t.MaHoSoBaoTri.Value))
                    || (t.MaHoSoSuaChua != null && activeSc.Contains(t.MaHoSoSuaChua.Value)));
            }

            var rows = await query.OrderByDescending(t => t.ThoiDiemCapNhat).Take(300)
                .Select(t => new { t.MaHoSoBaoTri, t.MaHoSoSuaChua, t.SoBuoc, t.MoTaBuoc, t.TrangThai })
                .ToListAsync(ct);

            if (rows.Count == 0)
            {
                var hs = maBt != null ? $"BT#{maBt}" : maSc != null ? $"SC#{maSc}" : "hồ sơ đang thực hiện";
                return new KetQuaXuLy(true,
                    $"Dạ, {hs} chưa ghi nhận bước quy trình nào.", "buoc_quy_trinh", 0);
            }

            var nhom = rows
                .GroupBy(t => t.MaHoSoBaoTri != null ? $"BT#{t.MaHoSoBaoTri}" : $"SC#{t.MaHoSoSuaChua}")
                .ToList();

            // Một hồ sơ cụ thể → hiển thị danh sách bước.
            if (nhom.Count == 1)
            {
                var g = nhom[0];
                var uniq = g.GroupBy(x => x.SoBuoc).Select(x => x.First())
                    .OrderBy(x => x.SoBuoc).ToList();
                var xong = uniq.Count(x => LaBuocXong(x.TrangThai));
                var con = uniq.Count(x => LaBuocCon(x.TrangThai));

                var sb = new StringBuilder();
                sb.AppendLine($"Dạ, hồ sơ **{g.Key}**: **{uniq.Count}** bước, đã xong **{xong}**, còn **{con}** bước chưa xong.");
                foreach (var b in uniq)
                    sb.AppendLine($"• B{b.SoBuoc}: {b.MoTaBuoc} ({MoTaTrangThaiBuoc(b.TrangThai)})");
                return new KetQuaXuLy(true, sb.ToString().TrimEnd(), "buoc_quy_trinh", con);
            }

            var sb2 = new StringBuilder();
            sb2.AppendLine($"Dạ, có **{nhom.Count}** hồ sơ đang có bước quy trình:");
            foreach (var g in nhom.Take(5))
            {
                var uniq = g.GroupBy(x => x.SoBuoc).Select(x => x.First()).ToList();
                var con = uniq.Count(x => LaBuocCon(x.TrangThai));
                sb2.AppendLine($"• {g.Key}: còn **{con}**/{uniq.Count} bước chưa xong.");
            }
            if (nhom.Count > 5)
                sb2.AppendLine($"… còn {nhom.Count - 5} hồ sơ khác.");
            return new KetQuaXuLy(true, sb2.ToString().TrimEnd(), "buoc_quy_trinh", null);
        }

        private static bool LaBuocXong(string? tt) =>
            tt is "DaXong" or "DaCapNhat";

        private static bool LaBuocCon(string? tt) =>
            tt is "DuocChon" or "DangLam" or "BuocMoi";

        private static string MoTaTrangThaiBuoc(string? tt) => tt switch
        {
            "DaXong" => "Đã xong",
            "DaCapNhat" => "Đã cập nhật",
            "DangLam" => "Đang làm",
            "BuocMoi" => "Bước mới (NVKT tự thêm)",
            "DuocChon" => "Đã chọn",
            "BoChon" => "Bỏ chọn",
            null or "" => "Chưa rõ",
            _ => tt
        };

        private async Task<List<(string Ma, int Tong, int Con)>> LayTienDoDangThucHienAsync(
            CancellationToken ct)
        {
            var bts = await _db.HoSoBaoTris.AsNoTracking()
                .Where(h => h.TrangThai == "Đang thực hiện" || h.TrangThai == "Chờ xác nhận")
                .Select(h => new { h.MaHoSoBaoTri, TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "" })
                .ToListAsync(ct);
            var scs = await _db.HoSoSuaChuas.AsNoTracking()
                .Where(h => h.TrangThai == "Đang thực hiện" || h.TrangThai == "Chờ xác nhận")
                .Select(h => new { h.MaHoSoSuaChua, TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : "" })
                .ToListAsync(ct);

            var idsBt = bts.Select(x => x.MaHoSoBaoTri).ToList();
            var idsSc = scs.Select(x => x.MaHoSoSuaChua).ToList();

            var steps = await _db.TienDoBuocQuyTrinhs.AsNoTracking()
                .Where(t => (t.MaHoSoBaoTri != null && idsBt.Contains(t.MaHoSoBaoTri.Value))
                            || (t.MaHoSoSuaChua != null && idsSc.Contains(t.MaHoSoSuaChua.Value)))
                .OrderByDescending(t => t.ThoiDiemCapNhat)
                .Select(t => new { t.MaHoSoBaoTri, t.MaHoSoSuaChua, t.SoBuoc, t.TrangThai })
                .ToListAsync(ct);

            var kq = new List<(string Ma, int Tong, int Con)>();
            foreach (var h in bts)
            {
                var uniq = steps.Where(x => x.MaHoSoBaoTri == h.MaHoSoBaoTri)
                    .GroupBy(x => x.SoBuoc).Select(g => g.First()).ToList();
                var tong = uniq.Count(x => !string.Equals(x.TrangThai, "BoChon", StringComparison.OrdinalIgnoreCase));
                var con = uniq.Count(x => LaBuocCon(x.TrangThai));
                kq.Add(($"BT#{h.MaHoSoBaoTri}", tong, con));
            }
            foreach (var h in scs)
            {
                var uniq = steps.Where(x => x.MaHoSoSuaChua == h.MaHoSoSuaChua)
                    .GroupBy(x => x.SoBuoc).Select(g => g.First()).ToList();
                var tong = uniq.Count(x => !string.Equals(x.TrangThai, "BoChon", StringComparison.OrdinalIgnoreCase));
                var con = uniq.Count(x => LaBuocCon(x.TrangThai));
                kq.Add(($"SC#{h.MaHoSoSuaChua}", tong, con));
            }

            return kq;
        }
    }
}
