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

            // --- Vật tư: CHỈ sau khi Giám đốc xác nhận ---
            // Trạng thái sau XacNhanHoSoVatTuAsync = "Xác nhận" (và biến thể)
            var trangThaiGdXacNhan = new[] { "Xác nhận", "Đã xác nhận", "Đã duyệt", "Xac nhan", "Da duyet" };
            var vtRaw = await _db.HoSoSuDungVatTus.AsNoTracking()
                .Where(h => trangThaiGdXacNhan.Contains(h.TrangThai))
                .Select(h => new
                {
                    h.MaHoSoVatTu,
                    h.MaHoSoBaoTri,
                    h.MaHoSoSuaChua,
                    h.NgayThucHien,
                    h.NgayGuiGiamDoc,
                    h.TongTien
                })
                .ToListAsync();

            var maVtIds = vtRaw.Select(x => x.MaHoSoVatTu).ToList();
            var chiTietSl = await _db.ChiTietSuDungVatTus.AsNoTracking()
                .Where(c => maVtIds.Contains(c.MaHoSoVatTu))
                .GroupBy(c => c.MaHoSoVatTu)
                .Select(g => new { MaHoSoVatTu = g.Key, SoLuong = g.Sum(x => x.SoLuong) })
                .ToListAsync();
            var dictSl = chiTietSl.ToDictionary(x => x.MaHoSoVatTu, x => x.SoLuong);

            var vtTheoThang = new List<(int Thang, int Nam, int SoLuong, decimal TongTien)>();
            foreach (var h in vtRaw)
            {
                int thang;
                int namHs;
                var coThang = false;

                // Ưu tiên tháng của HS BT/SC đã hoàn thành (cùng thiết bị/quy trình)
                if (h.MaHoSoBaoTri is int maBt && mapBtThang.TryGetValue(maBt, out var btT))
                {
                    thang = btT.Thang;
                    namHs = btT.Nam;
                    coThang = true;
                }
                else if (h.MaHoSoSuaChua is int maSc && mapScThang.TryGetValue(maSc, out var scT))
                {
                    thang = scT.Thang;
                    namHs = scT.Nam;
                    coThang = true;
                }
                else if (h.MaHoSoBaoTri is int maBt2)
                {
                    // HS chưa nằm trong map năm đang xem — lấy năm/tháng hoàn thành thực tế nếu HS đã HT
                    var bt = btDone.FirstOrDefault(x => x.MaHoSoBaoTri == maBt2);
                    if (bt != null)
                    {
                        thang = bt.NgayHt.Month;
                        namHs = bt.NgayHt.Year;
                        coThang = true;
                    }
                    else continue; // quy trình chưa hoàn thành → chưa đưa VT vào TK (theo yêu cầu)
                }
                else if (h.MaHoSoSuaChua is int maSc2)
                {
                    var sc = scDone.FirstOrDefault(x => x.MaHoSoSuaChua == maSc2);
                    if (sc != null)
                    {
                        thang = sc.NgayHt.Month;
                        namHs = sc.NgayHt.Year;
                        coThang = true;
                    }
                    else continue;
                }
                else
                {
                    // Không gắn HS → dùng ngày gửi GĐ / thực hiện
                    var ngay = h.NgayGuiGiamDoc ?? h.NgayThucHien;
                    thang = ngay.Month;
                    namHs = ngay.Year;
                    coThang = true;
                }

                if (!coThang || namHs != nam) continue;

                var soLuong = dictSl.TryGetValue(h.MaHoSoVatTu, out var sl) ? sl : 0;
                vtTheoThang.Add((thang, namHs, soLuong, h.TongTien));
            }

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
