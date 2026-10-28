using Microsoft.EntityFrameworkCore;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class WorkOrderService
    {
        /// <summary>
        /// Thống kê GĐ: số HS BT/SC Đã hoàn thành (sau Xưởng xác nhận quy trình) hiện trước;
        /// số lượng &amp; tổng tiền vật tư chỉ khi GĐ đã xác nhận hồ sơ vật tư.
        /// </summary>
        public async Task<object> GetThongKeGiamDocAsync(int nam)
        {
            if (nam < 2026 || nam > 2100) nam = Math.Max(2026, DateTime.Now.Year);

            var mapNgayKeHoach = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.MaHoSoBaoTri != null)
                .OrderBy(c => c.MaChiTietKeHoach)
                .Select(c => new { MaHoSo = c.MaHoSoBaoTri!.Value, c.NgayDuKienBaoTri })
                .ToListAsync();
            var dictKeHoach = mapNgayKeHoach
                .GroupBy(x => x.MaHoSo)
                .ToDictionary(g => g.Key, g => g.First().NgayDuKienBaoTri);

            // --- HS BT/SC đã hoàn thành (Xưởng xác nhận quy trình xong) ---
            var btDone = await _db.HoSoBaoTris.AsNoTracking()
                .Where(h => h.TrangThai == "Đã hoàn thành")
                .Select(h => new
                {
                    h.MaHoSoBaoTri,
                    NgayHt = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao
                })
                .ToListAsync();

            var btAll = btDone.Select(h =>
            {
                if (dictKeHoach.TryGetValue(h.MaHoSoBaoTri, out var ngayKh))
                    return new { h.MaHoSoBaoTri, Thang = ngayKh.Month, Nam = ngayKh.Year };
                return new { h.MaHoSoBaoTri, Thang = h.NgayHt.Month, Nam = h.NgayHt.Year };
            }).Where(x => x.Nam == nam).ToList();

            var mapBtThang = btAll.ToDictionary(x => x.MaHoSoBaoTri, x => (x.Thang, x.Nam));

            var scDone = await _db.HoSoSuaChuas.AsNoTracking()
                .Where(h => h.TrangThai == "Đã hoàn thành")
                .Select(h => new
                {
                    h.MaHoSoSuaChua,
                    NgayHt = h.ThoiDiemKetThucThucTe ?? h.NgayDuyet ?? h.NgayTao
                })
                .ToListAsync();

            var scAll = scDone
                .Select(h => new { h.MaHoSoSuaChua, Thang = h.NgayHt.Month, Nam = h.NgayHt.Year })
                .Where(x => x.Nam == nam)
                .ToList();

            var mapScThang = scAll.ToDictionary(x => x.MaHoSoSuaChua, x => (x.Thang, x.Nam));

            // --- Vật tư: cùng nguồn và mốc ngày với chatbot; giữ quy tắc phụ thuộc năm của app ---
            var vtTheoNgay = await ThongKeVatTuDongBo.LayTheoNgayAsync(_db, nam);
            var vtTheoThang = vtTheoNgay.Select(x => new
            {
                Thang = x.Ngay.Month,
                Nam = x.Ngay.Year,
                SoLuong = checked((long)x.SoLuong),
                x.TongTien
            }).ToList();

            var theoThang = new List<object>();
            for (var m = 1; m <= 12; m++)
            {
                var btThang = btAll.Where(x => x.Thang == m).ToList();
                var scThang = scAll.Where(x => x.Thang == m).ToList();
                var vtThang = vtTheoThang.Where(x => x.Thang == m).ToList();
                theoThang.Add(new
                {
                    Thang = m,
                    SoBaoTri = btThang.Count,
                    SoBaoTriHoanThanh = btThang.Count,
                    SoSuaChua = scThang.Count,
                    SoSuaChuaHoanThanh = scThang.Count,
                    SoLuongVatTu = vtThang.Sum(x => x.SoLuong),
                    TongTienVatTu = vtThang.Sum(x => x.TongTien)
                });
            }

            const int namToiThieu = 2026;
            var namKeHoach = await _db.KeHoachBaoTris.AsNoTracking()
                .Where(k => k.Nam >= namToiThieu).Select(k => k.Nam).Distinct().ToListAsync();
            var namChiTiet = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.NgayDuKienBaoTri.Year >= namToiThieu)
                .Select(c => c.NgayDuKienBaoTri.Year).Distinct().ToListAsync();
            var namBt = btDone.Select(h =>
                dictKeHoach.TryGetValue(h.MaHoSoBaoTri, out var nk) ? nk.Year : h.NgayHt.Year)
                .Where(y => y >= namToiThieu).Distinct().ToList();
            var namSc = scDone.Select(h => h.NgayHt.Year).Where(y => y >= namToiThieu).Distinct().ToList();
            var namVt = vtTheoThang.Select(x => x.Nam).Where(y => y >= namToiThieu).Distinct().ToList();

            var danhSachNam = namKeHoach.Concat(namChiTiet).Concat(namBt).Concat(namSc).Concat(namVt)
                .Distinct().OrderBy(y => y).ToList();
            if (danhSachNam.Count == 0 && nam >= namToiThieu &&
                (btAll.Count > 0 || scAll.Count > 0 || vtTheoThang.Count > 0))
                danhSachNam.Add(nam);

            return new
            {
                Nam = nam,
                DanhSachNam = danhSachNam,
                TongBaoTri = btAll.Count,
                TongBaoTriHoanThanh = btAll.Count,
                TongSuaChua = scAll.Count,
                TongSuaChuaHoanThanh = scAll.Count,
                TongSoLuongVatTu = vtTheoThang.Sum(x => x.SoLuong),
                TongTienVatTu = vtTheoThang.Sum(x => x.TongTien),
                TheoThang = theoThang
            };
        }
    }
}
