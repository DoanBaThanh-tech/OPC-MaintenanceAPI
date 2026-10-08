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

        // ---------- từ XuLyBaoTriPhanCongVaNVKT.cs ----------


        public async Task<(bool, string?)> PhanCongBaoTriAsync(int maHoSo, int maNguoiDungPhanCong, PhanCongDto? dto)
        {
            try
            {
                if (dto == null)
                    return (false, "Thiếu dữ liệu phân công (body rỗng).");

                var nhanVienPhanCong = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungPhanCong);
                if (nhanVienPhanCong == null)
                    return (false, "Không xác định được người phân công (token).");

                var hoSo = await _repo.GetHoSoBaoTriByIdAsync(maHoSo);
                if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");

                var tt = (hoSo.TrangThai ?? "").Trim();
                // Cho phép phân công khi đã duyệt hoặc đang thực hiện (cập nhật lại)
                if (tt is not ("Đã duyệt" or "Đang thực hiện"))
                    return (false,
                        $"Chỉ phân công khi hồ sơ đã được Giám đốc duyệt. Trạng thái hiện tại: «{tt}».");

                // NVKT đã tiến hành quy trình → không được đổi / cập nhật phân công
                if (hoSo.ThoiDiemBatDauThucTe != null)
                    return (false,
                        "Nhân viên kỹ thuật đã tiến hành quy trình — không được cập nhật phân công.");

                var dsNv = new List<int>();
                if (dto.MaNhanVienThucHiens != null)
                    dsNv.AddRange(dto.MaNhanVienThucHiens.Where(x => x > 0));
                if (dsNv.Count == 0 && dto.MaNhanVienThucHien is > 0)
                    dsNv.Add(dto.MaNhanVienThucHien.Value);
                dsNv = dsNv.Distinct().ToList();

                if (dsNv.Count == 0)
                    return (false, "Vui lòng chọn ít nhất một nhân viên thực hiện.");

                // Bắt buộc Tổ trưởng đã chọn ≥1 bước quy trình trước khi phân công
                var soBuocKeHoach = await _db.TienDoBuocQuyTrinhs
                    .CountAsync(t => t.MaHoSoBaoTri == maHoSo && t.TrangThai == "DuocChon");
                if (soBuocKeHoach <= 0)
                    return (false,
                        "Vui lòng chọn và Lưu các bước quy trình trong chi tiết hồ sơ trước khi phân công nhân viên.");

                // NVKT đã có tiến độ bước thực tế → không đổi phân công
                var coTienDoNvkt = await _db.TienDoBuocQuyTrinhs.AnyAsync(t =>
                    t.MaHoSoBaoTri == maHoSo && t.TrangThai != "DuocChon" && t.MaNhanVien > 0);
                if (coTienDoNvkt)
                    return (false,
                        "Nhân viên kỹ thuật đã bắt đầu thực hiện bước — không được cập nhật phân công.");

                // Chỉ chặn khi đã gửi Xưởng (Chờ xác nhận)
                var pcDangMo = await _repo.GetPhanCongTheoHoSoBaoTriAsync(maHoSo);
                if (pcDangMo.Any(p => p.TrangThai == "Chờ xác nhận"))
                    return (false, "Quy trình đang chờ Xưởng xác nhận — không được đổi phân công.");

                // Hủy mềm phân công cũ còn mở
                foreach (var pc in pcDangMo.Where(p =>
                             p.TrangThai is "Đã phân công" or "Xác nhận" or "Đang thực hiện" or "Từ chối"))
                    pc.TrangThai = "Đã hủy";

                int? maPhanCongDau = null;
                foreach (var maNv in dsNv)
                {
                    // Kiểm tra NV tồn tại
                    var nvTonTai = await _db.NhanViens.AnyAsync(n => n.MaNhanVien == maNv);
                    if (!nvTonTai)
                        return (false, $"Mã nhân viên {maNv} không tồn tại trong hệ thống.");

                    var phanCong = new PhanCongCongViec
                    {
                        MaNhanVienThucHien = maNv,
                        MaNhanVienPhanCong = nhanVienPhanCong.MaNhanVien,
                        MaHoSoBaoTri = maHoSo,
                        NgayBatDauDuKien = dto.NgayBatDauDuKien,
                        NgayKetThucDuKien = dto.NgayKetThucDuKien,
                        TrangThai = "Đã phân công",
                        LaNguoiGhiChep = true,
                        NgayPhanCong = DateTime.Now
                    };
                    await _repo.AddPhanCongAsync(phanCong);
                    await _repo.SaveChangesAsync();
                    maPhanCongDau ??= phanCong.MaPhanCong;
                }

                hoSo.MaPhanCong = maPhanCongDau;
                hoSo.TrangThai = "Đang thực hiện";

                var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
                if (chiTiet != null)
                    chiTiet.TrangThai = "Đang thực hiện";

                await _repo.SaveChangesAsync();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi phân công: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        /// <summary>
        /// Nhân viên kỹ thuật bấm "Hoàn thành bảo trì".
        /// Một người bấm → đồng bộ tất cả phân công cùng hồ sơ + hồ sơ + kế hoạch + thiết bị.
        /// </summary>
        public async Task<(bool, string?)> NhanVienHoanThanhBaoTriAsync(int maHoSo, int maNguoiDung)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nhanVien == null) return (false, "Không xác định được nhân viên.");

            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");

            // Idempotent: đã hoàn thành
            if (hoSo.TrangThai == "Đã hoàn thành")
                return (true, null);

            // Đã gửi quy trình (PC = Chờ xác nhận) — HS vẫn Đang thực hiện
            var dsPcCheck = await _repo.GetPhanCongTheoHoSoBaoTriAsync(maHoSo);
            if ((hoSo.TrangThai is "Đang thực hiện" or "Chờ xác nhận")
                && dsPcCheck.Any(p => p.TrangThai == "Chờ xác nhận"))
                return (true, null);

            if (hoSo.TrangThai is not ("Đang thực hiện" or "Đã duyệt" or "Từ chối" or "Chờ xác nhận"))
                return (false, "Hồ sơ không ở trạng thái đang thực hiện.");

            var dsPc = dsPcCheck;
            if (dsPc.Count == 0 && hoSo.MaPhanCong != null)
            {
                var pc = await _repo.GetPhanCongByIdAsync(hoSo.MaPhanCong.Value);
                if (pc != null) dsPc.Add(pc);
            }

            if (!LaNguoiDuocGhiChep(dsPc, nhanVien.MaNhanVien))
                return (false, "Chỉ người ghi chép quy trình mới được bấm Xong / gửi Xưởng.");

            // Xong → PC chờ Xưởng; HS / chi tiết kế hoạch GIỮ "Đang thực hiện"
            foreach (var pc in dsPc.Where(p => p.TrangThai is not ("Đã hủy" or "Hoàn thành")))
            {
                pc.TrangThai = "Chờ xác nhận";
                pc.LyDoTuChoi = null;
                pc.MaHoSoBaoTri ??= maHoSo;
            }

            hoSo.TrangThai = "Đang thực hiện";
            hoSo.LyDoTuChoi = null;
            var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
            if (chiTiet != null)
                chiTiet.TrangThai = "Đang thực hiện";

            await _repo.SaveChangesAsync();
            return (true, null);
        }


        public async Task<(bool, string?)> NhanVienXacNhanBaoTriAsync(int maHoSo, int maNguoiDung)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nhanVien == null) return (false, "Không xác định được nhân viên.");

            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Đã duyệt") return (false, "Hồ sơ không ở trạng thái Đã duyệt.");
            if (hoSo.MaPhanCong == null) return (false, "Chưa được phân công.");

            var phanCong = await _repo.GetPhanCongByIdAsync(hoSo.MaPhanCong.Value);
            if (phanCong == null) return (false, "Không tìm thấy phân công.");
            if (phanCong.TrangThai == "Đã hủy")
                return (false, "Yêu cầu bảo trì này được hủy bởi tổ trưởng kỹ thuật");
            if (phanCong.MaNhanVienThucHien != nhanVien.MaNhanVien)
                return (false, "Bạn không được phân công hồ sơ này.");
            if (phanCong.TrangThai != "Chờ xác nhận" && phanCong.TrangThai != "Đã phân công")
                return (false, "Phân công không ở trạng thái chờ xác nhận.");

            // Hồ sơ + chi tiết kế hoạch → Đang thực hiện; phân công → Xác nhận
            hoSo.TrangThai = "Đang thực hiện";
            phanCong.TrangThai = "Xác nhận";
            phanCong.LyDoTuChoi = null;

            if (hoSo.MaThieBiNavigation != null)
                hoSo.MaThieBiNavigation.TinhTrangHienTai = "Bảo trì";

            var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
            if (chiTiet != null)
                chiTiet.TrangThai = "Đang thực hiện";

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool, string?)> NhanVienTuChoiBaoTriAsync(int maHoSo, int maNguoiDung, TuChoiNhanViecDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.LyDo))
                return (false, "Vui lòng nhập lý do từ chối.");

            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nhanVien == null) return (false, "Không xác định được nhân viên.");

            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Đã duyệt") return (false, "Hồ sơ không ở trạng thái Đã duyệt.");
            if (hoSo.MaPhanCong == null) return (false, "Chưa được phân công.");

            var phanCong = await _repo.GetPhanCongByIdAsync(hoSo.MaPhanCong.Value);
            if (phanCong == null) return (false, "Không tìm thấy phân công.");
            if (phanCong.TrangThai == "Đã hủy")
                return (false, "Yêu cầu bảo trì này được hủy bởi tổ trưởng kỹ thuật");
            if (phanCong.MaNhanVienThucHien != nhanVien.MaNhanVien)
                return (false, "Bạn không được phân công hồ sơ này.");
            if (phanCong.TrangThai != "Chờ xác nhận" && phanCong.TrangThai != "Đã phân công")
                return (false, "Phân công không ở trạng thái chờ xác nhận.");

            // Giữ liên kết MaPhanCong trên hồ sơ để tổ trưởng thấy trạng thái Từ chối + lý do
            // (không gỡ null — nếu gỡ thì màn chi tiết mất hết thông tin từ chối)
            phanCong.TrangThai = "Từ chối";
            phanCong.LyDoTuChoi = dto.LyDo.Trim();
            // Hồ sơ vẫn "Đã duyệt" — tổ trưởng phân công lại người khác

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool, string?)> CapNhatHoSoBaoTriBiTuChoiAsync(int id, CapNhatHoSoBaoTriDto dto)
        {
            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Từ chối")
                return (false, "Chỉ được chỉnh sửa hồ sơ ở trạng thái Từ chối.");

            if (string.IsNullOrWhiteSpace(dto.NoiDungCongViec))
                return (false, "Vui lòng nhập nội dung công việc.");

            // Cập nhật nội dung + thời lượng + giờ
            // ThoiGianDuKien: "4" = 4 giờ · "90p" / "90 phút" = 90 phút (≤1440)
            hoSo.NoiDungCongViec = dto.NoiDungCongViec.Trim();
            if (dto.ThoiGianDuKien != null)
            {
                var raw = dto.ThoiGianDuKien.Trim();
                var lower = raw.ToLowerInvariant();
                var laPhut = lower.EndsWith("p")
                             || lower.Contains("phút")
                             || lower.Contains("phut");
                var soStr = lower
                    .Replace("phút", "", StringComparison.Ordinal)
                    .Replace("phut", "", StringComparison.Ordinal)
                    .Replace("giờ", "", StringComparison.Ordinal)
                    .Replace("gio", "", StringComparison.Ordinal)
                    .TrimEnd('p')
                    .Trim();
                if (!int.TryParse(soStr, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var so) || so <= 0)
                    return (false, laPhut
                        ? "Số phút dự kiến phải là số nguyên dương lớn hơn 0."
                        : "Giờ dự kiến phải là số nguyên dương lớn hơn 0.");
                if (laPhut)
                {
                    if (so > 1440)
                        return (false, "Số phút không quá 1440 (tối đa 1 ngày).");
                    hoSo.ThoiGianDuKien = so + "p";
                }
                else
                {
                    if (so > 24)
                        return (false, "Số giờ không quá 24 (tối đa 1 ngày).");
                    hoSo.ThoiGianDuKien = so.ToString();
                }
            }

            if (!string.IsNullOrWhiteSpace(dto.GioBatDauDuKien) &&
                TimeSpan.TryParse(dto.GioBatDauDuKien, out var gbd))
                hoSo.GioBatDauDuKien = gbd;

            if (!string.IsNullOrWhiteSpace(dto.GioKetThucDuKien) &&
                TimeSpan.TryParse(dto.GioKetThucDuKien, out var gkt))
                hoSo.GioKetThucDuKien = gkt;

            if (hoSo.GioBatDauDuKien != null && hoSo.GioKetThucDuKien != null &&
                hoSo.GioKetThucDuKien <= hoSo.GioBatDauDuKien)
                return (false, "Giờ kết thúc phải sau giờ bắt đầu.");

            // Ngày dự kiến nằm trên Chi tiết kế hoạch — từ tháng gốc trở đi + không trùng tháng khác
            var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
            if (dto.NgayDuKienBaoTri.HasValue && chiTiet != null)
            {
                var ngayGoc = chiTiet.NgayDuKienBaoTri;
                var ngayMoi = dto.NgayDuKienBaoTri.Value;
                var (okNgay, loiNgay) = _kiemTraNgayDuKienKhiSua(ngayMoi, ngayGoc, hoSo.NgayTao);
                if (!okNgay) return (false, loiNgay);

                var ngayLap = chiTiet.MaKeHoachNavigation?.NgayLapKeHoach;
                if (ngayLap != null && ngayMoi <= ngayLap.Value)
                    return (false, "Ngày bảo trì dự kiến phải sau ngày lập kế hoạch.");

                if (ngayMoi.Year != ngayGoc.Year || ngayMoi.Month != ngayGoc.Month)
                {
                    var trungThang = await _repo.TonTaiChiTietHoacHoSoThietBiThangKhacAsync(
                        hoSo.MaThieBi, ngayMoi.Year, ngayMoi.Month,
                        excludeMaChiTiet: chiTiet.MaChiTietKeHoach,
                        excludeMaHoSo: hoSo.MaHoSoBaoTri);
                    if (trungThang)
                        return (false,
                            $"Thiết bị đã có hồ sơ/kế hoạch bảo trì trong tháng {ngayMoi.Month}/{ngayMoi.Year}. " +
                            "Vui lòng chọn tháng lớn hơn.");
                }

                chiTiet.NgayDuKienBaoTri = ngayMoi;
            }

            // Xưởng đã chỉnh sửa sau khi GĐ từ chối → gửi thẳng lại Giám đốc duyệt
            // (không quay về "Chờ duyệt"/chờ xưởng để tránh xưởng phải gửi lại lần nữa)
            hoSo.NgayTao = DateTime.Now;

            hoSo.TrangThai = "Chờ GĐ duyệt";
            hoSo.LyDoTuChoi = null;
            hoSo.MaNhanVienDuyet = null;
            hoSo.NgayDuyet = null;

            if (chiTiet != null)
                chiTiet.TrangThai = "Chờ GĐ duyệt";

            // Giữ thiết bị = Bảo trì (nếu bạn đã thêm dòng này trước đó)
            var tb = await _repo.GetThietBiByIdAsync(hoSo.MaThieBi);
            if (tb != null)
                tb.TinhTrangHienTai = "Bảo trì";

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<List<object>> GetLichSuPhanCongAsync()
        {
            var list = await _repo.GetLichSuPhanCongChiTietAsync();
            // Tổ trưởng không thấy phân công đã hủy
            list = list.Where(p => p.TrangThai != "Đã hủy").ToList();
            return list.Select(p =>
            {
                // Ưu tiên MaHoSo*Navigation (mỗi NV một dòng) — HoSoBaoTri inverse chỉ gắn PC đầu
                var hsBt = p.MaHoSoBaoTriNavigation ?? p.HoSoBaoTri;
                var hsSc = p.MaHoSoSuaChuaNavigation ?? p.HoSoSuaChua;
                // Giờ bắt đầu = NVKT bấm Tiến hành; giờ kết thúc = Xưởng xác nhận
                var gioBd = hsBt?.ThoiDiemBatDauThucTe ?? hsSc?.ThoiDiemBatDauThucTe;
                var gioKt = hsBt?.ThoiDiemKetThucThucTe ?? hsSc?.ThoiDiemKetThucThucTe;
                return (object)new
                {
                    p.MaPhanCong,
                    TenNhanVienPhanCong = p.MaNhanVienPhanCongNavigation?.HoTen,
                    TenNhanVienThucHien = p.MaNhanVienThucHienNavigation?.HoTen,
                    p.TrangThai,
                    p.LyDoTuChoi,
                    p.NgayPhanCong,
                    GioBatDau = gioBd,
                    GioKetThuc = gioKt,
                    MaHoSoBaoTri = hsBt?.MaHoSoBaoTri ?? p.MaHoSoBaoTri,
                    MaHoSoSuaChua = hsSc?.MaHoSoSuaChua ?? p.MaHoSoSuaChua,
                    TenThietBi = hsBt?.MaThieBiNavigation?.TenThietBi
                                 ?? hsSc?.MaThieBiNavigation?.TenThietBi,
                    Loai = hsBt != null || p.MaHoSoBaoTri != null
                        ? "Bảo trì"
                        : (hsSc != null || p.MaHoSoSuaChua != null ? "Sửa chữa" : null),
                };
            }).ToList();
        }


        public async Task<List<object>> GetLichSuPheDuyetAsync(string? loai, int? nam)
        {
            var list = await _repo.GetLichSuPheDuyetAsync(loai, nam);
            return list.Select(l =>
            {
                var isBt = l.MaHoSoBaoTri != null;
                var hsBt = l.MaHoSoBaoTriNavigation;
                var hsSc = l.MaHoSoSuaChuaNavigation;
                // Map quyết định → trạng thái hồ sơ dễ hiểu
                var trangThaiHoSo = l.QuyetDinh == "Duyệt" || l.QuyetDinh == "Đã duyệt"
                    ? "Đã duyệt"
                    : (l.QuyetDinh == "Từ chối" ? "Từ chối" : (l.QuyetDinh ?? ""));
                return (object)new
                {
                    l.MaPheDuyet,
                    Loai = isBt ? "Bảo trì" : "Sửa chữa",
                    MaHoSo = isBt ? l.MaHoSoBaoTri : l.MaHoSoSuaChua,
                    TenThietBi = isBt
                        ? hsBt?.MaThieBiNavigation?.TenThietBi
                        : hsSc?.MaThieBiNavigation?.TenThietBi,
                    TenNguoiLap = isBt
                        ? hsBt?.MaNhanVienTaoNavigation?.HoTen
                        : hsSc?.MaNhanVienTaoNavigation?.HoTen,
                    NoiDung = isBt ? hsBt?.NoiDungCongViec : hsSc?.MoTaHuHong,
                    TenNguoiDuyet = l.MaNhanVienDuyetNavigation?.HoTen,
                    QuyetDinh = l.QuyetDinh,
                    TrangThaiHoSo = trangThaiHoSo,
                    l.LyDo,
                    l.NgayDuyet,
                    Nam = l.NgayDuyet.Year,
                };
            }).ToList();
        }

        public async Task<List<int>> GetCacNamCoLichSuPheDuyetAsync(string? loai)
        {
            var list = await _repo.GetLichSuPheDuyetAsync(loai, null);
            return list.Select(l => l.NgayDuyet.Year).Distinct().OrderByDescending(y => y).ToList();
        }

        public async Task<(bool, string?)> HuyPhanCongAsync(int maPhanCong, int maNguoiDung)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nhanVien == null) return (false, "Không xác định được người dùng.");

            var phanCong = await _repo.GetPhanCongByIdAsync(maPhanCong);
            if (phanCong == null) return (false, "Không tìm thấy phân công.");

            var tt = phanCong.TrangThai ?? "";
            if (tt is not ("Chờ xác nhận" or "Từ chối"))
                return (false, "Chỉ hủy được phân công ở trạng thái Chờ xác nhận hoặc Từ chối.");

            // Gỡ liên kết hồ sơ → tổ trưởng phân công lại được
            var hoSoBt = await _repo.GetHoSoBaoTriByMaPhanCongAsync(maPhanCong);
            if (hoSoBt != null)
            {
                hoSoBt.MaPhanCong = null;
                if (hoSoBt.TrangThai == "Đang thực hiện")
                    hoSoBt.TrangThai = "Đã duyệt";
                var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSoBt.MaHoSoBaoTri);
                if (chiTiet != null && chiTiet.TrangThai == "Đang thực hiện")
                    chiTiet.TrangThai = "Đã duyệt";
            }

            var hoSoSc = await _repo.GetHoSoSuaChuaByMaPhanCongAsync(maPhanCong);
            if (hoSoSc != null)
            {
                hoSoSc.MaPhanCong = null;
                if (hoSoSc.TrangThai == "Đang thực hiện")
                    hoSoSc.TrangThai = "Đã duyệt";
            }

            // Soft cancel — giữ bản ghi để NVKT vẫn thấy & nhận thông báo khi mở
            phanCong.TrangThai = "Đã hủy";
            await _repo.SaveChangesAsync();
            return (true, "Đã hủy phân công. Có thể phân công lại trên hồ sơ.");
        }

        public async Task<(bool, string?)> GhiNhanKetQuaAsync(int maPhanCong, GhiNhanKetQuaDto dto)
        {
            var phanCong = await _repo.GetPhanCongByIdAsync(maPhanCong);
            if (phanCong == null) return (false, "Không tìm thấy công việc.");
            if (phanCong.TrangThai != "Xác nhận" && phanCong.TrangThai != "Đang thực hiện")
                return (false, "Chỉ ghi nhận kết quả cho phân công đã được xác nhận.");

            if (await _repo.DaCoKetQuaAsync(maPhanCong))
                return (false, "Phân công này đã có kết quả thực hiện.");

            var hoSoBt = await _repo.GetHoSoBaoTriByMaPhanCongAsync(maPhanCong);
            var hoSoSc = hoSoBt == null ? await _repo.GetHoSoSuaChuaByMaPhanCongAsync(maPhanCong) : null;

            DateOnly? ngayDuKien = null;
            if (hoSoBt != null)
                ngayDuKien = await _repo.GetNgayDuKienBaoTriTheoHoSoBaoTriAsync(hoSoBt.MaHoSoBaoTri);

            var ngayGhi = dto.NgayGhiNhan ?? DateTime.Now;
            if (ngayDuKien.HasValue)
            {
                // Không được chọn ngày/tháng trước ngày dự kiến bảo trì trong hồ sơ
                var ngayDk = ngayDuKien.Value.ToDateTime(TimeOnly.MinValue);
                var ngayGhiDate = ngayGhi.Date;
                if (ngayGhiDate < ngayDk.Date)
                    return (false,
                        $"Ngày ghi nhận không được trước ngày dự kiến bảo trì ({ngayDuKien.Value:dd/MM/yyyy}).");
                // Phải nằm đúng tháng/năm dự kiến bảo trì theo hồ sơ
                if (ngayGhi.Year != ngayDuKien.Value.Year || ngayGhi.Month != ngayDuKien.Value.Month)
                    return (false,
                        $"Ngày ghi nhận phải nằm trong tháng {ngayDuKien.Value.Month}/{ngayDuKien.Value.Year} (tháng dự kiến bảo trì theo hồ sơ).");
            }

            var maNvGhiNhan = dto.MaNhanVienGhiNhan;
            if (maNvGhiNhan <= 0)
                maNvGhiNhan = phanCong.MaNhanVienThucHien;

            await _repo.AddKetQuaAsync(new KetQuaThucHien
            {
                MaPhanCong = maPhanCong,
                MaNhanVienGhiNhan = maNvGhiNhan,
                SoLieuGhiNhan = dto.SoLieuGhiNhan,
                HinhAnh = dto.HinhAnh,
                GhiChu = dto.GhiChu,
                NgayGhiNhan = ngayGhi,
                XacNhanHoanThanh = true
            });
            // NVKT gửi kết quả → PC chờ Xưởng; HS BT/SC GIỮ "Đang thực hiện"
            phanCong.TrangThai = "Chờ xác nhận";
            phanCong.LyDoTuChoi = null;

            if (hoSoBt != null)
            {
                phanCong.MaHoSoBaoTri ??= hoSoBt.MaHoSoBaoTri;
                hoSoBt.TrangThai = "Đang thực hiện";
                hoSoBt.LyDoTuChoi = null;
                var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSoBt.MaHoSoBaoTri);
                if (chiTiet != null)
                    chiTiet.TrangThai = "Đang thực hiện";
            }
            else if (hoSoSc != null)
            {
                phanCong.MaHoSoSuaChua ??= hoSoSc.MaHoSoSuaChua;
                hoSoSc.TrangThai = "Đang thực hiện";
                hoSoSc.LyDoTuChoi = null;
            }

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        /// <summary>
        /// Xưởng xác nhận hoặc từ chối kết quả BT/SC do NVKT gửi (sau khi bấm Xong).
        /// Xác nhận → Đã hoàn thành + TB về Sản xuất. Từ chối → NVKT chỉnh lại (Trạng thái Từ chối).
        /// </summary>
        public async Task<(bool, string?)> XuongXacNhanKetQuaAsync(
            int? maHoSoBaoTri, int? maHoSoSuaChua, int maNguoiDung, bool xacNhan, string? lyDo)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nhanVien == null) return (false, "Không xác định được nhân viên xưởng.");

            if (maHoSoBaoTri.HasValue && maHoSoBaoTri.Value > 0)
            {
                var hoSo = await _repo.GetHoSoBaoTriByIdAsync(maHoSoBaoTri.Value);
                if (hoSo == null) return (false, "Không tìm thấy hồ sơ bảo trì.");

                var dsPc = await _repo.GetPhanCongTheoHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
                // Chờ Xưởng: HS Đang thực hiện + PC Chờ xác nhận (hoặc HS cũ còn gắn Chờ xác nhận)
                var choXuongXn = hoSo.TrangThai == "Chờ xác nhận"
                    || (hoSo.TrangThai == "Đang thực hiện"
                        && dsPc.Any(p => p.TrangThai == "Chờ xác nhận"));
                if (!choXuongXn)
                    return (false, "Hồ sơ chưa có quy trình chờ Xưởng xác nhận (vẫn Đang thực hiện / chưa gửi).");

                var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);

                if (xacNhan)
                {
                    // Xưởng xác nhận → Hoàn thành (đồng bộ HS + PC + chi tiết)
                    hoSo.TrangThai = "Đã hoàn thành";
                    GhiNhanKetThucThucTe(hoSo);
                    hoSo.LyDoTuChoi = null;
                    if (chiTiet != null) chiTiet.TrangThai = "Đã hoàn thành";
                    foreach (var pc in dsPc.Where(p => p.TrangThai is not "Đã hủy"))
                    {
                        pc.TrangThai = "Hoàn thành";
                        pc.LyDoTuChoi = null;
                    }

                    var conHoSoMo = await _repo.CoHoSoBaoTriDangMoAsync(hoSo.MaThieBi, loaiTruMaHoSo: hoSo.MaHoSoBaoTri);
                    if (!conHoSoMo)
                    {
                        var tb = await _repo.GetThietBiByIdAsync(hoSo.MaThieBi);
                        if (tb != null) tb.TinhTrangHienTai = "Sản xuất";
                    }

                    // Xưởng xác nhận → gửi thẳng hồ sơ vật tư cho Giám đốc (Chờ duyệt), không qua Tổ trưởng
                    await CapNhatTrangThaiHoSoVatTuTheoCongViecAsync(
                        maHoSoBaoTri: hoSo.MaHoSoBaoTri, maHoSoSuaChua: null, trangThai: "Chờ duyệt");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(lyDo))
                        return (false, "Vui lòng nhập lý do từ chối để NVKT chỉnh sửa.");
                    // HS vẫn Đang thực hiện; PC = Từ chối (NVKT cập nhật quy trình)
                    hoSo.TrangThai = "Đang thực hiện";
                    hoSo.LyDoTuChoi = null;
                    if (chiTiet != null) chiTiet.TrangThai = "Đang thực hiện";
                    foreach (var pc in dsPc.Where(p => p.TrangThai is "Chờ xác nhận" or "Hoàn thành" or "Đang thực hiện"))
                    {
                        pc.TrangThai = "Từ chối";
                        pc.LyDoTuChoi = lyDo.Trim();
                    }
                    // Mở lại bước đã «Cập nhật thành công» trước đó → cho NV chỉnh lại
                    await MoLaiTienDoSauTuChoiAsync(maHoSoBaoTri: hoSo.MaHoSoBaoTri, maHoSoSuaChua: null);
                }

                await _repo.SaveChangesAsync();
                return (true, null);
            }

            if (maHoSoSuaChua.HasValue && maHoSoSuaChua.Value > 0)
            {
                var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSoSuaChua.Value);
                if (hoSo == null) return (false, "Không tìm thấy hồ sơ sửa chữa.");

                var dsPc = await _repo.GetPhanCongTheoHoSoSuaChuaAsync(hoSo.MaHoSoSuaChua);
                var choXuongXn = hoSo.TrangThai == "Chờ xác nhận"
                    || (hoSo.TrangThai == "Đang thực hiện"
                        && dsPc.Any(p => p.TrangThai == "Chờ xác nhận"));
                if (!choXuongXn)
                    return (false, "Hồ sơ chưa có quy trình chờ Xưởng xác nhận (vẫn Đang thực hiện / chưa gửi).");

                if (xacNhan)
                {
                    hoSo.TrangThai = "Đã hoàn thành";
                    GhiNhanKetThucThucTe(hoSo);
                    hoSo.LyDoTuChoi = null;
                    foreach (var pc in dsPc.Where(p => p.TrangThai is not "Đã hủy"))
                    {
                        pc.TrangThai = "Hoàn thành";
                        pc.LyDoTuChoi = null;
                    }

                    var conScMo = await _repo.CoHoSoSuaChuaDangMoAsync(hoSo.MaThieBi, loaiTruMaHoSo: hoSo.MaHoSoSuaChua);
                    var conBtMo = await _repo.CoHoSoBaoTriDangMoAsync(hoSo.MaThieBi);
                    var tb = await _repo.GetThietBiByIdAsync(hoSo.MaThieBi);
                    if (tb != null && !conScMo && !conBtMo)
                        tb.TinhTrangHienTai = "Sản xuất";

                    // Xưởng xác nhận SC → gửi thẳng HS vật tư cho Giám đốc
                    await CapNhatTrangThaiHoSoVatTuTheoCongViecAsync(
                        maHoSoBaoTri: null, maHoSoSuaChua: hoSo.MaHoSoSuaChua, trangThai: "Chờ duyệt");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(lyDo))
                        return (false, "Vui lòng nhập lý do từ chối để NVKT chỉnh sửa.");
                    hoSo.TrangThai = "Đang thực hiện";
                    hoSo.LyDoTuChoi = null;
                    foreach (var pc in dsPc.Where(p => p.TrangThai is "Chờ xác nhận" or "Hoàn thành" or "Đang thực hiện"))
                    {
                        pc.TrangThai = "Từ chối";
                        pc.LyDoTuChoi = lyDo.Trim();
                    }
                    await MoLaiTienDoSauTuChoiAsync(maHoSoBaoTri: null, maHoSoSuaChua: hoSo.MaHoSoSuaChua);
                }

                await _repo.SaveChangesAsync();
                return (true, null);
            }

            return (false, "Cần mã hồ sơ bảo trì hoặc sửa chữa.");
        }

        public async Task<List<object>> GetYeuCauCuaNhanVienAsync(int maNguoiDung, string? loai = null, string? trangThaiPhanCong = null)
        {
            var nv = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nv == null) return new List<object>();

            var list = await _repo.GetPhanCongCuaNhanVienAsync(nv.MaNhanVien, loai, trangThaiPhanCong);
            var ketQua = new List<object>();
            foreach (var p in list)
            {
                var bt = p.HoSoBaoTri ?? p.MaHoSoBaoTriNavigation;
                // Nhiều NV cùng 1 HS SC: PC đầu có HoSoSuaChua; PC còn lại có MaHoSoSuaChuaNavigation
                var sc = p.HoSoSuaChua ?? p.MaHoSoSuaChuaNavigation;
                DateOnly? ngayDuKien = null;
                if (bt != null)
                    ngayDuKien = await _repo.GetNgayDuKienBaoTriTheoHoSoBaoTriAsync(bt.MaHoSoBaoTri);

                ketQua.Add(new
                {
                    p.MaPhanCong,
                    TrangThaiPhanCong = p.TrangThai,
                    p.LyDoTuChoi,
                    p.NgayPhanCong,
                    NgayBatDauDuKien = p.NgayBatDauDuKien,
                    NgayKetThucDuKien = p.NgayKetThucDuKien,
                    TenNhanVienPhanCong = p.MaNhanVienPhanCongNavigation?.HoTen,
                    Loai = bt != null ? "Bảo trì" : (sc != null ? "Sửa chữa" : null),
                    MaHoSo = bt?.MaHoSoBaoTri ?? sc?.MaHoSoSuaChua ?? p.MaHoSoSuaChua,
                    MaThietBi = bt?.MaThieBi ?? sc?.MaThieBi,
                    TenThietBi = bt?.MaThieBiNavigation?.TenThietBi ?? sc?.MaThieBiNavigation?.TenThietBi,
                    NoiDung = bt?.NoiDungCongViec ?? sc?.MoTaHuHong,
                    ThoiGianDuKien = bt?.ThoiGianDuKien,
                    TrangThaiHoSo = bt?.TrangThai ?? sc?.TrangThai,
                    NgayDuKienBaoTri = ngayDuKien,
                    NgayTaoHoSo = bt?.NgayTao ?? sc?.NgayTao,
                    LaNguoiGhiChep = p.LaNguoiGhiChep,
                    // Có giá trị = đã từng bấm Tiến hành → nút "Tiếp tục quy trình"
                    ThoiDiemBatDauThucTe = bt?.ThoiDiemBatDauThucTe ?? sc?.ThoiDiemBatDauThucTe,
                });
            }
            return ketQua;
        }

        public async Task<List<object>> GetYeuCauDaXacNhanAsync(int maNguoiDung, string? loai = null)
        {
            return await GetYeuCauCuaNhanVienAsync(maNguoiDung, loai, "Xác nhận");
        }

        public async Task<(bool, string?)> NhanVienXacNhanSuaChuaAsync(int maHoSo, int maNguoiDung)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nhanVien == null) return (false, "Không xác định được nhân viên.");

            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Đã duyệt" && hoSo.TrangThai != "Đang thực hiện")
                return (false, "Hồ sơ không ở trạng thái phù hợp.");
            if (hoSo.MaPhanCong == null) return (false, "Chưa được phân công.");

            var phanCong = await _repo.GetPhanCongByIdAsync(hoSo.MaPhanCong.Value);
            if (phanCong == null) return (false, "Không tìm thấy phân công.");
            if (phanCong.TrangThai == "Đã hủy")
                return (false, "Yêu cầu bảo trì này được hủy bởi tổ trưởng kỹ thuật");
            if (phanCong.MaNhanVienThucHien != nhanVien.MaNhanVien)
                return (false, "Bạn không được phân công hồ sơ này.");
            if (phanCong.TrangThai != "Chờ xác nhận" && phanCong.TrangThai != "Đã phân công")
                return (false, "Phân công không ở trạng thái chờ xác nhận.");

            hoSo.TrangThai = "Đang thực hiện";
            phanCong.TrangThai = "Xác nhận";
            phanCong.LyDoTuChoi = null;
            if (hoSo.MaThieBiNavigation != null)
                hoSo.MaThieBiNavigation.TinhTrangHienTai = "Sửa chữa";

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool, string?)> NhanVienTuChoiSuaChuaAsync(int maHoSo, int maNguoiDung, TuChoiNhanViecDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.LyDo))
                return (false, "Vui lòng nhập lý do từ chối.");

            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nhanVien == null) return (false, "Không xác định được nhân viên.");

            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.MaPhanCong == null) return (false, "Chưa được phân công.");

            var phanCong = await _repo.GetPhanCongByIdAsync(hoSo.MaPhanCong.Value);
            if (phanCong == null) return (false, "Không tìm thấy phân công.");
            if (phanCong.TrangThai == "Đã hủy")
                return (false, "Yêu cầu bảo trì này được hủy bởi tổ trưởng kỹ thuật");
            if (phanCong.MaNhanVienThucHien != nhanVien.MaNhanVien)
                return (false, "Bạn không được phân công hồ sơ này.");
            if (phanCong.TrangThai != "Chờ xác nhận" && phanCong.TrangThai != "Đã phân công")
                return (false, "Phân công không ở trạng thái chờ xác nhận.");

            // Giữ MaPhanCong để thấy trạng thái Từ chối + lý do trên chi tiết
            phanCong.TrangThai = "Từ chối";
            phanCong.LyDoTuChoi = dto.LyDo.Trim();
            if (hoSo.TrangThai == "Đang thực hiện")
                hoSo.TrangThai = "Đã duyệt";

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool, string?)> XacNhanHoanThanhBaoTriAsync(int maHoSo, XacNhanDto dto)
        {
            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");

            var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);

            if (!dto.Dat)
            {
                hoSo.TrangThai = "Đang thực hiện";
                if (chiTiet != null) chiTiet.TrangThai = "Đang thực hiện";
                await _repo.SaveChangesAsync();
                return (true, "Yêu cầu nhân viên thực hiện lại.");
            }

            hoSo.TrangThai = "Đã hoàn thành";
            GhiNhanKetThucThucTe(hoSo);
            if (chiTiet != null)
                chiTiet.TrangThai = "Đã hoàn thành";

            // Chỉ về Sản xuất khi không còn hồ sơ bảo trì đang mở
            var conHoSoMo = await _repo.CoHoSoBaoTriDangMoAsync(hoSo.MaThieBi, loaiTruMaHoSo: maHoSo);
            if (!conHoSoMo)
            {
                if (hoSo.MaThieBiNavigation != null)
                    hoSo.MaThieBiNavigation.TinhTrangHienTai = "Sản xuất";
                else
                {
                    var tb = await _repo.GetThietBiByIdAsync(hoSo.MaThieBi);
                    if (tb != null)
                        tb.TinhTrangHienTai = "Sản xuất";
                }
            }

            await _repo.SaveChangesAsync();
            return (true, null);

        }
    }
}
