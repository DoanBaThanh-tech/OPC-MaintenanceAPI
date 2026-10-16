using Microsoft.EntityFrameworkCore;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class WorkOrderService
    {
        // ---------- từ XuLyThongKeGiamDoc.cs ----------

        public async Task<object> GetThongKeGiamDocAsync(int nam)
        {
            if (nam < 2026 || nam > 2100) nam = Math.Max(2026, DateTime.Now.Year);

            // Ngày dự kiến trên kế hoạch (xếp đúng tháng kế hoạch: 11, 12...)
            var mapNgayKeHoach = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.MaHoSoBaoTri != null)
                .Select(c => new { MaHoSo = c.MaHoSoBaoTri!.Value, c.NgayDuKienBaoTri })
                .ToListAsync();
            var dictKeHoach = mapNgayKeHoach
                .GroupBy(x => x.MaHoSo)
                .ToDictionary(g => g.Key, g => g.First().NgayDuKienBaoTri);

            // Hồ sơ bảo trì đã hoàn thành
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

            // Map mã HS BT -> (tháng, năm) thống kê
            var mapBtThang = btAll.ToDictionary(x => x.MaHoSoBaoTri, x => (x.Thang, x.Nam));

            // Hồ sơ sửa chữa đã hoàn thành
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

            // Vật tư: gắn theo hồ sơ BT/SC đã hoàn thành → cùng tháng với thiết bị đó
            // (không lấy theo NgayThucHien để tránh dồn hết về tháng bấm hoàn thành)
            var vtRaw = await _db.HoSoSuDungVatTus.AsNoTracking()
                .Include(h => h.ChiTietSuDungVatTus)
                .Where(h => h.TrangThai != "Đã hủy" && h.TrangThai != "Từ chối")
                .Select(h => new
                {
                    h.MaHoSoVatTu,
                    h.MaHoSoBaoTri,
                    h.MaHoSoSuaChua,
                    h.NgayThucHien,
                    h.TongTien,
                    ChiTiet = h.ChiTietSuDungVatTus.Select(c => new { c.SoLuong }).ToList()
                })
                .ToListAsync();

            var vtTheoThang = new List<(int Thang, int Nam, int SoLuong, decimal TongTien)>();
            foreach (var h in vtRaw)
            {
                int thang;
                int namHs;
                var ganVoiHsHoanThanh = false;

                if (h.MaHoSoBaoTri is int maBt && mapBtThang.TryGetValue(maBt, out var btT))
                {
                    thang = btT.Thang;
                    namHs = btT.Nam;
                    ganVoiHsHoanThanh = true;
                }
                else if (h.MaHoSoSuaChua is int maSc && mapScThang.TryGetValue(maSc, out var scT))
                {
                    thang = scT.Thang;
                    namHs = scT.Nam;
                    ganVoiHsHoanThanh = true;
                }
                else
                {
                    // Không gắn HS đã HT → bỏ qua (tránh đếm vật tư của hồ sơ chưa xong)
                    continue;
                }

                if (!ganVoiHsHoanThanh || namHs != nam) continue;

                var soLuong = h.ChiTiet.Sum(c => c.SoLuong);
                vtTheoThang.Add((thang, namHs, soLuong, h.TongTien));
            }

            var theoThang = new List<object>();
            for (var m = 1; m <= 12; m++)
            {
                var btThang = btAll.Where(x => x.Thang == m).ToList();
                var scThang = scAll.Where(x => x.Thang == m).ToList();
                var vtThang = vtTheoThang.Where(x => x.Thang == m).ToList();
                var soBt = btThang.Count;
                var soSc = scThang.Count;

                theoThang.Add(new
                {
                    Thang = m,
                    SoBaoTri = soBt,
                    SoBaoTriHoanThanh = soBt,
                    SoSuaChua = soSc,
                    SoSuaChuaHoanThanh = soSc,
                    SoLuongVatTu = vtThang.Sum(x => x.SoLuong),
                    TongTienVatTu = vtThang.Sum(x => x.TongTien)
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

            var namBt = btDone.Select(h =>
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

            var tongSlVt = vtTheoThang.Sum(x => x.SoLuong);
            var tongTienVt = vtTheoThang.Sum(x => x.TongTien);

            return new
            {
                Nam = nam,
                DanhSachNam = danhSachNam,
                TongBaoTri = btAll.Count,
                TongBaoTriHoanThanh = btAll.Count,
                TongSuaChua = scAll.Count,
                TongSuaChuaHoanThanh = scAll.Count,
                TongSoLuongVatTu = tongSlVt,
                TongTienVatTu = tongTienVt,
                TheoThang = theoThang
            };
        }
    }
}
