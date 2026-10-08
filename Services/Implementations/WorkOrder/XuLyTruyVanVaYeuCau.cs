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


        // Danh sách năm THẬT SỰ có hồ sơ ở trạng thái này — dùng để đổ vào bộ lọc, không phải dải năm cố định
        public async Task<List<int>> GetCacNamCoHoSoBaoTriAsync(string? trangThai)
        {
            var list = await LayDanhSachKemNamAsync(trangThai);
            return list.Select(x => x.nam).Distinct().OrderByDescending(n => n).ToList();
        }

        public async Task<object?> GetHoSoBaoTriByIdAsync(int id)
        {
            var h = await _repo.GetHoSoBaoTriByIdAsync(id);
            if (h == null) return null;
            var namTuKeHoach = await _repo.GetNamKeHoachTheoHoSoBaoTriAsync(h.MaHoSoBaoTri);
            var ngayDuKien = await _repo.GetNgayDuKienBaoTriTheoHoSoBaoTriAsync(h.MaHoSoBaoTri);
            static string? FmtGio(TimeSpan? t) =>
                t == null ? null : $"{(int)t.Value.TotalHours:D2}:{t.Value.Minutes:D2}";

            var pc = h.MaPhanCongNavigation;
            // Nếu Include chưa load NV thực hiện — lấy lại đầy đủ
            if (pc != null && pc.MaNhanVienThucHienNavigation == null && h.MaPhanCong.HasValue)
                pc = await _repo.GetPhanCongByIdAsync(h.MaPhanCong.Value) ?? pc;

            // Tất cả NV phân công (trừ đã hủy) — luôn hiển thị đủ người
            var dsPc = await _repo.GetPhanCongTheoHoSoBaoTriAsync(h.MaHoSoBaoTri);
            var dsNvDangPc = dsPc
                .Where(p => p.TrangThai != "Đã hủy")
                .GroupBy(p => p.MaNhanVienThucHien)
                .Select(g => g.OrderByDescending(x => x.MaPhanCong).First())
                .Select(p => new
                {
                    MaNhanVien = p.MaNhanVienThucHien,
                    TenNhanVien = p.MaNhanVienThucHienNavigation?.HoTen,
                    p.TrangThai,
                    p.MaPhanCong
                })
                .ToList();

            var dsNvHoanThanh = dsPc
                .Where(p => p.TrangThai == "Hoàn thành")
                .Select(p => new
                {
                    MaNhanVien = p.MaNhanVienThucHien,
                    TenNhanVien = p.MaNhanVienThucHienNavigation?.HoTen,
                    p.TrangThai,
                    p.MaPhanCong
                })
                .ToList();

            var tenNvHoanThanh = dsNvHoanThanh.Count == 0
                ? null
                : string.Join(", ", dsNvHoanThanh.Select(x => x.TenNhanVien).Where(t => !string.IsNullOrEmpty(t)));

            // Quy trình đã chọn lúc tạo / Tổ trưởng tích (DuocChon + tiến độ NVKT)
            var dsBuoc = await _db.TienDoBuocQuyTrinhs
                .Where(t => t.MaHoSoBaoTri == h.MaHoSoBaoTri)
                .OrderBy(t => t.SoBuoc)
                .Select(t => new
                {
                    t.SoBuoc,
                    t.MoTaBuoc,
                    t.TrangThai,
                    t.TenNhanVien
                })
                .ToListAsync();

            return new
            {
                h.MaHoSoBaoTri,
                MaThietBi = h.MaThieBi,
                TenThietBi = h.MaThieBiNavigation?.TenThietBi,
                TenNhanVienTao = h.MaNhanVienTaoNavigation?.HoTen,
                h.NoiDungCongViec,
                h.ThoiGianDuKien,
                GioBatDauDuKien = FmtGio(h.GioBatDauDuKien),
                GioKetThucDuKien = FmtGio(h.GioKetThucDuKien),
                ThoiDiemBatDauThucTe = h.ThoiDiemBatDauThucTe,
                ThoiDiemKetThucThucTe = h.ThoiDiemKetThucThucTe,
                h.TrangThai,
                h.LyDoTuChoi,
                h.NgayTao,
                h.NgayDuyet,
                h.MaPhanCong,
                // Ưu tiên PC Chờ xác nhận — Xưởng thấy nút Xác nhận / Từ chối
                TrangThaiPhanCong = dsPc.FirstOrDefault(p => p.TrangThai == "Chờ xác nhận")?.TrangThai
                    ?? pc?.TrangThai,
                LyDoTuChoiPhanCong = dsPc.FirstOrDefault(p => p.TrangThai == "Chờ xác nhận")?.LyDoTuChoi
                    ?? pc?.LyDoTuChoi,
                MaNhanVienThucHien = pc?.MaNhanVienThucHien,
                TenNhanVienThucHien = pc?.MaNhanVienThucHienNavigation?.HoTen,
                NgayPhanCong = pc?.NgayPhanCong,
                ChoXuongXacNhanQuyTrinh = (h.TrangThai is "Đang thực hiện" or "Chờ xác nhận")
                    && dsPc.Any(p => p.TrangThai == "Chờ xác nhận"),
                // Nhiều NV đang phân công
                DanhSachNhanVienPhanCong = dsNvDangPc,
                MaNhanVienThucHiens = dsNvDangPc.Select(x => x.MaNhanVien).ToList(),
                TenNhanVienThucHiens = string.Join(", ", dsNvDangPc.Select(x => x.TenNhanVien).Where(t => !string.IsNullOrEmpty(t))),
                // NV đã hoàn thành
                DanhSachNhanVienHoanThanh = dsNvHoanThanh,
                MaNhanVienHoanThanhs = dsNvHoanThanh.Select(x => x.MaNhanVien).ToList(),
                TenNhanVienHoanThanhs = tenNvHoanThanh,
                // Bước quy trình đã chọn
                DanhSachBuocQuyTrinh = dsBuoc,
                NgayDuKienBaoTri = ngayDuKien,
                RowVersion = Convert.ToBase64String(h.RowVersion),
                Nam = namTuKeHoach ?? h.NgayTao.Year,
                NamTuKeHoach = namTuKeHoach != null
            };
        }

        public async Task<(bool, string?)> TaoYeuCauBaoTriAsync(int maNguoiDung, TaoYeuCauBaoTriDto dto)
        {
            var nv = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nv == null) return (false, "Không xác định được người dùng.");

            if (dto.ThoiGianDuKien <= 0)
                return (false, "Thời gian dự kiến phải lớn hơn 0.");
            if (dto.ThoiGianDuKien > 24)
                return (false, "Bảo trì trong ngày — thời gian dự kiến tối đa 24 giờ.");
            if (dto.ThangBaoTri < 1 || dto.ThangBaoTri > 12)
                return (false, "Tháng bảo trì không hợp lệ.");
            if (dto.NgayBaoTri.Month != dto.ThangBaoTri || dto.NgayBaoTri.Year != dto.NamBaoTri)
                return (false, "Ngày bảo trì phải thuộc tháng/năm đã chọn.");
            if (dto.GioKetThuc <= dto.GioBatDau)
                return (false, "Giờ kết thúc phải sau giờ bắt đầu.");

            var tb = await _repo.GetThietBiByIdAsync(dto.MaThietBi);
            if (tb == null) return (false, "Không tìm thấy thiết bị.");

            // Không trùng yêu cầu cùng thiết bị + tháng/năm còn hiệu lực
            if (await _repo.TonTaiYeuCauBaoTriThangAsync(dto.MaThietBi, dto.NamBaoTri, dto.ThangBaoTri))
                return (false, $"Thiết bị đã có yêu cầu bảo trì trong tháng {dto.ThangBaoTri}/{dto.NamBaoTri}.");

            var yc = new YeuCauBaoTriThietBi
            {
                MaThietBi = dto.MaThietBi,
                MaNhanVienYeuCau = nv.MaNhanVien,
                ThangBaoTri = dto.ThangBaoTri,
                NamBaoTri = dto.NamBaoTri,
                NgayBaoTri = dto.NgayBaoTri,
                ThoiGianDuKien = dto.ThoiGianDuKien,
                GioBatDau = dto.GioBatDau,
                GioKetThuc = dto.GioKetThuc,
                GhiChu = dto.GhiChu,
                TrangThai = "Chờ xác nhận",
                NgayTao = DateTime.Now,
            };
            await _repo.AddYeuCauBaoTriAsync(yc);
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<List<object>> GetYeuCauBaoTriAsync(string? trangThai, int? nam, int? thang)
        {
            var list = await _repo.GetYeuCauBaoTriListAsync(trangThai, nam, thang);
            return list.Select(y => (object)new
            {
                y.MaYeuCauBaoTri,
                y.MaThietBi,
                TenThietBi = y.MaThietBiNavigation?.TenThietBi,
                DanhMuc = y.MaThietBiNavigation?.LoaiThietBi,
                TenNguoiYeuCau = y.MaNhanVienYeuCauNavigation?.HoTen,
                TenNguoiXacNhan = y.MaNhanVienXacNhanNavigation?.HoTen,
                y.ThangBaoTri,
                y.NamBaoTri,
                y.NgayBaoTri,
                y.ThoiGianDuKien,
                GioBatDau = y.GioBatDau.ToString(@"hh\:mm"),
                GioKetThuc = y.GioKetThuc.ToString(@"hh\:mm"),
                y.TrangThai,
                y.LyDoTuChoi,
                y.GhiChu,
                y.NgayTao,
                y.NgayXacNhan,
            }).ToList();
        }

        public async Task<(bool, string?)> XacNhanYeuCauBaoTriAsync(int maYeuCau, int maNguoiDung, XacNhanYeuCauBaoTriDto dto)
        {
            var nv = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nv == null) return (false, "Không xác định được người dùng.");

            var yc = await _repo.GetYeuCauBaoTriByIdAsync(maYeuCau);
            if (yc == null) return (false, "Không tìm thấy yêu cầu.");
            if (yc.TrangThai != "Chờ xác nhận")
                return (false, "Yêu cầu không ở trạng thái chờ xác nhận.");

            if (dto.QuyetDinh == "Từ chối")
            {
                if (string.IsNullOrWhiteSpace(dto.LyDo))
                    return (false, "Vui lòng nhập lý do từ chối.");
                yc.TrangThai = "Từ chối";
                yc.LyDoTuChoi = dto.LyDo.Trim();
            }
            else if (dto.QuyetDinh == "Xác nhận")
            {
                yc.TrangThai = "Đã xác nhận";
                yc.LyDoTuChoi = null;
            }
            else
                return (false, "Quyết định không hợp lệ.");

            yc.MaNhanVienXacNhan = nv.MaNhanVien;
            yc.NgayXacNhan = DateTime.Now;
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<List<object>> GetYeuCauDaXacNhanDeTaoHoSoAsync(int? nam, int? thang)
        {
            var list = await _repo.GetYeuCauBaoTriListAsync("Đã xác nhận", nam, thang);
            return list.Select(y => (object)new
            {
                y.MaYeuCauBaoTri,
                y.MaThietBi,
                TenThietBi = y.MaThietBiNavigation?.TenThietBi,
                DanhMuc = y.MaThietBiNavigation?.LoaiThietBi,
                // Nhãn rõ ràng để không nhầm tháng khi lập kế hoạch
                NhanHienThi = $"YC #{y.MaYeuCauBaoTri} · {y.MaThietBiNavigation?.TenThietBi} · {y.NgayBaoTri:dd/MM/yyyy} · {y.ThoiGianDuKien:0.#}h",
                y.ThangBaoTri,
                y.NamBaoTri,
                y.NgayBaoTri,
                y.ThoiGianDuKien,
                GioBatDau = y.GioBatDau.ToString(@"hh\:mm"),
                GioKetThuc = y.GioKetThuc.ToString(@"hh\:mm"),
                TenNguoiYeuCau = y.MaNhanVienYeuCauNavigation?.HoTen,
                y.GhiChu,
            }).ToList();
        }

        /// <summary>
        /// Tổ trưởng cơ điện sửa yêu cầu bị xưởng từ chối rồi gửi lại (trạng thái → Chờ xác nhận).
        /// </summary>
        public async Task<(bool, string?)> SuaYeuCauBaoTriAsync(int maYeuCau, int maNguoiDung, SuaYeuCauBaoTriDto dto)
        {
            var nv = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nv == null) return (false, "Không xác định được người dùng.");

            var yc = await _repo.GetYeuCauBaoTriByIdAsync(maYeuCau);
            if (yc == null) return (false, "Không tìm thấy yêu cầu.");
            if (yc.TrangThai != "Từ chối")
                return (false, "Chỉ được sửa yêu cầu ở trạng thái Từ chối.");

            if (dto.ThoiGianDuKien <= 0)
                return (false, "Thời gian dự kiến phải lớn hơn 0.");
            if (dto.ThoiGianDuKien > 24)
                return (false, "Bảo trì trong ngày — thời gian dự kiến tối đa 24 giờ.");
            if (dto.ThangBaoTri < 1 || dto.ThangBaoTri > 12)
                return (false, "Tháng bảo trì không hợp lệ.");
            if (dto.NgayBaoTri.Month != dto.ThangBaoTri || dto.NgayBaoTri.Year != dto.NamBaoTri)
                return (false, "Ngày bảo trì phải thuộc tháng/năm đã chọn.");
            if (dto.GioKetThuc <= dto.GioBatDau)
                return (false, "Giờ kết thúc phải sau giờ bắt đầu.");

            var tb = await _repo.GetThietBiByIdAsync(dto.MaThietBi);
            if (tb == null) return (false, "Không tìm thấy thiết bị.");

            // Không trùng yêu cầu khác (cùng TB + tháng/năm, còn hiệu lực, khác id hiện tại)
            var trung = await _repo.TonTaiYeuCauBaoTriThangKhacIdAsync(dto.MaThietBi, dto.NamBaoTri, dto.ThangBaoTri, maYeuCau);
            if (trung)
                return (false, $"Thiết bị đã có yêu cầu bảo trì khác trong tháng {dto.ThangBaoTri}/{dto.NamBaoTri}.");

            yc.MaThietBi = dto.MaThietBi;
            yc.ThangBaoTri = dto.ThangBaoTri;
            yc.NamBaoTri = dto.NamBaoTri;
            yc.NgayBaoTri = dto.NgayBaoTri;
            yc.ThoiGianDuKien = dto.ThoiGianDuKien;
            yc.GioBatDau = dto.GioBatDau;
            yc.GioKetThuc = dto.GioKetThuc;
            yc.GhiChu = dto.GhiChu;
            yc.TrangThai = "Chờ xác nhận";
            yc.LyDoTuChoi = null;
            yc.MaNhanVienXacNhan = null;
            yc.NgayXacNhan = null;

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        /// <summary>NVKT bấm Tiến hành quy trình bảo trì — ghi nhận thời điểm bắt đầu lần đầu.</summary>
        public async Task<(bool, string?)> NhanVienBatDauBaoTriAsync(int maHoSo, int maNguoiDung)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nhanVien == null) return (false, "Không xác định được nhân viên.");
            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai is "Đã hoàn thành" or "Đã hủy" or "Từ chối")
                return (false, "Hồ sơ đã kết thúc, không thể tiến hành.");

            var dsPc = await _repo.GetPhanCongTheoHoSoBaoTriAsync(maHoSo);
            if (!LaNguoiDuocGhiChep(dsPc, nhanVien.MaNhanVien))
                return (false, "Bạn không được phân công trên hồ sơ này.");

            if (hoSo.ThoiDiemBatDauThucTe == null)
            {
                var now = DateTime.Now;
                hoSo.ThoiDiemBatDauThucTe = now;
                hoSo.GioBatDauDuKien = now.TimeOfDay;
            }
            foreach (var pc in dsPc.Where(p => p.TrangThai is "Đã phân công" or "Xác nhận"))
                pc.TrangThai = "Đang thực hiện";
            if (hoSo.TrangThai is "Đã duyệt")
                hoSo.TrangThai = "Đang thực hiện";

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        /// <summary>Danh sách tháng trong năm đã có hồ sơ BT (không hủy) của thiết bị.</summary>
        public async Task<object> GetThangCoBaoTriTheoThietBiAsync(int maThietBi, int nam)
        {
            var list = await _repo.GetHoSoBaoTriByTrangThaiAsync(null);
            var thang = list
                .Where(h => h.MaThieBi == maThietBi && h.TrangThai != "Đã hủy" && h.TrangThai != "Nháp")
                .Select(h =>
                {
                    var ct = h.ChiTietKeHoachBaoTri;
                    var m = ct?.NgayDuKienBaoTri.Month;
                    var y = ct?.NgayDuKienBaoTri.Year ?? h.NgayTao.Year;
                    return (Thang: m, Nam: y, h.MaHoSoBaoTri, h.TrangThai);
                })
                .Where(x => x.Nam == nam && x.Thang != null)
                .GroupBy(x => x.Thang!.Value)
                .Select(g => new
                {
                    Thang = g.Key,
                    SoHoSo = g.Count(),
                    TrangThais = g.Select(x => x.TrangThai).Distinct().ToList()
                })
                .OrderBy(x => x.Thang)
                .ToList();
            return new { Nam = nam, MaThietBi = maThietBi, DanhSachThang = thang };
        }
    }
}
