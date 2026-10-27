using System.Text;
using Microsoft.EntityFrameworkCore;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class AiChatService
    {
        // ===================== HỒ SƠ BT / SC (ĐẾM + LIỆT KÊ) =====================

        private async Task<KetQuaXuLy> TraLoiHoSoBtScAsync(
            string vt, int? ngay, int? thang, int? nam, string q, CancellationToken ct)
        {
            var hoiSc = Co(q, "sua chua") && !Co(q, "bao tri");
            var hoiCaHai = Co(q, "sua chua") && Co(q, "bao tri");
            var hoiLietKe = Co(q, "liet ke", "danh sach", "nhung ho so", "ho so nao", "ten", "gom");

            string? trangThai = null;
            if (Co(q, "hoan thanh")) trangThai = "Đã hoàn thành";
            else if (Co(q, "cho duyet")) trangThai = "Chờ duyệt";
            else if (Co(q, "dang thuc hien")) trangThai = "Đang thực hiện";
            else if (Co(q, "tu choi")) trangThai = "Từ chối";
            else if (Co(q, "cho xuong", "cho xuat xuong")) trangThai = "Chờ xưởng";
            else if (Co(q, "da duyet")) trangThai = "Đã duyệt";

            var now = DateTime.Now;
            var y = nam ?? now.Year;

            var sb = new StringBuilder();
            var tong = 0;

            if (!hoiSc || hoiCaHai)
            {
                var bts = await _db.HoSoBaoTris.AsNoTracking()
                    .Select(h => new
                    {
                        h.MaHoSoBaoTri,
                        h.TrangThai,
                        h.NgayDuyet,
                        h.NgayTao,
                        h.ThoiDiemKetThucThucTe,
                        TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                    })
                    .ToListAsync(ct);
                var plans = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                    .Where(c => c.MaHoSoBaoTri != null)
                    .Select(c => new { Ma = c.MaHoSoBaoTri!.Value, c.NgayDuKienBaoTri })
                    .ToListAsync(ct);
                var dictPlan = plans.GroupBy(x => x.Ma)
                    .ToDictionary(g => g.Key, g => g.First().NgayDuKienBaoTri);

                var rows = bts.Where(h => !LaTrangThaiAnTrenApp(h.TrangThai)).Select(h =>
                {
                    var d = dictPlan.TryGetValue(h.MaHoSoBaoTri, out var nk)
                        ? nk.ToDateTime(TimeOnly.MinValue)
                        : (h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao);
                    return new { h.MaHoSoBaoTri, h.TrangThai, h.TenTb, Ngay = d };
                }).ToList();

                if (thang != null || ngay != null)
                    rows = rows.Where(x => x.Ngay.Year == y
                                           && (thang == null || x.Ngay.Month == thang)
                                           && (ngay == null || x.Ngay.Day == ngay)).ToList();
                if (!string.IsNullOrEmpty(trangThai))
                    rows = rows.Where(x => x.TrangThai == trangThai).ToList();

                tong += rows.Count;
                var dem = rows.GroupBy(x => x.TrangThai).OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}: {g.Count()}").ToList();

                if (hoiLietKe && rows.Count > 0)
                {
                    sb.AppendLine($"Dạ, có **{rows.Count}** hồ sơ bảo trì"
                                  + (trangThai != null ? $" **{trangThai}**" : "") + ":");
                    foreach (var h in rows.OrderByDescending(x => x.Ngay).Take(8))
                        sb.AppendLine($"• BT#{h.MaHoSoBaoTri} | {h.TenTb} | {h.TrangThai} | {h.Ngay:dd/MM/yyyy}");
                    if (rows.Count > 8)
                        sb.AppendLine($"… còn {rows.Count - 8} hồ sơ khác.");
                }
                else if (trangThai != null)
                {
                    sb.AppendLine($"Dạ, hiện có **{rows.Count}** hồ sơ bảo trì **{trangThai}**.");
                }
                else
                {
                    sb.AppendLine($"Dạ, hiện có **{rows.Count}** hồ sơ bảo trì"
                                  + (dem.Count > 0 ? ": " + string.Join(" · ", dem) + "." : "."));
                }
            }

            if (hoiSc || hoiCaHai)
            {
                var scs = await _db.HoSoSuaChuas.AsNoTracking()
                    .Select(h => new
                    {
                        h.MaHoSoSuaChua,
                        h.TrangThai,
                        h.NgayDuyet,
                        h.NgayTao,
                        h.ThoiDiemKetThucThucTe,
                        TenTb = h.MaThieBiNavigation != null ? h.MaThieBiNavigation.TenThietBi : ""
                    })
                    .ToListAsync(ct);

                var rowsSc = scs.Where(h => !LaTrangThaiAnTrenApp(h.TrangThai)).Select(h =>
                {
                    var d = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao;
                    return new { h.MaHoSoSuaChua, h.TrangThai, h.TenTb, Ngay = d };
                }).ToList();

                if (thang != null || ngay != null)
                    rowsSc = rowsSc.Where(x => x.Ngay.Year == y
                                               && (thang == null || x.Ngay.Month == thang)
                                               && (ngay == null || x.Ngay.Day == ngay)).ToList();
                if (!string.IsNullOrEmpty(trangThai))
                    rowsSc = rowsSc.Where(x => x.TrangThai == trangThai).ToList();

                tong += rowsSc.Count;
                var demSc = rowsSc.GroupBy(x => x.TrangThai).OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}: {g.Count()}").ToList();

                if (hoiLietKe && rowsSc.Count > 0)
                {
                    sb.AppendLine($"Dạ, có **{rowsSc.Count}** hồ sơ sửa chữa"
                                  + (trangThai != null ? $" **{trangThai}**" : "") + ":");
                    foreach (var h in rowsSc.OrderByDescending(x => x.Ngay).Take(8))
                        sb.AppendLine($"• SC#{h.MaHoSoSuaChua} | {h.TenTb} | {h.TrangThai} | {h.Ngay:dd/MM/yyyy}");
                    if (rowsSc.Count > 8)
                        sb.AppendLine($"… còn {rowsSc.Count - 8} hồ sơ khác.");
                }
                else if (trangThai != null)
                {
                    sb.AppendLine($"Dạ, hiện có **{rowsSc.Count}** hồ sơ sửa chữa **{trangThai}**.");
                }
                else
                {
                    sb.AppendLine($"Dạ, hiện có **{rowsSc.Count}** hồ sơ sửa chữa"
                                  + (demSc.Count > 0 ? ": " + string.Join(" · ", demSc) + "." : "."));
                }
            }

            if (sb.Length == 0)
                sb.AppendLine("Dạ, hiện chưa có hồ sơ nào khớp câu hỏi trong phạm vi của bạn.");

            return new KetQuaXuLy(true, sb.ToString().TrimEnd(), "chung", tong);
        }

        // ===================== DUYỆT / TỪ CHỐI =====================

        private async Task<KetQuaXuLy> TraLoiPheDuyetAsync(
            string vt, int? thang, int? nam, string q, CancellationToken ct)
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

            var duyet = rows
                .Where(p => LaDuyet(p.QuyetDinh))
                .Select(p => p.MaHoSoBaoTri != null ? "BT#" + p.MaHoSoBaoTri
                    : p.MaHoSoSuaChua != null ? "SC#" + p.MaHoSoSuaChua : "?")
                .Distinct().ToList();
            var tuChoi = rows
                .Where(p => LaTuChoi(p.QuyetDinh))
                .Select(p => p.MaHoSoBaoTri != null ? "BT#" + p.MaHoSoBaoTri
                    : p.MaHoSoSuaChua != null ? "SC#" + p.MaHoSoSuaChua : "?")
                .Distinct().ToList();

            var sb = new StringBuilder();
            sb.Append($"Dạ, tháng {m}/{y} Giám đốc đã duyệt **{duyet.Count}** hồ sơ");
            if (tuChoi.Count > 0)
                sb.Append($", từ chối **{tuChoi.Count}** hồ sơ");
            sb.Append('.');

            if (Co(q, "nao", "liet ke", "danh sach", "ho so nao") && duyet.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Hồ sơ đã duyệt:");
                foreach (var ma in duyet.Take(8))
                    sb.AppendLine($"• {ma}");
                if (duyet.Count > 8)
                    sb.AppendLine($"… còn {duyet.Count - 8} hồ sơ khác.");
            }

            sb.AppendLine(PhamViPheDuyetChotNgay(vt));
            return new KetQuaXuLy(true, sb.ToString().TrimEnd(), "phe_duyet_chot_ngay", duyet.Count);
        }

        // ===================== LỊCH BẢO TRÌ =====================

        private async Task<KetQuaXuLy> TraLoiLichBaoTriAsync(CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var lich = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.NgayDuKienBaoTri >= today)
                .OrderBy(c => c.NgayDuKienBaoTri).Take(12)
                .Select(c => new
                {
                    c.NgayDuKienBaoTri,
                    c.MaHoSoBaoTri,
                    Ten = c.MaThietBiNavigation != null
                        ? c.MaThietBiNavigation.TenThietBi
                        : ("TB#" + c.MaThietBi)
                })
                .ToListAsync(ct);

            if (lich.Count == 0)
                return new KetQuaXuLy(true, "Dạ, chưa có lịch bảo trì dự kiến sắp tới.", "lich_bao_tri", 0);

            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, lịch bảo trì dự kiến sắp tới (**{lich.Count}** mốc):");
            foreach (var c in lich)
                sb.AppendLine($"• {c.NgayDuKienBaoTri:dd/MM/yyyy} — {c.Ten}"
                              + (c.MaHoSoBaoTri.HasValue ? $" (BT#{c.MaHoSoBaoTri})" : ""));
            return new KetQuaXuLy(true, sb.ToString().TrimEnd(), "lich_bao_tri", lich.Count);
        }

        // ===================== LỊCH SỬ PHÂN CÔNG =====================

        private async Task<KetQuaXuLy> TraLoiLichSuPhanCongAsync(string vt, CancellationToken ct)
        {
            var pcs = await _db.PhanCongCongViecs.AsNoTracking()
                .OrderByDescending(p => p.NgayPhanCong).Take(30)
                .Select(p => new
                {
                    p.MaPhanCong,
                    p.MaHoSoBaoTri,
                    p.MaHoSoSuaChua,
                    p.TrangThai,
                    p.NgayPhanCong,
                    TenNv = p.MaNhanVienThucHienNavigation != null
                        ? p.MaNhanVienThucHienNavigation.HoTen
                        : ("NV#" + p.MaNhanVienThucHien)
                })
                .ToListAsync(ct);

            if (pcs.Count == 0)
                return new KetQuaXuLy(true, "Dạ, chưa có phân công công việc nào.", "chung", 0);

            var sb = new StringBuilder();
            sb.AppendLine("Dạ, đây là **lịch sử phân công** (PC#), không phải danh sách hồ sơ chờ phân công:");
            sb.AppendLine(string.Join(" · ", pcs.GroupBy(x => x.TrangThai ?? "(trống)")
                .OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}")));
            foreach (var p in pcs.Take(10))
            {
                var hs = p.MaHoSoBaoTri != null ? $"BT#{p.MaHoSoBaoTri}" : $"SC#{p.MaHoSoSuaChua}";
                sb.AppendLine($"• PC#{p.MaPhanCong} | {hs} | {p.TenNv} | {p.TrangThai} | {p.NgayPhanCong:dd/MM/yyyy}");
            }
            if (pcs.Count > 10)
                sb.AppendLine($"… và {pcs.Count - 10} phân công khác trong lịch sử gần đây.");
            return new KetQuaXuLy(true, sb.ToString().TrimEnd(), "chung", pcs.Count);
        }

        // ===================== VẬT TƯ =====================

        private async Task<KetQuaXuLy> TraLoiVatTuAsync(
            int maNd, string vt, int? maBt, int? maSc, string q, CancellationToken ct)
        {
            var query = _db.HoSoSuDungVatTus.AsNoTracking().AsQueryable();

            if (vt == "Nhân viên kỹ thuật")
            {
                var maNv = await _db.NhanViens.AsNoTracking()
                    .Where(n => n.MaNguoiDung == maNd)
                    .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
                if (maNv == null)
                    return new KetQuaXuLy(true, "Dạ, mình chưa xác định được tài khoản nhân viên của bạn.", "vat_tu", 0);
                query = query.Where(h => h.MaNhanVienTH == maNv);
            }
            if (maBt != null) query = query.Where(h => h.MaHoSoBaoTri == maBt);
            if (maSc != null) query = query.Where(h => h.MaHoSoSuaChua == maSc);

            var raw = await query.OrderByDescending(h => h.NgayThucHien).Take(200)
                .Select(h => new
                {
                    h.MaHoSoVatTu,
                    h.MaHoSoBaoTri,
                    h.MaHoSoSuaChua,
                    TenTb = h.TenThietBi ?? "",
                    h.TongTien,
                    h.TrangThai,
                    h.NgayThucHien,
                    SoLoai = h.ChiTietSuDungVatTus.Select(c => c.TenVatTu).Distinct().Count(),
                    TongSl = h.ChiTietSuDungVatTus.Sum(c => c.SoLuong)
                })
                .ToListAsync(ct);

            string? trangThai = null;
            if (Co(q, "xac nhan", "da xem")) trangThai = "Xác nhận";
            else if (Co(q, "cho duyet", "da gui")) trangThai = "Chờ duyệt";
            else if (Co(q, "cho gui")) trangThai = "Chờ gửi";
            else if (Co(q, "tu choi")) trangThai = "Từ chối";

            var list = raw.AsEnumerable();
            if (!string.IsNullOrEmpty(trangThai))
            {
                list = list.Where(h =>
                {
                    var t = h.TrangThai ?? "";
                    if (trangThai == "Xác nhận")
                        return t.Contains("Xác nhận", StringComparison.OrdinalIgnoreCase)
                               || t.Equals("Đã xem", StringComparison.OrdinalIgnoreCase);
                    if (trangThai == "Chờ duyệt")
                        return t.Contains("Chờ duyệt", StringComparison.OrdinalIgnoreCase)
                               || t.Equals("Đã gửi GĐ", StringComparison.OrdinalIgnoreCase);
                    return t.Contains(trangThai, StringComparison.OrdinalIgnoreCase);
                });
            }

            var arr = list.ToList();
            var theoTt = raw.GroupBy(x => x.TrangThai ?? "(trống)")
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();

            if (arr.Count == 0)
            {
                var msg = string.IsNullOrEmpty(trangThai)
                    ? "Dạ, chưa có hồ sơ vật tư trong phạm vi của bạn."
                    : $"Dạ, chưa có hồ sơ vật tư trạng thái **{trangThai}**.";
                if (theoTt.Count > 0)
                    msg += "\nPhân bố hiện có: " + string.Join(" · ", theoTt);
                return new KetQuaXuLy(true, msg, "vat_tu", 0);
            }

            var sb = new StringBuilder();
            sb.Append($"Dạ, có **{arr.Count}** hồ sơ vật tư"
                      + (trangThai != null ? $" (**{trangThai}**)" : "") + ".");
            if (theoTt.Count > 0 && trangThai == null)
                sb.Append("\nTheo trạng thái: " + string.Join(" · ", theoTt));
            foreach (var x in arr.Take(10))
            {
                var k = x.MaHoSoBaoTri != null ? $"BT#{x.MaHoSoBaoTri}"
                    : x.MaHoSoSuaChua != null ? $"SC#{x.MaHoSoSuaChua}"
                    : $"VT#{x.MaHoSoVatTu}";
                sb.AppendLine();
                sb.Append($"• {k} | {x.TenTb} | SL {x.TongSl} | {x.TongTien:N0} ₫ | {x.TrangThai} | {x.NgayThucHien:dd/MM/yyyy}");
            }
            if (arr.Count > 10)
            {
                sb.AppendLine();
                sb.Append($"… còn {arr.Count - 10} hồ sơ vật tư khác.");
            }

            return new KetQuaXuLy(true, sb.ToString().TrimEnd(), "vat_tu", arr.Count);
        }

        // ===================== VIỆC CỦA NHÂN VIÊN KỸ THUẬT =====================

        private async Task<KetQuaXuLy> TraLoiViecCuaToiAsync(
            int maNd, string q, CancellationToken ct)
        {
            var maNv = await _db.NhanViens.AsNoTracking()
                .Where(n => n.MaNguoiDung == maNd)
                .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
            if (maNv == null)
                return new KetQuaXuLy(true, "Dạ, mình chưa xác định được tài khoản nhân viên của bạn.", "chung", 0);

            var pcs = await _db.PhanCongCongViecs.AsNoTracking()
                .Where(p => p.MaNhanVienThucHien == maNv)
                .OrderByDescending(p => p.NgayPhanCong).Take(30)
                .Select(p => new
                {
                    p.MaPhanCong,
                    p.MaHoSoBaoTri,
                    p.MaHoSoSuaChua,
                    p.TrangThai,
                    p.NgayPhanCong
                })
                .ToListAsync(ct);

            if (pcs.Count == 0)
                return new KetQuaXuLy(true, "Dạ, hiện bạn chưa có phân công nào.", "chung", 0);

            var idsBt = pcs.Where(p => p.MaHoSoBaoTri != null).Select(p => p.MaHoSoBaoTri!.Value).Distinct().ToList();
            var idsSc = pcs.Where(p => p.MaHoSoSuaChua != null).Select(p => p.MaHoSoSuaChua!.Value).Distinct().ToList();

            var bts = await _db.HoSoBaoTris.AsNoTracking()
                .Where(h => idsBt.Contains(h.MaHoSoBaoTri))
                .Select(h => new { h.MaHoSoBaoTri, h.TrangThai })
                .ToListAsync(ct);
            var scs = await _db.HoSoSuaChuas.AsNoTracking()
                .Where(h => idsSc.Contains(h.MaHoSoSuaChua))
                .Select(h => new { h.MaHoSoSuaChua, h.TrangThai })
                .ToListAsync(ct);
            var dictBt = bts.ToDictionary(x => x.MaHoSoBaoTri, x => x.TrangThai);
            var dictSc = scs.ToDictionary(x => x.MaHoSoSuaChua, x => x.TrangThai);

            var active = pcs.Where(p =>
            {
                var ttHs = p.MaHoSoBaoTri != null && dictBt.TryGetValue(p.MaHoSoBaoTri.Value, out var b)
                    ? b
                    : p.MaHoSoSuaChua != null && dictSc.TryGetValue(p.MaHoSoSuaChua.Value, out var s)
                        ? s
                        : null;
                return ttHs is "Đang thực hiện" or "Chờ xác nhận";
            }).ToList();

            if (active.Count == 0)
                return new KetQuaXuLy(true, "Dạ, hiện bạn không có việc nào đang thực hiện.", "chung", 0);

            var tienDo = await LayTienDoDangThucHienAsync(ct);

            var sb = new StringBuilder();
            sb.AppendLine($"Dạ, bạn đang có **{active.Count}** việc:");
            foreach (var p in active.Take(8))
            {
                var ma = p.MaHoSoBaoTri != null ? $"BT#{p.MaHoSoBaoTri}" : $"SC#{p.MaHoSoSuaChua}";
                var tt = p.MaHoSoBaoTri != null
                    ? dictBt.GetValueOrDefault(p.MaHoSoBaoTri.Value, "—")
                    : dictSc.GetValueOrDefault(p.MaHoSoSuaChua!.Value, "—");
                var td = tienDo.FirstOrDefault(x => x.Ma == ma);
                sb.AppendLine($"• {ma} — {tt}" + (td.Ma != null ? $" — còn **{td.Con}**/{td.Tong} bước" : ""));
            }
            if (active.Count > 8)
                sb.AppendLine($"… còn {active.Count - 8} việc khác.");
            sb.AppendLine("Mình chỉ hiển thị việc được phân công cho bạn.");
            return new KetQuaXuLy(true, sb.ToString().TrimEnd(), "chung", active.Count);
        }
    }
}
