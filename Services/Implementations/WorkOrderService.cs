using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Core.Exceptions;
using OPC.MaintenanceAPI.DTOs.WorkOrder;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;
using OPC.MaintenanceAPI.DTOs.Common;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public class WorkOrderService : IWorkOrderService
    {
        private readonly IWorkOrderRepository _repo;
        private readonly INhanVienRepository _nhanVienRepo;

        public WorkOrderService(IWorkOrderRepository repo, INhanVienRepository nhanVienRepo)
        {
            _repo = repo;
            _nhanVienRepo = nhanVienRepo;
        }

        // ===== BẢO TRÌ =====

        public async Task<(bool, string?)> TaoHoSoBaoTriAsync(int maNguoiDungTao, TaoHoSoBaoTriDto dto)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungTao);
            if (nhanVien == null) return (false, "Không xác định được người tạo hồ sơ.");

            if (dto.MaChiTietKeHoach.HasValue)
            {
                var chiTiet = await _repo.GetChiTietKeHoachByIdAsync(dto.MaChiTietKeHoach.Value);
                if (chiTiet == null) return (false, "Không tìm thấy dòng kế hoạch bảo trì.");
                if (chiTiet.MaHoSoBaoTri != null)
                    return (false, "Dòng kế hoạch này đã có hồ sơ bảo trì. Không thể tạo thêm.");

                // Ngày dự kiến bảo trì phải sau ngày lập kế hoạch
                var ngayLap = chiTiet.MaKeHoachNavigation?.NgayLapKeHoach;
                if (ngayLap != null && chiTiet.NgayDuKienBaoTri <= ngayLap.Value)
                    return (false,
                        $"Ngày dự kiến bảo trì ({chiTiet.NgayDuKienBaoTri:dd/MM/yyyy}) phải lớn hơn ngày lập kế hoạch ({ngayLap.Value:dd/MM/yyyy}). " +
                        "Vui lòng chỉnh lại ngày dự kiến trên kế hoạch trước khi tạo hồ sơ.");

                // Chặn: thiết bị đã có hồ sơ bảo trì trong cùng tháng với ngày dự kiến
                var nam = chiTiet.NgayDuKienBaoTri.Year;
                var thang = chiTiet.NgayDuKienBaoTri.Month;
                if (await _repo.TonTaiHoSoBaoTriTheoThietBiThangAsync(dto.MaThietBi, nam, thang))
                    return (false,
                        $"Thiết bị này đã có hồ sơ bảo trì trong tháng {thang}/{nam}. Không thể tạo thêm hồ sơ.");
            }


            var hoSo = new HoSoBaoTri
            {
                MaThieBi = dto.MaThietBi,
                MaNhanVienTao = nhanVien.MaNhanVien,
                NoiDungCongViec = dto.NoiDungCongViec,
                ThoiGianDuKien = dto.ThoiGianDuKien,
                NgayTao = DateTime.Now,
                TrangThai = "Chờ duyệt"
            };
            await _repo.AddHoSoBaoTriAsync(hoSo);
            await _repo.SaveChangesAsync();
            // Lấy ngày dự kiến từ chi tiết KH
        DateOnly? ngayDuKien = null;
        if (dto.MaChiTietKeHoach.HasValue)
        {
            var ct = await _repo.GetChiTietKeHoachByIdAsync(dto.MaChiTietKeHoach.Value);
            ngayDuKien = ct?.NgayDuKienBaoTri;
        }

        if (ngayDuKien.HasValue)
        {
            var tb = await _repo.GetThietBiByIdAsync(dto.MaThietBi);
            if (tb != null)
            {
                if (tb.NgayBaoTriGanNhat == null)
                {
                    // Lần 1
                    tb.NgayBaoTriGanNhat = ngayDuKien.Value;
                    tb.NgayBaoTriTiepTheo = null;
                }
                else if (tb.NgayBaoTriTiepTheo == null)
                {
                    // Lần 2
                    tb.NgayBaoTriTiepTheo = ngayDuKien.Value;
                }
                else
                {
                    // Lần 3+
                    tb.NgayBaoTriGanNhat = tb.NgayBaoTriTiepTheo;
                    tb.NgayBaoTriTiepTheo = ngayDuKien.Value;
                }
            }
        }
        await _repo.SaveChangesAsync();
            if (dto.MaChiTietKeHoach.HasValue)
            {
                var chiTiet = await _repo.GetChiTietKeHoachByIdAsync(dto.MaChiTietKeHoach.Value);
                chiTiet!.MaHoSoBaoTri = hoSo.MaHoSoBaoTri;
                await _repo.SaveChangesAsync();
            }
            {
                var tbTrangThai = await _repo.GetThietBiByIdAsync(dto.MaThietBi);
                if (tbTrangThai != null)
                {
                    tbTrangThai.TinhTrangHienTai = "Bảo trì";
                    await _repo.SaveChangesAsync();
                }
            }
            return (true, null);
        }


        public async Task<List<object>> GetHoSoBaoTriTheoTrangThaiAsync(string? trangThai, int? nam)
        {
            var list = await _repo.GetHoSoBaoTriByTrangThaiAsync(trangThai);
            var ketQua = new List<object>();
            foreach (var h in list)
            {
                var namTuKeHoach = await _repo.GetNamKeHoachTheoHoSoBaoTriAsync(h.MaHoSoBaoTri);
                var namThucTe = namTuKeHoach ?? h.NgayTao.Year;
                if (nam.HasValue && namThucTe != nam.Value) continue;

                ketQua.Add(new
                {
                    h.MaHoSoBaoTri,
                    MaThietBi = h.MaThieBi,
                    TenThietBi = h.MaThieBiNavigation?.TenThietBi,
                    TenNhanVienTao = h.MaNhanVienTaoNavigation?.HoTen,  // ← thêm
                    h.NoiDungCongViec, h.ThoiGianDuKien, h.TrangThai, h.NgayTao,
                    h.MaPhanCong,
                    Nam = namThucTe,
                    NamTuKeHoach = namTuKeHoach != null
                });
            }
            return ketQua;
        }

        // Luồng 7 — có Optimistic Concurrency Control qua RowVersion
        public async Task<(bool, string?)> DuyetHoSoBaoTriAsync(int id, int maNguoiDungDuyet, DuyetHoSoDto dto)
        {
            var nhanVienDuyet = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungDuyet);
            if (nhanVienDuyet == null) return (false, "Không xác định được người duyệt.");

            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Chờ duyệt")
                return (false, "Hồ sơ đã được xử lý trước đó.");
            if (dto.QuyetDinh != "Duyệt" && dto.QuyetDinh != "Từ chối")
                return (false, "QuyetDinh chỉ nhận 'Duyệt' hoặc 'Từ chối'.");
            if (dto.QuyetDinh == "Từ chối" && string.IsNullOrWhiteSpace(dto.LyDo))
                return (false, "Vui lòng nhập lý do từ chối.");

            // ===== MỤC 3: chỉ khi DUYỆT — Ngày duyệt (hôm nay) phải < Ngày bảo trì dự kiến =====
            if (dto.QuyetDinh == "Duyệt")
            {
                var ngayDuKien = await _repo.GetNgayDuKienBaoTriTheoHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
                if (ngayDuKien != null)
                {
                    var ngayDuyet = DateOnly.FromDateTime(DateTime.Today);
                    if (ngayDuyet >= ngayDuKien.Value)
                        return (false,
                            $"Ngày duyệt ({ngayDuyet:dd/MM/yyyy}) phải nhỏ hơn ngày bảo trì dự kiến ({ngayDuKien.Value:dd/MM/yyyy}). " +
                            "Vui lòng yêu cầu tổ trưởng chỉnh lại ngày dự kiến hoặc duyệt trước ngày đó.");
                }
            }
            // ===== hết mục 3 =====

            _repo.SetHoSoBaoTriRowVersion(hoSo, Convert.FromBase64String(dto.RowVersion));

            hoSo.MaNhanVienDuyet = nhanVienDuyet.MaNhanVien;
            hoSo.NgayDuyet = DateTime.Now;   // đúng ngày GĐ bấm duyệt
            hoSo.TrangThai = dto.QuyetDinh == "Duyệt" ? "Đã duyệt" : "Từ chối";
            hoSo.LyDoTuChoi = dto.QuyetDinh == "Từ chối" ? dto.LyDo : null;

            // Đồng bộ sang Chi tiết kế hoạch
            var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
            if (chiTiet != null)
                chiTiet.TrangThai = hoSo.TrangThai;

            try
            {
                await _repo.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return (false, "Hồ sơ này vừa được người khác xử lý trước bạn. Vui lòng tải lại.");
            }

            await _repo.AddLichSuPheDuyetAsync(new LichSuPheDuyet
            {
                MaHoSoBaoTri = hoSo.MaHoSoBaoTri,
                MaNhanVienDuyet = nhanVienDuyet.MaNhanVien,
                QuyetDinh = dto.QuyetDinh,
                LyDo = dto.LyDo,
                NgayDuyet = DateTime.Now
            });
            await _repo.SaveChangesAsync();

            return (true, null);
        }

        public async Task<(bool, string?)> PhanCongBaoTriAsync(int maHoSo, int maNguoiDungPhanCong, PhanCongDto dto)
        {
            var nhanVienPhanCong = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungPhanCong);
            if (nhanVienPhanCong == null) return (false, "Không xác định được người phân công.");

            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Đã duyệt") return (false, "Hồ sơ chưa được duyệt.");

            if (await _repo.ThietBiDangTrongQuyTrinhKhacAsync(hoSo.MaThieBi, "BaoTri", maHoSo))
                return (false, "Thiết bị này đang trong quy trình bảo trì/sửa chữa khác, không thể phân công.");

            if (dto.NgayKetThucDuKien < dto.NgayBatDauDuKien)
                return (false, "Ngày kết thúc không được trước ngày bắt đầu.");
            var phanCong = new PhanCongCongViec
            {
                MaNhanVienThucHien = dto.MaNhanVienThucHien,
                MaNhanVienPhanCong = nhanVienPhanCong.MaNhanVien,
                NgayBatDauDuKien = dto.NgayBatDauDuKien,
                NgayKetThucDuKien = dto.NgayKetThucDuKien,
                TrangThai = "Chờ xác nhận",   // chờ NVKT xác nhận
                NgayPhanCong = DateTime.Now
            };
            await _repo.AddPhanCongAsync(phanCong);
            await _repo.SaveChangesAsync();

            hoSo.MaPhanCong = phanCong.MaPhanCong;
            // GIỮ nguyên "Đã duyệt" — chưa chuyển Đang thực hiện
            // Không đổi TinhTrangHienTai thiết bị ở bước này

            var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
            if (chiTiet != null)
                chiTiet.TrangThai = "Đã duyệt";

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

            // Chỉ cập nhật phân công; hồ sơ vẫn "Đã duyệt" để tổ trưởng phân công lại
            phanCong.TrangThai = "Từ chối";
            phanCong.LyDoTuChoi = dto.LyDo.Trim();
            // Gỡ liên kết phân công trên hồ sơ để có thể phân công nhân viên khác
            hoSo.MaPhanCong = null;

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
            hoSo.NoiDungCongViec = dto.NoiDungCongViec.Trim();
            if (dto.ThoiGianDuKien != null)
                hoSo.ThoiGianDuKien = dto.ThoiGianDuKien.Trim();

            if (!string.IsNullOrWhiteSpace(dto.GioBatDauDuKien) &&
                TimeSpan.TryParse(dto.GioBatDauDuKien, out var gbd))
                hoSo.GioBatDauDuKien = gbd;

            if (!string.IsNullOrWhiteSpace(dto.GioKetThucDuKien) &&
                TimeSpan.TryParse(dto.GioKetThucDuKien, out var gkt))
                hoSo.GioKetThucDuKien = gkt;

            if (hoSo.GioBatDauDuKien != null && hoSo.GioKetThucDuKien != null &&
                hoSo.GioKetThucDuKien <= hoSo.GioBatDauDuKien)
                return (false, "Giờ kết thúc phải sau giờ bắt đầu.");

            // Ngày dự kiến nằm trên Chi tiết kế hoạch
            var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
            if (dto.NgayDuKienBaoTri.HasValue && chiTiet != null)
            {
                var ngayMoi = dto.NgayDuKienBaoTri.Value;
                if (ngayMoi < DateOnly.FromDateTime(DateTime.Today))
                    return (false, "Ngày bảo trì dự kiến không được ở quá khứ.");

                var ngayLap = chiTiet.MaKeHoachNavigation?.NgayLapKeHoach;
                if (ngayLap != null && ngayMoi <= ngayLap.Value)
                    return (false, "Ngày bảo trì dự kiến phải sau ngày lập kế hoạch.");

                chiTiet.NgayDuKienBaoTri = ngayMoi;
            }

            // Gửi lại duyệt — Ngày duyệt để null, khi GĐ duyệt mới ghi
            hoSo.NgayTao = DateTime.Now;

            hoSo.TrangThai = "Chờ duyệt";
            hoSo.LyDoTuChoi = null;
            hoSo.MaNhanVienDuyet = null;
            hoSo.NgayDuyet = null;

            if (chiTiet != null)
                chiTiet.TrangThai = "Chờ duyệt";

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
            return list.Select(p => (object)new
            {
                p.MaPhanCong,
                TenNhanVienPhanCong = p.MaNhanVienPhanCongNavigation?.HoTen,
                TenNhanVienThucHien = p.MaNhanVienThucHienNavigation?.HoTen,
                p.TrangThai,
                p.LyDoTuChoi,
                p.NgayPhanCong,
                GioBatDau = p.NgayBatDauDuKien,
                GioKetThuc = p.NgayKetThucDuKien,
                MaHoSoBaoTri = p.HoSoBaoTri?.MaHoSoBaoTri,
                MaHoSoSuaChua = p.HoSoSuaChua?.MaHoSoSuaChua,
                TenThietBi = p.HoSoBaoTri?.MaThieBiNavigation?.TenThietBi
                             ?? p.HoSoSuaChua?.MaThieBiNavigation?.TenThietBi,
                Loai = p.HoSoBaoTri != null ? "Bảo trì" : (p.HoSoSuaChua != null ? "Sửa chữa" : null),
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
                if (ngayGhi.Year != ngayDuKien.Value.Year || ngayGhi.Month != ngayDuKien.Value.Month)
                    return (false,
                        $"Ngày ghi nhận phải nằm trong tháng {ngayDuKien.Value.Month}/{ngayDuKien.Value.Year} (tháng dự kiến bảo trì).");
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
            phanCong.TrangThai = "Hoàn thành";

            if (hoSoBt != null)
            {
                hoSoBt.TrangThai = "Đã hoàn thành";
                var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSoBt.MaHoSoBaoTri);
                if (chiTiet != null)
                    chiTiet.TrangThai = "Đã hoàn thành";

                var conHoSoMo = await _repo.CoHoSoBaoTriDangMoAsync(hoSoBt.MaThieBi, loaiTruMaHoSo: hoSoBt.MaHoSoBaoTri);
                if (!conHoSoMo)
                {
                    var tb = await _repo.GetThietBiByIdAsync(hoSoBt.MaThieBi);
                    if (tb != null)
                        tb.TinhTrangHienTai = "Sản xuất";
                }
            }
            else if (hoSoSc != null)
            {
                hoSoSc.TrangThai = "Đã hoàn thành";
                var tb = await _repo.GetThietBiByIdAsync(hoSoSc.MaThieBi);
                if (tb != null)
                    tb.TinhTrangHienTai = "Sản xuất";
            }

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<List<object>> GetYeuCauCuaNhanVienAsync(int maNguoiDung, string? loai = null, string? trangThaiPhanCong = null)
        {
            var nv = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nv == null) return new List<object>();

            var list = await _repo.GetPhanCongCuaNhanVienAsync(nv.MaNhanVien, loai, trangThaiPhanCong);
            var ketQua = new List<object>();
            foreach (var p in list)
            {
                var bt = p.HoSoBaoTri;
                var sc = p.HoSoSuaChua;
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
                    Loai = bt != null ? "Bảo trì" : "Sửa chữa",
                    MaHoSo = bt?.MaHoSoBaoTri ?? sc?.MaHoSoSuaChua,
                    MaThietBi = bt?.MaThieBi ?? sc?.MaThieBi,
                    TenThietBi = bt?.MaThieBiNavigation?.TenThietBi ?? sc?.MaThieBiNavigation?.TenThietBi,
                    NoiDung = bt?.NoiDungCongViec ?? sc?.MoTaHuHong,
                    ThoiGianDuKien = bt?.ThoiGianDuKien,
                    TrangThaiHoSo = bt?.TrangThai ?? sc?.TrangThai,
                    NgayDuKienBaoTri = ngayDuKien,
                    NgayTaoHoSo = bt?.NgayTao ?? sc?.NgayTao,
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

            phanCong.TrangThai = "Từ chối";
            phanCong.LyDoTuChoi = dto.LyDo.Trim();
            if (hoSo.TrangThai == "Đang thực hiện")
                hoSo.TrangThai = "Đã duyệt";
            hoSo.MaPhanCong = null;

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

        // ===== SỬA CHỮA =====


        public async Task<(bool, string?)> TaoHoSoSuaChuaAsync(TaoHoSoSuaChuaDto dto)
        {
            var hoSo = new HoSoSuaChua
            {
                MaThieBi = dto.MaThietBi,
                MaNhanVienTao = dto.MaNhanVienTao,
                MoTaHuHong = dto.MoTaHuHong,
                PhuongAnSuaChua = dto.PhuongAnSuaChua,
                NgayTao = DateTime.Now,
                TrangThai = dto.GuiDuyet ? "Chờ duyệt" : "Nháp"
            };
            await _repo.AddHoSoSuaChuaAsync(hoSo);
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<List<object>> GetHoSoSuaChuaTheoTrangThaiAsync(string trangThai) =>
        (await _repo.GetHoSoSuaChuaByTrangThaiAsync(trangThai)).Select(h => (object)new
        {
            h.MaHoSoSuaChua, h.MaThieBi, TenThietBi = h.MaThieBiNavigation?.TenThietBi,
            h.MoTaHuHong, h.TrangThai, h.NgayTao
        }).ToList();

        public async Task<object?> GetChiTietHoSoSuaChuaAsync(int id)
        {
            var h = await _repo.GetHoSoSuaChuaByIdAsync(id);
            if (h == null) return null;
            return new
            {
                h.MaHoSoSuaChua, h.MaThieBi, TenThietBi = h.MaThieBiNavigation?.TenThietBi,
                h.MoTaHuHong, h.PhuongAnSuaChua, h.TrangThai, h.LyDoTuChoi,
                h.NgayTao, h.NgayDuyet, h.MaPhanCong,
                RowVersion = Convert.ToBase64String(h.RowVersion)
            };
        }

        // Luồng 11 — cũng áp dụng Optimistic Concurrency Control
        public async Task<(bool, string?)> DuyetHoSoSuaChuaAsync(int id, int maNguoiDungDuyet, DuyetHoSoDto dto)
        {
            var nhanVienDuyet = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungDuyet);
            if (nhanVienDuyet == null) return (false, "Không xác định được người duyệt.");

            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Chờ duyệt")
                return (false, "Hồ sơ đã được xử lý trước đó.");
            if (dto.QuyetDinh != "Duyệt" && dto.QuyetDinh != "Từ chối")
                return (false, "QuyetDinh chỉ nhận 'Duyệt' hoặc 'Từ chối'.");
            if (dto.QuyetDinh == "Từ chối" && string.IsNullOrWhiteSpace(dto.LyDo))
                return (false, "Vui lòng nhập lý do từ chối.");

            _repo.SetHoSoSuaChuaRowVersion(hoSo, Convert.FromBase64String(dto.RowVersion));

            hoSo.MaNhanVienDuyet = nhanVienDuyet.MaNhanVien;
            hoSo.NgayDuyet = DateTime.Now;
            hoSo.TrangThai = dto.QuyetDinh == "Duyệt" ? "Đã duyệt" : "Từ chối";
            hoSo.LyDoTuChoi = dto.QuyetDinh == "Từ chối" ? dto.LyDo : null;

            try
            {
                await _repo.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return (false, "Hồ sơ này vừa được người khác xử lý trước bạn. Vui lòng tải lại.");
            }

            await _repo.AddLichSuPheDuyetAsync(new LichSuPheDuyet
            {
                MaHoSoSuaChua = hoSo.MaHoSoSuaChua,
                MaNhanVienDuyet = nhanVienDuyet.MaNhanVien,
                QuyetDinh = dto.QuyetDinh,
                LyDo = dto.LyDo,
                NgayDuyet = DateTime.Now
            });
            await _repo.SaveChangesAsync();

            return (true, null);
        }

        public async Task<(bool, string?)> PhanCongSuaChuaAsync(int maHoSo, PhanCongDto dto)
        {
            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");

            if (await _repo.ThietBiDangTrongQuyTrinhKhacAsync(hoSo.MaThieBi, "SuaChua", maHoSo))
                return (false, "Thiết bị này đang trong quy trình bảo trì/sửa chữa khác, không thể phân công.");

            if (dto.NgayKetThucDuKien < dto.NgayBatDauDuKien)
                return (false, "Ngày kết thúc không được trước ngày bắt đầu.");
            var phanCong = new PhanCongCongViec
            {
                MaNhanVienThucHien = dto.MaNhanVienThucHien,
                MaNhanVienPhanCong = dto.MaNhanVienPhanCong,
                NgayBatDauDuKien = dto.NgayBatDauDuKien,
                NgayKetThucDuKien = dto.NgayKetThucDuKien,
                TrangThai = "Chờ xác nhận",
                NgayPhanCong = DateTime.Now
            };
            await _repo.AddPhanCongAsync(phanCong);
            await _repo.SaveChangesAsync();

            hoSo.MaPhanCong = phanCong.MaPhanCong;
            // Giữ "Đã duyệt" — chờ NVKT xác nhận mới chuyển Đang thực hiện

            await _repo.SaveChangesAsync();
            return (true, null);
        }



        public async Task<(bool, string?)> XacNhanHoanThanhSuaChuaAsync(int maHoSo, XacNhanDto dto)
        {
            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");

            if (hoSo.MaPhanCong == null || !await _repo.DaCoKetQuaAsync(hoSo.MaPhanCong.Value))
                return (false, "Chưa có kết quả thực hiện được ghi nhận, không thể xác nhận hoàn thành.");

            if (!dto.Dat)
            {
                hoSo.TrangThai = "Đang thực hiện";
                await _repo.SaveChangesAsync();
                return (true, "Yêu cầu nhân viên thực hiện lại.");
            }

            hoSo.TrangThai = "Đã hoàn thành";
            // Hoàn thành sửa chữa → trở về Sản xuất
            if (hoSo.MaThieBiNavigation != null)
                hoSo.MaThieBiNavigation.TinhTrangHienTai = "Sản xuất";
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        // ===== TRUY VẤN =====



        // Helper dùng chung: tính Nam cho từng hồ sơ 1 lần, tái sử dụng cho cả lọc lẫn liệt kê năm có dữ liệu
        private async Task<List<(HoSoBaoTri hoSo, int nam, bool namTuKeHoach)>> LayDanhSachKemNamAsync(string? trangThai)
        {
            var list = await _repo.GetHoSoBaoTriByTrangThaiAsync(trangThai);
            var ketQua = new List<(HoSoBaoTri, int, bool)>();
            foreach (var h in list)
            {
                var namTuKeHoach = await _repo.GetNamKeHoachTheoHoSoBaoTriAsync(h.MaHoSoBaoTri);
                ketQua.Add((h, namTuKeHoach ?? h.NgayTao.Year, namTuKeHoach != null));
            }
            return ketQua;
        }


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

            return new
            {
                h.MaHoSoBaoTri,
                MaThietBi = h.MaThieBi,
                TenThietBi = h.MaThieBiNavigation?.TenThietBi,
                TenNhanVienTao = h.MaNhanVienTaoNavigation?.HoTen,
                h.NoiDungCongViec,
                h.ThoiGianDuKien,
                GioBatDauDuKien = FmtGio(h.GioBatDauDuKien),   // ← thêm
                GioKetThucDuKien = FmtGio(h.GioKetThucDuKien), // ← thêm
                h.TrangThai,
                h.LyDoTuChoi,
                h.NgayTao,
                h.NgayDuyet,
                h.MaPhanCong,
                NgayDuKienBaoTri = ngayDuKien,
                RowVersion = Convert.ToBase64String(h.RowVersion),
                Nam = namTuKeHoach ?? h.NgayTao.Year,
                NamTuKeHoach = namTuKeHoach != null
            };
        }
    }
}