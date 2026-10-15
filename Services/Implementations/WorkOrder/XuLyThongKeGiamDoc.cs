using Microsoft.EntityFrameworkCore;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class WorkOrderService
    {
        // ---------- từ XuLyThongKeGiamDoc.cs ----------

        public async Task<object> GetThongKeGiamDocAsync(int nam)
        {
            if (nam < 2026 || nam > 2100) nam = Math.Max(2026, DateTime.Now.Year);

            // Ngày dự kiến trên kế hoạch (để xếp đúng tháng kế hoạch: 11, 12...)
            var mapNgayKeHoach = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.MaHoSoBaoTri != null)
                .Select(c => new { MaHoSo = c.MaHoSoBaoTri!.Value, c.NgayDuKienBaoTri })
                .ToListAsync();
            var dictKeHoach = mapNgayKeHoach
                .GroupBy(x => x.MaHoSo)
                .ToDictionary(g => g.Key, g => g.First().NgayDuKienBaoTri);

            // Chỉ hồ sơ ĐÃ HOÀN THÀNH — không lấy hồ sơ đang làm / chờ duyệt
            var btDone = await _db.HoSoBaoTris.AsNoTracking()
                .Where(h => h.TrangThai == "Đã hoàn thành")
                .Select(h => new
                {
                    h.MaHoSoBaoTri,
                    NgayHt = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao
                })
                .ToListAsync();

            // Tháng trên biểu đồ = tháng kế hoạch (nếu có), không thì tháng hoàn thành thực tế.
            // Tránh trường hợp kế hoạch tháng 12 nhưng bấm xong vào tháng 10 → bị dồn hết tháng 10.
            var btAll = btDone.Select(h =>
            {
                if (dictKeHoach.TryGetValue(h.MaHoSoBaoTri, out var ngayKh))
                    return new { h.MaHoSoBaoTri, Thang = ngayKh.Month, Nam = ngayKh.Year };
                return new { h.MaHoSoBaoTri, Thang = h.NgayHt.Month, Nam = h.NgayHt.Year };
            }).Where(x => x.Nam == nam).ToList();

            var scDone = await _db.HoSoSuaChuas.AsNoTracking()
                .Where(h => h.TrangThai == "Đã hoàn thành")
                .Select(h => new
                {
                    h.MaHoSoSuaChua,
                    NgayHt = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao
                })
                .ToListAsync();

            // Sửa chữa không có kế hoạch năm → dùng tháng hoàn thành thực tế
            var scAll = scDone
                .Select(h => new { h.MaHoSoSuaChua, Thang = h.NgayHt.Month, Nam = h.NgayHt.Year })
                .Where(x => x.Nam == nam)
                .ToList();

            var vtAll = await _db.HoSoSuDungVatTus.AsNoTracking()
                .Include(h => h.ChiTietSuDungVatTus)
                .Where(h => h.NgayThucHien.Year == nam)
                .ToListAsync();

            var theoThang = new List<object>();
            for (var m = 1; m <= 12; m++)
            {
                var btThang = btAll.Where(x => x.Thang == m).ToList();
                var scThang = scAll.Where(x => x.Thang == m).ToList();
                var vtThang = vtAll.Where(h => h.NgayThucHien.Month == m).ToList();
                var soBt = btThang.Count;
                var soSc = scThang.Count;

                theoThang.Add(new
                {
                    Thang = m,
                    SoBaoTri = soBt,
                    SoBaoTriHoanThanh = soBt,
                    SoSuaChua = soSc,
                    SoSuaChuaHoanThanh = soSc,
                    SoLuongVatTu = vtThang.SelectMany(h => h.ChiTietSuDungVatTus).Sum(c => c.SoLuong),
                    TongTienVatTu = vtThang.Sum(h => h.TongTien)
                });
            }

            const int namToiThieu = 2026;
            var namKeHoach = await _db.KeHoachBaoTris.AsNoTracking()
                .Where(k => k.Nam >= namToiThieu)
                .Select(k => k.Nam)
                .Distinct()
                .ToListAsync();

            var namChiTiet = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.NgayDuKienBaoTri.Year >= namToiThieu)
                .Select(c => c.NgayDuKienBaoTri.Year)
                .Distinct()
                .ToListAsync();

            var namBt = btAll.Select(x => x.Nam).Where(y => y >= namToiThieu).Distinct().ToList();
            // cả BT hoàn thành ở năm khác (nếu dict lệch) — bổ sung từ btDone đã map
            namBt = btDone.Select(h =>
            {
                if (dictKeHoach.TryGetValue(h.MaHoSoBaoTri, out var ngayKh))
                    return ngayKh.Year;
                return h.NgayHt.Year;
            }).Where(y => y >= namToiThieu).Distinct().ToList();

            var namSc = scDone.Select(h => h.NgayHt.Year)
                .Where(y => y >= namToiThieu).Distinct().ToList();

            var danhSachNam = namKeHoach
                .Concat(namChiTiet)
                .Concat(namBt)
                .Concat(namSc)
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            if (danhSachNam.Count == 0 && nam >= namToiThieu &&
                (btAll.Count > 0 || scAll.Count > 0))
            {
                danhSachNam.Add(nam);
            }

            return new
            {
                Nam = nam,
                DanhSachNam = danhSachNam,
                TongBaoTri = btAll.Count,
                TongBaoTriHoanThanh = btAll.Count,
                TongSuaChua = scAll.Count,
                TongSuaChuaHoanThanh = scAll.Count,
                TongSoLuongVatTu = vtAll.SelectMany(h => h.ChiTietSuDungVatTus).Sum(c => c.SoLuong),
                TongTienVatTu = vtAll.Sum(h => h.TongTien),
                TheoThang = theoThang
            };
        }
    }
}
