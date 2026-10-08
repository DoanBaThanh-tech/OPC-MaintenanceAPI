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

        // ---------- từ XuLyKeHoachVaTienDoBuoc.cs ----------

        // ===== KẾ HOẠCH BƯỚC (TỔ TRƯỞNG CHỌN TRƯỚC PHÂN CÔNG) =====

        public async Task<(bool, string?)> LuuKeHoachBuocAsync(int maNguoiDung, KeHoachBuocDto dto)
        {
            if (dto == null)
                return (false, "Thiếu dữ liệu.");
            if (!dto.MaHoSoBaoTri.HasValue && !dto.MaHoSoSuaChua.HasValue)
                return (false, "Thiếu mã hồ sơ.");

            var ds = (dto.DanhSachBuoc ?? new List<KeHoachBuocItemDto>())
                .Where(b => b.SoBuoc > 0)
                .GroupBy(b => b.SoBuoc)
                .Select(g => g.First())
                .OrderBy(b => b.SoBuoc)
                .ToList();

            if (ds.Count == 0)
                return (false, "Vui lòng tích chọn ít nhất một bước quy trình trước khi Lưu.");

            // Chỉ cho lưu khi hồ sơ ở trạng thái phù hợp (đã duyệt / chờ PC / đang TH)
            if (dto.MaHoSoBaoTri.HasValue)
            {
                var hs = await _repo.GetHoSoBaoTriByIdAsync(dto.MaHoSoBaoTri.Value);
                if (hs == null) return (false, "Không tìm thấy hồ sơ bảo trì.");
                var tt = (hs.TrangThai ?? "").Trim();
                if (tt is not ("Đã duyệt" or "Đang thực hiện" or "Chờ phân công"))
                    return (false, $"Chỉ chọn bước khi hồ sơ đã được duyệt. Trạng thái: «{tt}».");
                if (hs.ThoiDiemBatDauThucTe != null)
                    return (false,
                        "Nhân viên kỹ thuật đã tiến hành quy trình — không thể chỉnh sửa bước quy trình.");
            }
            else
            {
                var hs = await _repo.GetHoSoSuaChuaByIdAsync(dto.MaHoSoSuaChua!.Value);
                if (hs == null) return (false, "Không tìm thấy hồ sơ sửa chữa.");
                var tt = (hs.TrangThai ?? "").Trim();
                if (tt is not ("Đã duyệt" or "Đang thực hiện" or "Chờ phân công"))
                    return (false, $"Chỉ chọn bước khi hồ sơ đã được duyệt. Trạng thái: «{tt}».");
                if (hs.ThoiDiemBatDauThucTe != null)
                    return (false,
                        "Nhân viên kỹ thuật đã tiến hành quy trình — không thể chỉnh sửa bước quy trình.");
            }

            // NVKT đã nhận / xong bước → Tổ trưởng không được đổi kế hoạch bước
            var coTienDoNvkt = await _db.TienDoBuocQuyTrinhs.AnyAsync(t =>
                (dto.MaHoSoBaoTri.HasValue
                    ? t.MaHoSoBaoTri == dto.MaHoSoBaoTri
                    : t.MaHoSoSuaChua == dto.MaHoSoSuaChua)
                && t.TrangThai != "DuocChon"
                && t.MaNhanVien > 0);
            if (coTienDoNvkt)
                return (false,
                    "Nhân viên kỹ thuật đã bắt đầu thực hiện bước — không thể chỉnh sửa bước quy trình.");

            // Xóa các bước DuocChon cũ (không đụng DaXong / DangLam / DaCapNhat)
            IQueryable<TienDoBuocQuyTrinh> qCu = _db.TienDoBuocQuyTrinhs
                .Where(t => t.TrangThai == "DuocChon");
            if (dto.MaHoSoBaoTri.HasValue)
                qCu = qCu.Where(t => t.MaHoSoBaoTri == dto.MaHoSoBaoTri);
            else
                qCu = qCu.Where(t => t.MaHoSoSuaChua == dto.MaHoSoSuaChua);

            var cu = await qCu.ToListAsync();
            if (cu.Count > 0)
                _db.TienDoBuocQuyTrinhs.RemoveRange(cu);

            // Không ghi đè bước NVKT đã làm
            HashSet<int> buocDaCoThat;
            if (dto.MaHoSoBaoTri.HasValue)
            {
                buocDaCoThat = (await _db.TienDoBuocQuyTrinhs
                        .Where(t => t.MaHoSoBaoTri == dto.MaHoSoBaoTri && t.TrangThai != "DuocChon")
                        .Select(t => t.SoBuoc)
                        .ToListAsync())
                    .ToHashSet();
            }
            else
            {
                buocDaCoThat = (await _db.TienDoBuocQuyTrinhs
                        .Where(t => t.MaHoSoSuaChua == dto.MaHoSoSuaChua && t.TrangThai != "DuocChon")
                        .Select(t => t.SoBuoc)
                        .ToListAsync())
                    .ToHashSet();
            }

            foreach (var b in ds)
            {
                if (buocDaCoThat.Contains(b.SoBuoc)) continue; // giữ nguyên tiến độ NVKT
                _db.TienDoBuocQuyTrinhs.Add(new TienDoBuocQuyTrinh
                {
                    MaHoSoBaoTri = dto.MaHoSoBaoTri,
                    MaHoSoSuaChua = dto.MaHoSoSuaChua,
                    SoBuoc = b.SoBuoc,
                    MoTaBuoc = b.MoTaBuoc ?? "",
                    MaNhanVien = 0, // Tổ trưởng không phải người thực hiện
                    TenNhanVien = null,
                    TrangThai = "DuocChon",
                    JsonVatTu = null,
                    ThoiDiemCapNhat = DateTime.Now
                });
            }

            await _db.SaveChangesAsync();
            return (true, null);
        }

        // ===== TIẾN ĐỘ BƯỚC QUY TRÌNH (NVKT) =====

        public async Task<List<object>> GetTienDoBuocAsync(int? maHoSoBaoTri, int? maHoSoSuaChua)
        {
            var q = _db.TienDoBuocQuyTrinhs.AsQueryable();
            if (maHoSoBaoTri.HasValue)
                q = q.Where(t => t.MaHoSoBaoTri == maHoSoBaoTri);
            else if (maHoSoSuaChua.HasValue)
                q = q.Where(t => t.MaHoSoSuaChua == maHoSoSuaChua);
            else
                return new List<object>();

            return await q.OrderBy(t => t.SoBuoc)
                .Select(t => (object)new
                {
                    t.MaTienDo,
                    t.MaHoSoBaoTri,
                    t.MaHoSoSuaChua,
                    t.SoBuoc,
                    t.MoTaBuoc,
                    t.MaNhanVien,
                    t.TenNhanVien,
                    t.TrangThai,
                    t.JsonVatTu,
                    t.ThoiDiemCapNhat
                })
                .ToListAsync();
        }

        public async Task<(bool, string?, object?)> ClaimHoacLuuTienDoBuocAsync(int maNguoiDung, TienDoBuocDto dto)
        {
            if (!dto.MaHoSoBaoTri.HasValue && !dto.MaHoSoSuaChua.HasValue)
                return (false, "Thiếu mã hồ sơ.", null);
            if (dto.SoBuoc <= 0)
                return (false, "Số bước không hợp lệ.", null);

            var nv = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nv == null) return (false, "Không xác định được nhân viên.", null);

            var trangThai = (dto.TrangThai ?? "DangLam").Trim();
            // DangLam | DaXong | DaCapNhat | BoChon (hủy bước về chưa chọn)
            if (trangThai is not ("DangLam" or "DaXong" or "DaCapNhat" or "BoChon"))
                trangThai = "DangLam";

            TienDoBuocQuyTrinh? existing = null;
            if (dto.MaHoSoBaoTri.HasValue)
            {
                existing = await _db.TienDoBuocQuyTrinhs
                    .FirstOrDefaultAsync(t => t.MaHoSoBaoTri == dto.MaHoSoBaoTri && t.SoBuoc == dto.SoBuoc);
            }
            else
            {
                existing = await _db.TienDoBuocQuyTrinhs
                    .FirstOrDefaultAsync(t => t.MaHoSoSuaChua == dto.MaHoSoSuaChua && t.SoBuoc == dto.SoBuoc);
            }

            // Bỏ chọn bước → xóa tiến độ (về trạng thái chưa tích)
            if (trangThai == "BoChon")
            {
                if (existing != null)
                {
                    // Không cho xóa bước người khác đã Xong
                    if (existing.MaNhanVien > 0 && existing.MaNhanVien != nv.MaNhanVien
                        && existing.TrangThai is "DaXong" or "DaCapNhat")
                    {
                        var ten = string.IsNullOrWhiteSpace(existing.TenNhanVien)
                            ? "nhân viên khác"
                            : existing.TenNhanVien;
                        return (false, $"Không thể bỏ bước {dto.SoBuoc} — do {ten} phụ trách.", null);
                    }
                    _db.TienDoBuocQuyTrinhs.Remove(existing);
                    await _db.SaveChangesAsync();
                }
                return (true, null, new { SoBuoc = dto.SoBuoc, TrangThai = "BoChon" });
            }

            if (existing != null)
            {
                // Bước Tổ trưởng chọn sẵn (DuocChon) — NVKT nhận làm bình thường, chưa hiện tên TT
                var laKeHoachToTruong = existing.TrangThai == "DuocChon" || existing.MaNhanVien <= 0;

                var daHoanTatBuoc = existing.TrangThai is "DaXong" or "DaCapNhat";
                // Chỉ khóa khi người khác đã Xong / đã cập nhật sau từ chối
                if (!laKeHoachToTruong && existing.MaNhanVien != nv.MaNhanVien && daHoanTatBuoc)
                {
                    var ten = string.IsNullOrWhiteSpace(existing.TenNhanVien)
                        ? "nhân viên khác"
                        : existing.TenNhanVien;
                    return (false, $"Bước {dto.SoBuoc} đã được hoàn thành bởi {ten}.", null);
                }

                // Cùng người / DuocChon / ghi đè DangLam — cập nhật đầy đủ
                existing.MaNhanVien = nv.MaNhanVien;
                existing.TenNhanVien = nv.HoTen;
                existing.MoTaBuoc = dto.MoTaBuoc ?? existing.MoTaBuoc;
                existing.TrangThai = trangThai;
                if (dto.JsonVatTu != null)
                    existing.JsonVatTu = dto.JsonVatTu;
                existing.ThoiDiemCapNhat = DateTime.Now;
                await _db.SaveChangesAsync();
                return (true, null, new
                {
                    existing.MaTienDo,
                    existing.SoBuoc,
                    existing.MaNhanVien,
                    existing.TenNhanVien,
                    existing.TrangThai,
                    existing.JsonVatTu
                });
            }

            var row = new TienDoBuocQuyTrinh
            {
                MaHoSoBaoTri = dto.MaHoSoBaoTri,
                MaHoSoSuaChua = dto.MaHoSoSuaChua,
                SoBuoc = dto.SoBuoc,
                MoTaBuoc = dto.MoTaBuoc ?? "",
                MaNhanVien = nv.MaNhanVien,
                TenNhanVien = nv.HoTen,
                TrangThai = trangThai,
                JsonVatTu = dto.JsonVatTu,
                ThoiDiemCapNhat = DateTime.Now
            };
            _db.TienDoBuocQuyTrinhs.Add(row);
            await _db.SaveChangesAsync();
            return (true, null, new
            {
                row.MaTienDo,
                row.SoBuoc,
                row.MaNhanVien,
                row.TenNhanVien,
                row.TrangThai
            });
        }
    }
}
