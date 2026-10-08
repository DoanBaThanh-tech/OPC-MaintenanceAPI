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

        // ---------- từ XuLyBaoTri.cs ----------

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

            await LuuBuocDuocChonKhiTaoAsync(hoSo.MaHoSoBaoTri, null, dto.DanhSachBuoc);
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
    }
}
