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
            if (nam < 2000 || nam > 2100) nam = DateTime.Now.Year;

            // Bảo trì: theo tháng hoàn thành thực tế, không thì ngày dự kiến, không thì ngày tạo
            var btAll = await _db.HoSoBaoTris
                .Where(h => h.TrangThai != "Nháp" && h.TrangThai != "Đã hủy")
                .Select(h => new
                {
                    h.MaHoSoBaoTri,
                    h.TrangThai,
                    Thang = (h.ThoiDiemKetThucThucTe ?? h.ThoiDiemBatDauThucTe ?? h.NgayDuyet ?? h.NgayTao).Month,
                    Nam = (h.ThoiDiemKetThucThucTe ?? h.ThoiDiemBatDauThucTe ?? h.NgayDuyet ?? h.NgayTao).Year
                })
                .Where(x => x.Nam == nam)
                .ToListAsync();

            var scAll = await _db.HoSoSuaChuas
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

            // Vật tư: theo ngày thực hiện (NgayThucHien không nullable)
            var vtAll = await _db.HoSoSuDungVatTus
                .Include(h => h.ChiTietSuDungVatTus)
                .Where(h => h.NgayThucHien.Year == nam)
                .ToListAsync();

            var theoThang = new List<object>();
            for (var m = 1; m <= 12; m++)
            {
                var btThang = btAll.Where(x => x.Thang == m).ToList();
                var scThang = scAll.Where(x => x.Thang == m).ToList();
                var vtThang = vtAll.Where(h => h.NgayThucHien.Month == m).ToList();

                var soLuongVt = vtThang.SelectMany(h => h.ChiTietSuDungVatTus).Sum(c => c.SoLuong);
                var tongTien = vtThang.Sum(h => h.TongTien);

                theoThang.Add(new
                {
                    Thang = m,
                    SoBaoTri = btThang.Count,
                    SoBaoTriHoanThanh = btThang.Count(x => x.TrangThai == "Đã hoàn thành"),
                    SoSuaChua = scThang.Count,
                    SoSuaChuaHoanThanh = scThang.Count(x => x.TrangThai == "Đã hoàn thành"),
                    SoLuongVatTu = soLuongVt,
                    TongTienVatTu = tongTien
                });
            }

            return new
            {
                Nam = nam,
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
