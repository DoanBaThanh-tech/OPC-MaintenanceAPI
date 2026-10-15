using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Core.Exceptions;
using OPC.MaintenanceAPI.DTOs.WorkOrder;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;
using OPC.MaintenanceAPI.DTOs.Common;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class WorkOrderService
    {

        // ---------- từ XuLyThongKeGiamDoc.cs ----------

        public async Task<object> GetThongKeGiamDocAsync(int nam)
        {
            if (nam < 2026 || nam > 2100) nam = Math.Max(2026, DateTime.Now.Year);

            // Map MaHoSoBaoTri -> ngày dự kiến trên kế hoạch (nếu có)
            var mapNgayKeHoach = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Where(c => c.MaHoSoBaoTri != null)
                .Select(c => new { MaHoSo = c.MaHoSoBaoTri!.Value, c.NgayDuKienBaoTri })
                .ToListAsync();
            var dictKeHoach = mapNgayKeHoach
                .GroupBy(x => x.MaHoSo)
                .ToDictionary(g => g.Key, g => g.First().NgayDuKienBaoTri);

            var btEntities = await _db.HoSoBaoTris.AsNoTracking()
                .Where(h => h.TrangThai != "Nháp" && h.TrangThai != "Đã hủy")
                .Select(h => new
                {
                    h.MaHoSoBaoTri,
                    h.TrangThai,
                    NgayThucTe = h.ThoiDiemKetThucThucTe ?? h.ThoiDiemBatDauThucTe ?? h.NgayDuyet ?? h.NgayTao
                })
                .ToListAsync();

            var btAll = btEntities.Select(h =>
            {
                if (dictKeHoach.TryGetValue(h.MaHoSoBaoTri, out var ngayKh))
                {
                    return new { h.MaHoSoBaoTri, h.TrangThai, Thang = ngayKh.Month, Nam = ngayKh.Year };
                }
                return new { h.MaHoSoBaoTri, h.TrangThai, Thang = h.NgayThucTe.Month, Nam = h.NgayThucTe.Year };
            }).Where(x => x.Nam == nam).ToList();

            var scAll = await _db.HoSoSuaChuas.AsNoTracking()
                .Where(h => h.TrangThai != "Nháp" && h.TrangThai != "Đã hủy")
                .Select(h => new
                {
                    h.MaHoSoSuaChua,
                    h.TrangThai,
                    Thang = (h.ThoiDiemKetThucThucTe ?? h.ThoiDiemBatDauThucTe ?? h.NgayDuyet ?? h.NgayTao).Month,
                    Nam = (h.ThoiDiemKetThucThucTe ?? h.ThoiDiemBatDauThucTe ?? h.NgayDuyet ?? h.NgayTao).Year
                })
                .Where(x => x.Nam == nam)
                .ToListAsync();

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

                theoThang.Add(new
                {
                    Thang = m,
                    SoBaoTri = btThang.Count,
                    SoBaoTriHoanThanh = btThang.Count(x => x.TrangThai == "Đã hoàn thành"),
                    SoSuaChua = scThang.Count,
                    SoSuaChuaHoanThanh = scThang.Count(x => x.TrangThai == "Đã hoàn thành"),
                    SoLuongVatTu = vtThang.SelectMany(h => h.ChiTietSuDungVatTus).Sum(c => c.SoLuong),
                    TongTienVatTu = vtThang.Sum(h => h.TongTien)
                });
            }

            // Năm dropdown (>= 2026): kế hoạch BT, chi tiết ngày dự kiến, hồ sơ BT, SC hoàn thành
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

            var namBt = btEntities.Select(h =>
            {
                if (dictKeHoach.TryGetValue(h.MaHoSoBaoTri, out var ngayKh))
                    return ngayKh.Year;
                return h.NgayThucTe.Year;
            }).Where(y => y >= namToiThieu).Distinct().ToList();

            var namScHoanThanh = await _db.HoSoSuaChuas.AsNoTracking()
                .Where(h => h.TrangThai == "Đã hoàn thành")
                .Select(h => (h.ThoiDiemKetThucThucTe ?? h.ThoiDiemBatDauThucTe ?? h.NgayDuyet ?? h.NgayTao).Year)
                .Where(y => y >= namToiThieu)
                .Distinct()
                .ToListAsync();

            var danhSachNam = namKeHoach
                .Concat(namChiTiet)
                .Concat(namBt)
                .Concat(namScHoanThanh)
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
                TongBaoTriHoanThanh = btAll.Count(x => x.TrangThai == "Đã hoàn thành"),
                TongSuaChua = scAll.Count,
                TongSuaChuaHoanThanh = scAll.Count(x => x.TrangThai == "Đã hoàn thành"),
                TongSoLuongVatTu = vtAll.SelectMany(h => h.ChiTietSuDungVatTus).Sum(c => c.SoLuong),
                TongTienVatTu = vtAll.Sum(h => h.TongTien),
                TheoThang = theoThang
            };
        }

    }
}
