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
                if (await _repo.TonTaiHoSoBaoTriTheoThietBiThangAsync(dto.MaThietBi, nam, thang))
                    return (false,
                        $"Thiết bị này đã có hồ sơ bảo trì trong tháng {thang}/{nam}. Không thể tạo thêm hồ sơ.");
            }

            TimeSpan? gioBatDau = null;
            TimeSpan? gioKetThuc = null;
            if (!string.IsNullOrWhiteSpace(dto.GioBatDauDuKien) && TimeSpan.TryParse(dto.GioBatDauDuKien, out var gbd))
                gioBatDau = gbd;
            if (!string.IsNullOrWhiteSpace(dto.GioKetThucDuKien) && TimeSpan.TryParse(dto.GioKetThucDuKien, out var gkt))
                gioKetThuc = gkt;

            // GuiDuyet = true → gửi xưởng xem lịch, trạng thái vẫn Chờ duyệt (GĐ duyệt sau)
            var trangThai = dto.GuiDuyet ? "Chờ duyệt" : "Nháp";

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
                    tbTrangThai.TinhTrangHienTai = "Bảo trì";
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
                    chiTiet.NgayDuKienBaoTri = dto.NgayDuKienBaoTri.Value;
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
            if (hoSo.TrangThai is not ("Chờ duyệt" or "Chờ GĐ duyệt"))
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

            // Danh sách nhân viên được chọn (nhiều người)
            var dsNv = new List<int>();
            if (dto.MaNhanVienThucHiens != null && dto.MaNhanVienThucHiens.Count > 0)
                dsNv.AddRange(dto.MaNhanVienThucHiens.Distinct());
            else if (dto.MaNhanVienThucHien.HasValue && dto.MaNhanVienThucHien.Value > 0)
                dsNv.Add(dto.MaNhanVienThucHien.Value);

            if (dsNv.Count == 0)
                return (false, "Vui lòng chọn ít nhất một nhân viên thực hiện.");

            // Nếu đã có phân công đang mở → hủy mềm các bản cũ rồi phân công lại
            var pcDangMo = await _repo.GetPhanCongTheoHoSoBaoTriAsync(maHoSo);
            foreach (var pc in pcDangMo.Where(p => p.TrangThai is "Chờ xác nhận" or "Đã phân công" or "Xác nhận" or "Đang thực hiện"))
            {
                pc.TrangThai = "Đã hủy";
            }

            if (await _repo.ThietBiDangTrongQuyTrinhKhacAsync(hoSo.MaThieBi, "BaoTri", maHoSo))
                return (false, "Thiết bị này đang trong quy trình bảo trì/sửa chữa khác, không thể phân công.");

            if (dto.NgayKetThucDuKien < dto.NgayBatDauDuKien)
                return (false, "Ngày kết thúc không được trước ngày bắt đầu.");

            int? maPhanCongDau = null;
            foreach (var maNv in dsNv)
            {
                var phanCong = new PhanCongCongViec
                {
                    MaNhanVienThucHien = maNv,
                    MaNhanVienPhanCong = nhanVienPhanCong.MaNhanVien,
                    MaHoSoBaoTri = maHoSo,
                    NgayBatDauDuKien = dto.NgayBatDauDuKien,
                    NgayKetThucDuKien = dto.NgayKetThucDuKien,
                    // Không cần NV xác nhận/từ chối — hiện lên là biết phải làm
                    TrangThai = "Đã phân công",
                    NgayPhanCong = DateTime.Now
                };
                await _repo.AddPhanCongAsync(phanCong);
                await _repo.SaveChangesAsync();
                maPhanCongDau ??= phanCong.MaPhanCong;
            }

            // Giữ MaPhanCong (1) để tương thích code cũ; danh sách đầy đủ qua MaHoSoBaoTri
            hoSo.MaPhanCong = maPhanCongDau;
            // Phân công xong → Đang thực hiện ngay (không chờ xác nhận NV)
            hoSo.TrangThai = "Đang thực hiện";

            var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
            if (chiTiet != null)
                chiTiet.TrangThai = "Đang thực hiện";

            await _repo.SaveChangesAsync();
            return (true, null);
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
            if (hoSo.TrangThai is not ("Đang thực hiện" or "Đã duyệt"))
                return (false, "Hồ sơ không ở trạng thái đang thực hiện.");

            var dsPc = await _repo.GetPhanCongTheoHoSoBaoTriAsync(maHoSo);
            if (dsPc.Count == 0 && hoSo.MaPhanCong != null)
            {
                var pc = await _repo.GetPhanCongByIdAsync(hoSo.MaPhanCong.Value);
                if (pc != null) dsPc.Add(pc);
            }

            var duocPhanCong = dsPc.Any(p => p.MaNhanVienThucHien == nhanVien.MaNhanVien
                && p.TrangThai is not ("Đã hủy" or "Hoàn thành"));
            if (!duocPhanCong)
                return (false, "Bạn không được phân công hồ sơ này hoặc đã hoàn thành.");

            foreach (var pc in dsPc.Where(p => p.TrangThai is not ("Đã hủy" or "Hoàn thành")))
                pc.TrangThai = "Hoàn thành";

            hoSo.TrangThai = "Đã hoàn thành";
            var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
            if (chiTiet != null)
                chiTiet.TrangThai = "Đã hoàn thành";

            var conHoSoMo = await _repo.CoHoSoBaoTriDangMoAsync(hoSo.MaThieBi, loaiTruMaHoSo: maHoSo);
            if (!conHoSoMo)
            {
                var tb = await _repo.GetThietBiByIdAsync(hoSo.MaThieBi);
                if (tb != null)
                    tb.TinhTrangHienTai = "Sản xuất";
            }

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
            hoSo.NoiDungCongViec = dto.NoiDungCongViec.Trim();
            if (dto.ThoiGianDuKien != null)
            {
                var raw = dto.ThoiGianDuKien.Trim();
                // Chỉ nhận số giờ thuần (có thể kèm "giờ")
                var soStr = raw.Replace("giờ", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (!decimal.TryParse(soStr, System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out var soGio) || soGio <= 0)
                    return (false, "Giờ dự kiến phải là số dương lớn hơn 0.");
                if (soGio > 24)
                    return (false, "Bảo trì trong ngày — thời gian dự kiến tối đa 24 giờ.");
                hoSo.ThoiGianDuKien = soGio.ToString("0.#");
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

                // Ràng buộc: 1 thiết bị chỉ 1 lần bảo trì / tháng
                // (VD: GĐ yêu cầu chuyển sang T9 nhưng T9 đã có hồ sơ/kế hoạch → không cho)
                var namMoi = ngayMoi.Year;
                var thangMoi = ngayMoi.Month;
                var thangCu = chiTiet.NgayDuKienBaoTri.Month;
                var namCu = chiTiet.NgayDuKienBaoTri.Year;
                if (thangMoi != thangCu || namMoi != namCu)
                {
                    var daCo = await _repo.TonTaiChiTietHoacHoSoThietBiThangKhacAsync(
                        hoSo.MaThieBi, namMoi, thangMoi,
                        excludeMaChiTiet: chiTiet.MaChiTietKeHoach,
                        excludeMaHoSo: hoSo.MaHoSoBaoTri);
                    if (daCo)
                        return (false,
                            $"Thiết bị này đã có kế hoạch hoặc hồ sơ bảo trì trong tháng {thangMoi}/{namMoi}. " +
                            "Mỗi thiết bị chỉ được bảo trì một lần trong một tháng — không thể chuyển hồ sơ sang tháng đó. " +
                            "Vui lòng chọn tháng khác hoặc liên hệ giám đốc.");
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
                var bt = p.HoSoBaoTri ?? p.MaHoSoBaoTriNavigation;
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

            // PhanCongDto.MaNhanVienThucHien giờ là int? (hỗ trợ nhiều NV ở bảo trì)
            var maNvThucHien = dto.MaNhanVienThucHien
                ?? dto.MaNhanVienThucHiens?.FirstOrDefault()
                ?? 0;
            if (maNvThucHien <= 0)
                return (false, "Vui lòng chọn nhân viên thực hiện.");

            var phanCong = new PhanCongCongViec
            {
                MaNhanVienThucHien = maNvThucHien,
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

            var pc = h.MaPhanCongNavigation;
            // Nếu Include chưa load NV thực hiện — lấy lại đầy đủ
            if (pc != null && pc.MaNhanVienThucHienNavigation == null && h.MaPhanCong.HasValue)
                pc = await _repo.GetPhanCongByIdAsync(h.MaPhanCong.Value) ?? pc;

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
                h.TrangThai,
                h.LyDoTuChoi,
                h.NgayTao,
                h.NgayDuyet,
                h.MaPhanCong,
                // Thông tin phân công (để hiện Từ chối + lý do trên chi tiết hồ sơ)
                TrangThaiPhanCong = pc?.TrangThai,
                LyDoTuChoiPhanCong = pc?.LyDoTuChoi,
                MaNhanVienThucHien = pc?.MaNhanVienThucHien,
                TenNhanVienThucHien = pc?.MaNhanVienThucHienNavigation?.HoTen,
                NgayPhanCong = pc?.NgayPhanCong,
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

    }
}