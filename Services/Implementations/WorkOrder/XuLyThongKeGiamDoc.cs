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

            // Năm hiển thị: từ 2026 trở đi; chỉ khi có kế hoạch BT năm đó
            // hoặc có hồ sơ sửa chữa đã hoàn thành trong năm đó (không hiện trước).
            const int namToiThieu = 2026;
            var namKeHoach = await _db.KeHoachBaoTris
                .Where(k => k.Nam >= namToiThieu)
                .Select(k => k.Nam)
                .Distinct()
                .ToListAsync();

            var namScHoanThanh = await _db.HoSoSuaChuas
                .Where(h => h.TrangThai == "Đã hoàn thành")
                .Select(h => (h.ThoiDiemKetThucThucTe ?? h.ThoiDiemBatDauThucTe ?? h.NgayDuyet ?? h.NgayTao).Year)
                .Where(y => y >= namToiThieu)
                .Distinct()
                .ToListAsync();

            var danhSachNam = namKeHoach
                .Concat(namScHoanThanh)
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            // Nếu chưa có năm nào đủ điều kiện nhưng đang xem năm hiện tại >= 2026
            // và năm đó đúng bằng nam đang query — vẫn trả list tối thiểu khi có data thống kê
            if (danhSachNam.Count == 0 && nam >= namToiThieu &&
                (btAll.Count > 0 || scAll.Count > 0))
            {
                danhSachNam.Add(nam);
            }

            // Nếu nam đang chọn không có trong list nhưng list không rỗng → giữ nguyên data năm đó
            // (client sẽ chỉnh dropdown). Không ép đổi nam server.

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
