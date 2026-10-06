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
        private readonly OPC.MaintenanceAPI.Data.OPCDbContext _db;

        public WorkOrderService(
            IWorkOrderRepository repo,
            INhanVienRepository nhanVienRepo,
            OPC.MaintenanceAPI.Data.OPCDbContext db)
        {
            _repo = repo;
            _nhanVienRepo = nhanVienRepo;
            _db = db;
        }

        // ===== BẢO TRÌ =====

        public async Task<(bool, string?)> TaoHoSoBaoTriAsync(int maNguoiDungTao, TaoHoSoBaoTriDto dto)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungTao);
            if (nhanVien == null) return (false, "Không xác định được người tạo hồ sơ.");

            // Không lập bảo trì khi thiết bị đang sửa chữa / bảo trì / có hồ sơ SC hoặc BT chưa xong
            var tbKiemTra = await _repo.GetThietBiByIdAsync(dto.MaThietBi);
            if (tbKiemTra == null) return (false, "Không tìm thấy thiết bị.");
            var ttTb = OPC.MaintenanceAPI.DTOs.Equipment.TrangThaiThietBiConst.ChuanHoa(tbKiemTra.TinhTrangHienTai);
            if (ttTb == "Sửa chữa" || await _repo.CoHoSoSuaChuaDangMoAsync(dto.MaThietBi))
                return (false, "Thiết bị đang sửa chữa — không thể tạo hồ sơ bảo trì. Hoàn tất sửa chữa trước.");
            if (ttTb == "Bảo trì" || await _repo.CoHoSoBaoTriDangMoAsync(dto.MaThietBi))
                return (false, "Thiết bị đang bảo trì — không thể tạo thêm hồ sơ bảo trì.");

            // Không còn bắt buộc Yêu cầu bảo trì — Tổ trưởng tạo hồ sơ và gửi xưởng trực tiếp
            if (dto.MaChiTietKeHoach.HasValue)
            {
                var chiTiet = await _repo.GetChiTietKeHoachByIdAsync(dto.MaChiTietKeHoach.Value);
                if (chiTiet == null) return (false, "Không tìm thấy dòng kế hoạch bảo trì.");
                if (chiTiet.MaHoSoBaoTri != null)
                    return (false, "Dòng kế hoạch này đã có hồ sơ bảo trì. Không thể tạo thêm.");

                var ngayLap = chiTiet.MaKeHoachNavigation?.NgayLapKeHoach;
                if (ngayLap != null && chiTiet.NgayDuKienBaoTri <= ngayLap.Value)
                    return (false,
                        $"Ngày dự kiến bảo trì ({chiTiet.NgayDuKienBaoTri:dd/MM/yyyy}) phải lớn hơn ngày lập kế hoạch ({ngayLap.Value:dd/MM/yyyy}). " +
                        "Vui lòng chỉnh lại ngày dự kiến trên kế hoạch trước khi tạo hồ sơ.");

                var nam = chiTiet.NgayDuKienBaoTri.Year;
                var thang = chiTiet.NgayDuKienBaoTri.Month;
                // Đã có BT hoặc SC trong tháng (kể cả đang mở / đã hoàn thành) → không tạo thêm
                if (await _repo.TonTaiHoSoBtHoacScTrongThangAsync(dto.MaThietBi, nam, thang))
                    return (false,
                        $"Thiết bị này đã có hồ sơ bảo trì hoặc sửa chữa trong tháng {thang}/{nam}. Không thể tạo thêm.");
            }
            else
            {
                // Không gắn chi tiết KH — vẫn chặn theo tháng tạo
                var now = DateTime.Now;
                if (await _repo.TonTaiHoSoBtHoacScTrongThangAsync(dto.MaThietBi, now.Year, now.Month))
                    return (false,
                        $"Thiết bị này đã có hồ sơ bảo trì hoặc sửa chữa trong tháng {now.Month}/{now.Year}. Không thể tạo thêm.");
            }

            TimeSpan? gioBatDau = null;
            TimeSpan? gioKetThuc = null;
            if (!string.IsNullOrWhiteSpace(dto.GioBatDauDuKien) && TimeSpan.TryParse(dto.GioBatDauDuKien, out var gbd))
                gioBatDau = gbd;
            if (!string.IsNullOrWhiteSpace(dto.GioKetThucDuKien) && TimeSpan.TryParse(dto.GioKetThucDuKien, out var gkt))
                gioKetThuc = gkt;

            // Tạo xong gửi thẳng xưởng — Chờ duyệt
            var trangThai = "Chờ duyệt";

            var hoSo = new HoSoBaoTri
            {
                MaThieBi = dto.MaThietBi,
                MaNhanVienTao = nhanVien.MaNhanVien,
                NoiDungCongViec = dto.NoiDungCongViec,
                ThoiGianDuKien = dto.ThoiGianDuKien,
                GioBatDauDuKien = gioBatDau,
                GioKetThucDuKien = gioKetThuc,
                MaYeuCauBaoTri = dto.MaYeuCauBaoTri, // legacy optional
                NgayTao = DateTime.Now,
                TrangThai = trangThai
            };
            await _repo.AddHoSoBaoTriAsync(hoSo);
            await _repo.SaveChangesAsync();

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
                    if (tb.NgayBaoTriTiepTheo.HasValue)
                        tb.NgayBaoTriGanNhat = tb.NgayBaoTriTiepTheo;
                    tb.NgayBaoTriTiepTheo = ngayDuKien.Value;
                }
            }
            await _repo.SaveChangesAsync();

            if (dto.MaChiTietKeHoach.HasValue)
            {
                var chiTiet = await _repo.GetChiTietKeHoachByIdAsync(dto.MaChiTietKeHoach.Value);
                chiTiet!.MaHoSoBaoTri = hoSo.MaHoSoBaoTri;
                chiTiet.TrangThai = trangThai;
                await _repo.SaveChangesAsync();
            }
            {
                var tbTrangThai = await _repo.GetThietBiByIdAsync(dto.MaThietBi);
                if (tbTrangThai != null)
                {
                    tbTrangThai.TinhTrangHienTai = OPC.MaintenanceAPI.DTOs.Equipment.TrangThaiThietBiConst.BaoTri;
                    await _repo.SaveChangesAsync();
                }
            }
            return (true, null);
        }

        /// <summary>
        /// Xưởng xác nhận lịch (đã xem/sửa ngày) — hồ sơ vẫn Chờ duyệt để Giám đốc duyệt.
        /// </summary>
        public async Task<(bool, string?)> XuongGuiGiamDocAsync(int id, int maNguoiDungXuong, XuongGuiGiamDocDto dto)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungXuong);
            if (nhanVien == null) return (false, "Không xác định được nhân viên xưởng.");

            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            // Chấp nhận cả dữ liệu cũ "Chờ xưởng" và luồng mới "Chờ duyệt"
            if (hoSo.TrangThai is not ("Chờ duyệt" or "Chờ xưởng"))
                return (false, "Chỉ xử lý được hồ sơ đang chờ duyệt.");

            if (!string.IsNullOrWhiteSpace(dto.NoiDungCongViec))
                hoSo.NoiDungCongViec = dto.NoiDungCongViec;
            if (!string.IsNullOrWhiteSpace(dto.ThoiGianDuKien))
                hoSo.ThoiGianDuKien = dto.ThoiGianDuKien;
            if (!string.IsNullOrWhiteSpace(dto.GioBatDauDuKien) && TimeSpan.TryParse(dto.GioBatDauDuKien, out var gbd))
                hoSo.GioBatDauDuKien = gbd;
            if (!string.IsNullOrWhiteSpace(dto.GioKetThucDuKien) && TimeSpan.TryParse(dto.GioKetThucDuKien, out var gkt))
                hoSo.GioKetThucDuKien = gkt;

            // Xưởng đã xác nhận lịch → chờ Giám đốc duyệt (khác "Chờ duyệt" để ẩn nút chỉnh sửa/gửi của Xưởng)
            hoSo.TrangThai = "Chờ GĐ duyệt";

            var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
            if (chiTiet != null)
                chiTiet.TrangThai = "Chờ GĐ duyệt";

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        /// <summary>Tổ trưởng gửi hồ sơ Chờ gửi → Chờ duyệt (xưởng nhận).</summary>
        public async Task<(bool, string?)> GuiDenXuongAsync(int id, int maNguoiDung)
        {
            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Chờ gửi")
                return (false, "Chỉ gửi được hồ sơ đang ở trạng thái Chờ gửi.");

            hoSo.TrangThai = "Chờ duyệt";
            var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
            if (chiTiet != null)
                chiTiet.TrangThai = "Chờ duyệt";

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        /// <summary>Tổ trưởng sửa ngày/nội dung khi hồ sơ còn Chờ gửi.</summary>
        public async Task<(bool, string?)> CapNhatHoSoChoGuiAsync(int id, int maNguoiDung, CapNhatHoSoBaoTriDto dto)
        {
            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Chờ gửi")
                return (false, "Chỉ chỉnh sửa được hồ sơ đang Chờ gửi.");

            if (dto.NoiDungCongViec != null)
                hoSo.NoiDungCongViec = dto.NoiDungCongViec;

            if (dto.NgayDuKienBaoTri.HasValue)
            {
                var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
                if (chiTiet != null)
                {
                    var ngayMoi = dto.NgayDuKienBaoTri.Value;
                    var ngayGoc = chiTiet.NgayDuKienBaoTri;
                    if (ngayMoi.Year != ngayGoc.Year)
                        return (false, $"Chỉ được chọn ngày trong năm kế hoạch {ngayGoc.Year}.");

                    if (ngayMoi.Month != ngayGoc.Month || ngayMoi.Year != ngayGoc.Year)
                    {
                        var trung = await _repo.TonTaiChiTietHoacHoSoThietBiThangKhacAsync(
                            hoSo.MaThieBi, ngayMoi.Year, ngayMoi.Month,
                            excludeMaChiTiet: chiTiet.MaChiTietKeHoach,
                            excludeMaHoSo: hoSo.MaHoSoBaoTri);
                        if (trung)
                            return (false,
                                $"Thiết bị đã có hồ sơ/kế hoạch bảo trì trong tháng {ngayMoi.Month}/{ngayMoi.Year}.");
                    }

                    chiTiet.NgayDuKienBaoTri = ngayMoi;
                }
            }

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        /// <summary>Xưởng lưu chỉnh sửa ngày/nội dung khi hồ sơ còn Chờ duyệt.</summary>
        public async Task<(bool, string?)> XuongLuuHoSoAsync(int id, int maNguoiDungXuong, CapNhatHoSoBaoTriDto dto)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungXuong);
            if (nhanVien == null) return (false, "Không xác định được nhân viên xưởng.");

            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai is not ("Chờ duyệt" or "Chờ xưởng"))
                return (false, "Chỉ chỉnh sửa được hồ sơ đang chờ duyệt.");

            if (dto.NoiDungCongViec != null) hoSo.NoiDungCongViec = dto.NoiDungCongViec;
            if (dto.ThoiGianDuKien != null) hoSo.ThoiGianDuKien = dto.ThoiGianDuKien;
            if (!string.IsNullOrWhiteSpace(dto.GioBatDauDuKien) && TimeSpan.TryParse(dto.GioBatDauDuKien, out var gbd))
                hoSo.GioBatDauDuKien = gbd;
            if (!string.IsNullOrWhiteSpace(dto.GioKetThucDuKien) && TimeSpan.TryParse(dto.GioKetThucDuKien, out var gkt))
                hoSo.GioKetThucDuKien = gkt;

            // Nếu còn trạng thái cũ → chuẩn hóa về Chờ duyệt
            if (hoSo.TrangThai == "Chờ xưởng")
                hoSo.TrangThai = "Chờ duyệt";

            if (dto.NgayDuKienBaoTri.HasValue)
            {
                var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
                if (chiTiet != null)
                {
                    var ngayGoc = chiTiet.NgayDuKienBaoTri;
                    var ngayMoi = dto.NgayDuKienBaoTri.Value;
                    var (okNgay, loiNgay) = _kiemTraNgayDuKienKhiSua(ngayMoi, ngayGoc, hoSo.NgayTao);
                    if (!okNgay) return (false, loiNgay);

                    chiTiet.NgayDuKienBaoTri = ngayMoi;
                    if (chiTiet.TrangThai == "Chờ xưởng")
                        chiTiet.TrangThai = "Chờ duyệt";
                }
            }

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        /// <summary>
        /// Ngày dự kiến khi Xưởng sửa:
        /// - Chỉ trong đúng tháng/năm kế hoạch gốc (không đổi tháng).
        /// - Phải lớn hơn ngày tạo hồ sơ; không ở quá khứ.
        /// </summary>
        private static (bool ok, string? loi) _kiemTraNgayDuKienKhiSua(
            DateOnly ngayMoi, DateOnly ngayDuKienCu, DateTime ngayTao)
        {
            if (ngayMoi < DateOnly.FromDateTime(DateTime.Today))
                return (false, "Ngày bảo trì dự kiến không được ở quá khứ.");

            var ngayTaoOnly = DateOnly.FromDateTime(ngayTao);
            if (ngayMoi <= ngayTaoOnly)
                return (false,
                    $"Ngày dự kiến bảo trì ({ngayMoi:dd/MM/yyyy}) phải lớn hơn ngày tạo hồ sơ ({ngayTaoOnly:dd/MM/yyyy}). " +
                    "Không được đặt trùng hoặc trước ngày tạo.");

            if (ngayMoi.Year != ngayDuKienCu.Year || ngayMoi.Month != ngayDuKienCu.Month)
                return (false,
                    $"Xưởng chỉ được chọn ngày trong tháng kế hoạch {ngayDuKienCu.Month}/{ngayDuKienCu.Year}. " +
                    "Không được đổi sang tháng khác (tháng do Tổ trưởng cơ điện quyết định khi lập kế hoạch).");

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

                // Ưu tiên PC "Chờ xác nhận" (đã gửi quy trình Xưởng) — không lấy PC cũ Đã hủy / Đang làm
                var dsPcList = await _repo.GetPhanCongTheoHoSoBaoTriAsync(h.MaHoSoBaoTri);
                var ttPc = dsPcList
                    .Where(p => p.TrangThai is not "Đã hủy")
                    .OrderByDescending(p => p.TrangThai == "Chờ xác nhận" ? 2
                        : p.TrangThai is "Hoàn thành" ? 0 : 1)
                    .ThenByDescending(p => p.MaPhanCong)
                    .Select(p => p.TrangThai)
                    .FirstOrDefault()
                    ?? h.MaPhanCongNavigation?.TrangThai;

                ketQua.Add(new
                {
                    h.MaHoSoBaoTri,
                    MaThietBi = h.MaThieBi,
                    TenThietBi = h.MaThieBiNavigation?.TenThietBi,
                    TenNhanVienTao = h.MaNhanVienTaoNavigation?.HoTen,
                    h.NoiDungCongViec, h.ThoiGianDuKien, h.TrangThai, h.NgayTao,
                    h.MaPhanCong,
                    TrangThaiPhanCong = ttPc,
                    ChoXuongXacNhanQuyTrinh = (h.TrangThai is "Đang thực hiện" or "Chờ xác nhận")
                        && dsPcList.Any(p => p.TrangThai == "Chờ xác nhận"),
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
            // Chỉ duyệt khi Xưởng đã gửi (Chờ GĐ duyệt) — không duyệt hồ sơ còn ở Xưởng
            if (hoSo.TrangThai is not "Chờ GĐ duyệt")
            {
                if (hoSo.TrangThai is "Chờ duyệt" or "Chờ xưởng")
                    return (false, "Hồ sơ chưa được Xưởng gửi lên Giám đốc.");
                return (false, "Hồ sơ đã được xử lý trước đó.");
            }
            if (dto.QuyetDinh != "Duyệt" && dto.QuyetDinh != "Từ chối")
                return (false, "QuyetDinh chỉ nhận 'Duyệt' hoặc 'Từ chối'.");
            if (dto.QuyetDinh == "Từ chối" && string.IsNullOrWhiteSpace(dto.LyDo))
                return (false, "Vui lòng nhập lý do từ chối.");

            // Ngày duyệt (hôm nay): ≥ ngày tạo; khi Duyệt phải < ngày dự kiến bảo trì
            {
                var ngayDuyet = DateOnly.FromDateTime(DateTime.Today);
                var ngayTaoOnly = DateOnly.FromDateTime(hoSo.NgayTao);
                if (ngayDuyet < ngayTaoOnly)
                    return (false,
                        $"Ngày duyệt ({ngayDuyet:dd/MM/yyyy}) phải lớn hơn hoặc bằng ngày tạo ({ngayTaoOnly:dd/MM/yyyy}).");

                if (dto.QuyetDinh == "Duyệt")
                {
                    var ngayDuKien = await _repo.GetNgayDuKienBaoTriTheoHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
                    if (ngayDuKien != null && ngayDuyet >= ngayDuKien.Value)
                        return (false,
                            $"Ngày duyệt ({ngayDuyet:dd/MM/yyyy}) phải nhỏ hơn ngày bảo trì dự kiến ({ngayDuKien.Value:dd/MM/yyyy}). " +
                            "Vui lòng yêu cầu chỉnh lại ngày dự kiến (đúng tháng kế hoạch) hoặc duyệt trước ngày đó.");
                }
            }

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

        /// <summary>
        /// Xưởng từ chối → mở lại bước đã «DaCapNhat» thành «DaXong» để NV chỉnh sửa lần nữa.
        /// </summary>
        private async Task MoLaiTienDoSauTuChoiAsync(int? maHoSoBaoTri, int? maHoSoSuaChua)
        {
            IQueryable<TienDoBuocQuyTrinh> q = _db.TienDoBuocQuyTrinhs;
            if (maHoSoBaoTri.HasValue)
                q = q.Where(t => t.MaHoSoBaoTri == maHoSoBaoTri);
            else if (maHoSoSuaChua.HasValue)
                q = q.Where(t => t.MaHoSoSuaChua == maHoSoSuaChua);
            else
                return;

            var list = await q.Where(t => t.TrangThai == "DaCapNhat").ToListAsync();
            foreach (var t in list)
                t.TrangThai = "DaXong";
        }

        /// <summary>
        /// Khi Xưởng xác nhận quy trình — chuyển HS vật tư sang trạng thái chỉ định
        /// (mặc định nghiệp vụ mới: "Chờ duyệt" = đã gửi Giám đốc, không qua Tổ trưởng).
        /// </summary>
        private async Task CapNhatTrangThaiHoSoVatTuTheoCongViecAsync(
            int? maHoSoBaoTri, int? maHoSoSuaChua, string trangThai)
        {
            IQueryable<HoSoSuDungVatTu> q = _db.HoSoSuDungVatTus;
            if (maHoSoBaoTri.HasValue)
                q = q.Where(h => h.MaHoSoBaoTri == maHoSoBaoTri);
            else if (maHoSoSuaChua.HasValue)
                q = q.Where(h => h.MaHoSoSuaChua == maHoSoSuaChua);
            else
                return;

            var list = await q.ToListAsync();
            var now = DateTime.Now;
            foreach (var h in list)
            {
                if (h.TrangThai is "Chờ gửi" or "Chờ duyệt" or "Đã gửi GĐ")
                {
                    h.TrangThai = trangThai;
                    if (trangThai is "Chờ duyệt" or "Đã gửi GĐ")
                        h.NgayGuiGiamDoc ??= now;
                }
            }
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

        // ===== SỬA CHỮA =====


        public async Task<(bool, string?)> TaoHoSoSuaChuaAsync(int maNguoiDungTao, TaoHoSoSuaChuaDto dto)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungTao);
            if (nhanVien == null) return (false, "Không xác định được người tạo hồ sơ.");

            if (string.IsNullOrWhiteSpace(dto.MoTaHuHong))
                return (false, "Vui lòng mô tả hư hỏng thiết bị.");

            var tb = await _repo.GetThietBiByIdAsync(dto.MaThietBi);
            if (tb == null) return (false, "Không tìm thấy thiết bị.");

            // Đang bảo trì / sửa chữa → không tạo SC (và ngược lại với BT)
            var ttTb = OPC.MaintenanceAPI.DTOs.Equipment.TrangThaiThietBiConst.ChuanHoa(tb.TinhTrangHienTai);
            if (ttTb == "Bảo trì" || await _repo.CoHoSoBaoTriDangMoAsync(dto.MaThietBi))
                return (false, "Thiết bị đang bảo trì — không thể tạo hồ sơ sửa chữa. Hoàn tất bảo trì trước.");
            if (ttTb == "Sửa chữa" || await _repo.CoHoSoSuaChuaDangMoAsync(dto.MaThietBi))
                return (false, "Thiết bị đã có hồ sơ sửa chữa chưa hoàn thành — không thể tạo thêm.");

            // Đã có BT hoặc SC trong tháng hiện tại → không tạo thêm SC
            var nowSc = DateTime.Now;
            if (await _repo.TonTaiHoSoBtHoacScTrongThangAsync(dto.MaThietBi, nowSc.Year, nowSc.Month))
                return (false,
                    $"Thiết bị này đã có hồ sơ bảo trì hoặc sửa chữa trong tháng {nowSc.Month}/{nowSc.Year}. Không thể tạo thêm hồ sơ sửa chữa.");

            // Gửi Tổ trưởng phân công (không qua GĐ)
            var trangThai = dto.GuiDuyet ? "Chờ phân công" : "Nháp";

            // Thời gian dự kiến (giờ/phút) + giờ bắt đầu/kết thúc — cùng ngày, không tràn
            TimeSpan? gbd = null;
            TimeSpan? gkt = null;
            if (!string.IsNullOrWhiteSpace(dto.GioBatDauDuKien) &&
                TimeSpan.TryParse(dto.GioBatDauDuKien, out var parsedGbd))
                gbd = parsedGbd;
            if (!string.IsNullOrWhiteSpace(dto.GioKetThucDuKien) &&
                TimeSpan.TryParse(dto.GioKetThucDuKien, out var parsedGkt))
                gkt = parsedGkt;
            if (gbd != null && gkt != null && gkt <= gbd)
                return (false, "Giờ kết thúc phải sau giờ bắt đầu (trong cùng ngày).");

            var hoSo = new HoSoSuaChua
            {
                MaThieBi = dto.MaThietBi,
                MaNhanVienTao = nhanVien.MaNhanVien,
                MoTaHuHong = dto.MoTaHuHong.Trim(),
                PhuongAnSuaChua = string.IsNullOrWhiteSpace(dto.PhuongAnSuaChua)
                    ? null
                    : dto.PhuongAnSuaChua.Trim(),
                ThoiGianDuKien = string.IsNullOrWhiteSpace(dto.ThoiGianDuKien)
                    ? null
                    : dto.ThoiGianDuKien.Trim(),
                GioBatDauDuKien = gbd,
                GioKetThucDuKien = gkt,
                NgayTao = DateTime.Now,
                TrangThai = trangThai
            };
            await _repo.AddHoSoSuaChuaAsync(hoSo);

            // Tạo hồ sơ SC → thiết bị Sản xuất → Sửa chữa ngay (kể cả nháp đã gửi)
            tb.TinhTrangHienTai = "Sửa chữa";
            // Lịch sửa chữa trên thiết bị: lần đầu → gần nhất; lần sau → tiếp theo
            var ngaySc = DateOnly.FromDateTime(hoSo.NgayTao);
            if (tb.NgayBaoTriGanNhat == null)
                tb.NgayBaoTriGanNhat = ngaySc;
            else
                tb.NgayBaoTriTiepTheo = ngaySc;

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<List<object>> GetHoSoSuaChuaTheoTrangThaiAsync(string? trangThai)
        {
            var list = await _repo.GetHoSoSuaChuaByTrangThaiAsync(trangThai);
            var ketQua = new List<object>();
            foreach (var h in list)
            {
                var dsPc = await _repo.GetPhanCongTheoHoSoSuaChuaAsync(h.MaHoSoSuaChua);
                var ttPc = dsPc
                    .Where(p => p.TrangThai is not "Đã hủy")
                    .OrderByDescending(p => p.TrangThai == "Chờ xác nhận" ? 2
                        : p.TrangThai is "Hoàn thành" ? 0 : 1)
                    .ThenByDescending(p => p.MaPhanCong)
                    .Select(p => p.TrangThai)
                    .FirstOrDefault();

                ketQua.Add(new
                {
                    h.MaHoSoSuaChua,
                    MaThietBi = h.MaThieBi,
                    TenThietBi = h.MaThieBiNavigation?.TenThietBi,
                    h.MoTaHuHong,
                    h.PhuongAnSuaChua,
                    h.TrangThai,
                    h.NgayTao,
                    TenNhanVienTao = h.MaNhanVienTaoNavigation?.HoTen,
                    h.MaPhanCong,
                    TrangThaiPhanCong = ttPc,
                    ChoXuongXacNhanQuyTrinh = (h.TrangThai is "Đang thực hiện" or "Chờ xác nhận")
                        && dsPc.Any(p => p.TrangThai == "Chờ xác nhận")
                });
            }
            return ketQua;
        }

        public async Task<object?> GetChiTietHoSoSuaChuaAsync(int id)
        {
            var h = await _repo.GetHoSoSuaChuaByIdAsync(id);
            if (h == null) return null;

            var dsPc = await _repo.GetPhanCongTheoHoSoSuaChuaAsync(h.MaHoSoSuaChua);
            var dangPc = dsPc
                .Where(p => p.TrangThai is "Đã phân công" or "Chờ xác nhận" or "Xác nhận" or "Đang thực hiện" or "Hoàn thành" or "Từ chối")
                .Select(p => new
                {
                    MaNhanVien = p.MaNhanVienThucHien,
                    TenNhanVien = p.MaNhanVienThucHienNavigation?.HoTen,
                    p.TrangThai,
                    p.MaPhanCong
                })
                .ToList();

            // Ưu tiên PC đang chờ Xưởng xác nhận quy trình (để hiện nút Xác nhận / Từ chối)
            var pcChoXn = dsPc.FirstOrDefault(p => p.TrangThai == "Chờ xác nhận");
            var pcChinh = pcChoXn
                ?? (h.MaPhanCong != null
                    ? dsPc.FirstOrDefault(p => p.MaPhanCong == h.MaPhanCong)
                    : null)
                ?? dsPc.OrderByDescending(p => p.MaPhanCong).FirstOrDefault();

            var choXuongXn = (h.TrangThai is "Đang thực hiện" or "Chờ xác nhận")
                && dsPc.Any(p => p.TrangThai == "Chờ xác nhận");

            return new
            {
                h.MaHoSoSuaChua,
                MaThietBi = h.MaThieBi,
                TenThietBi = h.MaThieBiNavigation?.TenThietBi,
                TenNhanVienTao = h.MaNhanVienTaoNavigation?.HoTen,
                h.MoTaHuHong,
                h.PhuongAnSuaChua,
                h.ThoiGianDuKien,
                GioBatDauDuKien = h.GioBatDauDuKien == null
                    ? null
                    : $"{(int)h.GioBatDauDuKien.Value.TotalHours:D2}:{h.GioBatDauDuKien.Value.Minutes:D2}",
                GioKetThucDuKien = h.GioKetThucDuKien == null
                    ? null
                    : $"{(int)h.GioKetThucDuKien.Value.TotalHours:D2}:{h.GioKetThucDuKien.Value.Minutes:D2}",
                ThoiDiemBatDauThucTe = h.ThoiDiemBatDauThucTe,
                ThoiDiemKetThucThucTe = h.ThoiDiemKetThucThucTe,
                h.TrangThai,
                h.LyDoTuChoi,
                h.NgayTao,
                h.NgayDuyet,
                h.MaPhanCong,
                TrangThaiPhanCong = pcChinh?.TrangThai,
                LyDoTuChoiPhanCong = pcChinh?.LyDoTuChoi,
                ChoXuongXacNhanQuyTrinh = choXuongXn,
                DanhSachNhanVienPhanCong = dangPc,
                MaNhanVienThucHiens = dangPc.Select(x => x.MaNhanVien).ToList(),
                TenNhanVienThucHiens = string.Join(", ", dangPc.Select(x => x.TenNhanVien).Where(t => !string.IsNullOrEmpty(t))),
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

        public async Task<(bool, string?)> PhanCongSuaChuaAsync(int maHoSo, int maNguoiDungPhanCong, PhanCongDto dto)
        {
            var nhanVienPhanCong = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungPhanCong);
            if (nhanVienPhanCong == null) return (false, "Không xác định được người phân công.");

            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai is not ("Chờ phân công" or "Đã duyệt" or "Đang thực hiện"))
                return (false, "Chỉ phân công khi hồ sơ chờ phân công / đã duyệt / đang thực hiện.");

            var dsNv = new List<int>();
            if (dto.MaNhanVienThucHiens != null && dto.MaNhanVienThucHiens.Count > 0)
                dsNv.AddRange(dto.MaNhanVienThucHiens.Distinct());
            else if (dto.MaNhanVienThucHien.HasValue && dto.MaNhanVienThucHien.Value > 0)
                dsNv.Add(dto.MaNhanVienThucHien.Value);
            if (dsNv.Count == 0)
                return (false, "Vui lòng chọn ít nhất một nhân viên thực hiện.");

            // Bắt buộc đã chọn ≥1 bước quy trình trước khi phân công SC
            var soBuocKeHoachSc = await _db.TienDoBuocQuyTrinhs
                .CountAsync(t => t.MaHoSoSuaChua == maHoSo && t.TrangThai == "DuocChon");
            if (soBuocKeHoachSc <= 0)
                return (false,
                    "Vui lòng chọn và Lưu các bước quy trình trong chi tiết hồ sơ trước khi phân công nhân viên.");

            var (okGhiChep, maGhiChep, loiGhiChep) = ResolveNguoiGhiChep(dsNv, dto.MaNhanVienGhiChep);
            if (!okGhiChep)
                return (false, loiGhiChep);

            // Không bắt buộc ngày/giờ dự kiến khi phân công
            if (dto.NgayBatDauDuKien.HasValue && dto.NgayKetThucDuKien.HasValue &&
                dto.NgayKetThucDuKien < dto.NgayBatDauDuKien)
                return (false, "Ngày kết thúc không được trước ngày bắt đầu.");

            // Hủy mềm PC cũ — không đổi khi đang chờ Xưởng
            var pcCu = await _repo.GetPhanCongTheoHoSoSuaChuaAsync(maHoSo);
            if (pcCu.Any(p => p.TrangThai == "Chờ xác nhận"))
                return (false, "Quy trình đang chờ Xưởng xác nhận — không được đổi phân công / người ghi chép.");

            var (okKhoaGc, loiKhoaGc) = await KiemTraKhoaNguoiGhiChepAsync(
                pcCu, maGhiChep, dsNv, maHoSoBaoTri: null, maHoSoSuaChua: maHoSo);
            if (!okKhoaGc)
                return (false, loiKhoaGc);

            foreach (var pc in pcCu.Where(p => p.TrangThai is "Đã phân công" or "Xác nhận" or "Đang thực hiện"))
                pc.TrangThai = "Đã hủy";

            int? maPhanCongDau = null;
            foreach (var maNv in dsNv)
            {
                var phanCong = new PhanCongCongViec
                {
                    MaNhanVienThucHien = maNv,
                    MaNhanVienPhanCong = nhanVienPhanCong.MaNhanVien,
                    MaHoSoSuaChua = maHoSo,
                    NgayBatDauDuKien = dto.NgayBatDauDuKien,
                    NgayKetThucDuKien = dto.NgayKetThucDuKien,
                    TrangThai = "Đã phân công",
                    // Mọi NV được phân công đều được Tiến hành quy trình
                    LaNguoiGhiChep = true,
                    NgayPhanCong = DateTime.Now
                };
                await _repo.AddPhanCongAsync(phanCong);
                await _repo.SaveChangesAsync();
                maPhanCongDau ??= phanCong.MaPhanCong;
            }

            hoSo.MaPhanCong = maPhanCongDau;
            // Giữ "Chờ phân công" trên hồ sơ cho đến khi NVKT bấm "Tiến hành sửa chữa"
            // → hồ sơ vẫn hiện trên thanh trạng thái Tổ trưởng để quản lý.
            if (hoSo.TrangThai is "Chờ phân công" or "Đã duyệt")
                hoSo.TrangThai = "Chờ phân công";
            if (hoSo.MaThieBiNavigation != null)
                hoSo.MaThieBiNavigation.TinhTrangHienTai = "Sửa chữa";

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        /// <summary>
        /// NVKT bấm "Tiến hành sửa chữa" → hồ sơ + mọi phân công đồng bộ "Đang thực hiện"
        /// (hiển thị cho mọi vai trò trên thanh trạng thái).
        /// </summary>
        public async Task<(bool, string?)> NhanVienBatDauSuaChuaAsync(int maHoSo, int maNguoiDung)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nhanVien == null) return (false, "Không xác định được nhân viên.");

            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai == "Đã hoàn thành" || hoSo.TrangThai == "Đã hủy" || hoSo.TrangThai == "Từ chối")
                return (false, "Hồ sơ đã kết thúc, không thể tiến hành.");

            var dsPc = await _repo.GetPhanCongTheoHoSoSuaChuaAsync(maHoSo);
            if (!LaNguoiDuocGhiChep(dsPc, nhanVien.MaNhanVien))
                return (false, "Bạn không được phân công trên hồ sơ này.");

            // Đồng bộ tất cả phân công còn mở + hồ sơ → Đang thực hiện
            foreach (var pc in dsPc.Where(p => p.TrangThai is not ("Đã hủy" or "Hoàn thành")))
                pc.TrangThai = "Đang thực hiện";

            hoSo.TrangThai = "Đang thực hiện";
            // Ghi nhận thời điểm bắt đầu thực tế lần đầu
            if (hoSo.ThoiDiemBatDauThucTe == null)
            {
                var now = DateTime.Now;
                hoSo.ThoiDiemBatDauThucTe = now;
                hoSo.GioBatDauDuKien = now.TimeOfDay;
            }
            if (hoSo.MaThieBiNavigation != null)
                hoSo.MaThieBiNavigation.TinhTrangHienTai = "Sửa chữa";

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        /// <summary>NVKT hoàn thành sửa chữa — đồng bộ mọi phân công + hồ sơ + thiết bị.</summary>
        public async Task<(bool, string?)> NhanVienHoanThanhSuaChuaAsync(int maHoSo, int maNguoiDung)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nhanVien == null) return (false, "Không xác định được nhân viên.");

            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");

            if (hoSo.TrangThai == "Đã hoàn thành")
                return (true, null);

            var dsPc = await _repo.GetPhanCongTheoHoSoSuaChuaAsync(maHoSo);
            if ((hoSo.TrangThai is "Đang thực hiện" or "Chờ xác nhận")
                && dsPc.Any(p => p.TrangThai == "Chờ xác nhận"))
                return (true, null);

            if (hoSo.TrangThai is not ("Đang thực hiện" or "Chờ phân công" or "Đã duyệt" or "Từ chối" or "Chờ xác nhận"))
                return (false, "Hồ sơ không ở trạng thái đang sửa chữa.");

            if (!LaNguoiDuocGhiChep(dsPc, nhanVien.MaNhanVien))
                return (false, "Chỉ người ghi chép quy trình mới được bấm Xong / gửi Xưởng.");

            // Xong → PC chờ Xưởng; HS giữ Đang thực hiện
            foreach (var pc in dsPc.Where(p => p.TrangThai is not ("Đã hủy" or "Hoàn thành")))
            {
                pc.TrangThai = "Chờ xác nhận";
                pc.LyDoTuChoi = null;
                pc.MaHoSoSuaChua ??= maHoSo;
            }

            hoSo.TrangThai = "Đang thực hiện";
            hoSo.LyDoTuChoi = null;

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
            GhiNhanKetThucThucTe(hoSo);
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

            var pc = h.MaPhanCongNavigation;
            // Nếu Include chưa load NV thực hiện — lấy lại đầy đủ
            if (pc != null && pc.MaNhanVienThucHienNavigation == null && h.MaPhanCong.HasValue)
                pc = await _repo.GetPhanCongByIdAsync(h.MaPhanCong.Value) ?? pc;

            // Danh sách NV: đang làm (cập nhật phân công) hoặc đã hoàn thành (hiển thị người thực hiện)
            var dsPc = await _repo.GetPhanCongTheoHoSoBaoTriAsync(h.MaHoSoBaoTri);
            var dsNvDangPc = dsPc
                .Where(p => p.TrangThai is "Đã phân công" or "Chờ xác nhận" or "Xác nhận" or "Đang thực hiện")
                .Select(p => new
                {
                    MaNhanVien = p.MaNhanVienThucHien,
                    TenNhanVien = p.MaNhanVienThucHienNavigation?.HoTen,
                    p.TrangThai,
                    p.MaPhanCong
                })
                .ToList();

            // NV đã hoàn thành bảo trì thiết bị này (chỉ khi hồ sơ Đã hoàn thành)
            var dsNvHoanThanh = h.TrangThai == "Đã hoàn thành"
                ? dsPc
                    .Where(p => p.TrangThai == "Hoàn thành")
                    .Select(p => new
                    {
                        MaNhanVien = p.MaNhanVienThucHien,
                        TenNhanVien = p.MaNhanVienThucHienNavigation?.HoTen,
                        p.TrangThai,
                        p.MaPhanCong
                    })
                    .ToList()
                : null;

            var tenNvHoanThanh = dsNvHoanThanh == null
                ? null
                : string.Join(", ", dsNvHoanThanh.Select(x => x.TenNhanVien).Where(t => !string.IsNullOrEmpty(t)));

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
                // NV đã hoàn thành (chỉ khi hồ sơ Đã hoàn thành)
                DanhSachNhanVienHoanThanh = dsNvHoanThanh,
                MaNhanVienHoanThanhs = dsNvHoanThanh?.Select(x => x.MaNhanVien).ToList(),
                TenNhanVienHoanThanhs = tenNvHoanThanh,
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

        /// <summary>
        /// Chỉ khóa đổi phân công khi đã có bước quy trình hoàn thành (DaXong/DaCapNhat)
        /// hoặc đang chờ Xưởng xác nhận. Không khóa vì cờ «người ghi chép» (đã bỏ nghiệp vụ đó).
        /// </summary>
        private async Task<(bool Ok, string? Loi)> KiemTraKhoaNguoiGhiChepAsync(
            List<PhanCongCongViec> pcHienTai,
            int maGhiChepMoi,
            List<int> dsNvMoi,
            int? maHoSoBaoTri,
            int? maHoSoSuaChua)
        {
            if (pcHienTai.Any(p => p.TrangThai == "Chờ xác nhận"))
                return (false, "Quy trình đang chờ Xưởng xác nhận — không được đổi phân công.");

            // Đã có bước Xong trên server → không cho bỏ NV đã làm bước
            if (maHoSoBaoTri.HasValue || maHoSoSuaChua.HasValue)
            {
                IQueryable<TienDoBuocQuyTrinh> q = _db.TienDoBuocQuyTrinhs;
                if (maHoSoBaoTri.HasValue)
                    q = q.Where(t => t.MaHoSoBaoTri == maHoSoBaoTri);
                else
                    q = q.Where(t => t.MaHoSoSuaChua == maHoSoSuaChua);

                var daLam = await q
                    .Where(t => t.TrangThai == "DaXong" || t.TrangThai == "DaCapNhat")
                    .Select(t => t.MaNhanVien)
                    .Distinct()
                    .ToListAsync();

                foreach (var maNv in daLam)
                {
                    if (!dsNvMoi.Contains(maNv))
                        return (false,
                            "Không được bỏ nhân viên đã hoàn thành bước quy trình khỏi phân công.");
                }
            }

            return (true, null);
        }

        /// <summary>
        /// Không bắt buộc chọn người ghi chép — mọi NV đều làm quy trình.
        /// Nếu client gửi mã hợp lệ thì dùng; không thì lấy NV đầu danh sách.
        /// </summary>
        private static (bool Ok, int MaGhiChep, string? Loi) ResolveNguoiGhiChep(
            List<int> dsNv, int? maNhanVienGhiChep)
        {
            if (dsNv.Count == 0)
                return (false, 0, "Vui lòng chọn ít nhất một nhân viên thực hiện.");
            if (maNhanVienGhiChep.HasValue &&
                maNhanVienGhiChep.Value > 0 &&
                dsNv.Contains(maNhanVienGhiChep.Value))
                return (true, maNhanVienGhiChep.Value, null);
            return (true, dsNv[0], null);
        }

        /// <summary>Mọi NV còn phân công hiệu lực đều được Tiến hành / Xong (không còn chỉ người ghi chép).</summary>
        private static bool LaNguoiDuocGhiChep(IEnumerable<PhanCongCongViec> dsPc, int maNhanVien)
        {
            var conHieuLuc = dsPc.Where(p => p.TrangThai is not ("Đã hủy" or "Hoàn thành")).ToList();
            if (conHieuLuc.Count == 0) return false;
            return conHieuLuc.Any(p => p.MaNhanVienThucHien == maNhanVien);
        }

        private static string FormatThoiLuongThucTe(DateTime batDau, DateTime ketThuc)
        {
            var span = ketThuc - batDau;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            var h = (int)span.TotalHours;
            return $"{h} giờ {span.Minutes} phút {span.Seconds} giây";
        }

        private static void GhiNhanKetThucThucTe(HoSoBaoTri hoSo)
        {
            var now = DateTime.Now;
            if (hoSo.ThoiDiemKetThucThucTe == null)
                hoSo.ThoiDiemKetThucThucTe = now;
            hoSo.GioKetThucDuKien = hoSo.ThoiDiemKetThucThucTe.Value.TimeOfDay;
            if (hoSo.ThoiDiemBatDauThucTe != null)
            {
                hoSo.ThoiGianDuKien = FormatThoiLuongThucTe(
                    hoSo.ThoiDiemBatDauThucTe.Value, hoSo.ThoiDiemKetThucThucTe.Value);
                hoSo.GioBatDauDuKien = hoSo.ThoiDiemBatDauThucTe.Value.TimeOfDay;
            }
        }

        private static void GhiNhanKetThucThucTe(HoSoSuaChua hoSo)
        {
            var now = DateTime.Now;
            if (hoSo.ThoiDiemKetThucThucTe == null)
                hoSo.ThoiDiemKetThucThucTe = now;
            hoSo.GioKetThucDuKien = hoSo.ThoiDiemKetThucThucTe.Value.TimeOfDay;
            if (hoSo.ThoiDiemBatDauThucTe != null)
            {
                hoSo.ThoiGianDuKien = FormatThoiLuongThucTe(
                    hoSo.ThoiDiemBatDauThucTe.Value, hoSo.ThoiDiemKetThucThucTe.Value);
                hoSo.GioBatDauDuKien = hoSo.ThoiDiemBatDauThucTe.Value.TimeOfDay;
            }
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
            // DangLam | DaXong (xong lần đầu) | DaCapNhat (đã lưu lại sau Xưởng từ chối)
            if (trangThai is not ("DangLam" or "DaXong" or "DaCapNhat"))
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

        /// <summary>
        /// Thống kê Giám đốc theo năm: số hồ sơ BT/SC theo tháng + số lượng & tiền vật tư.
        /// </summary>
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