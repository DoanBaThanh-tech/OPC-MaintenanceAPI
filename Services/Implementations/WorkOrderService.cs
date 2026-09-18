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
                TrangThai = dto.GuiDuyet ? "Chờ duyệt" : "Nháp"
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

            _repo.SetHoSoBaoTriRowVersion(hoSo, Convert.FromBase64String(dto.RowVersion));

            hoSo.MaNhanVienDuyet = nhanVienDuyet.MaNhanVien;
            hoSo.NgayDuyet = DateTime.Now;
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

            if (await _repo.NhanVienKhongTheNhanThemAsync(dto.MaNhanVienThucHien))
            {
                var soCv = await _repo.DemCongViecDangThucHienAsync(dto.MaNhanVienThucHien);
                return (false,
                    $"Nhân viên này đang đảm nhận {soCv}/{WorkOrderRepository.SoThietBiToiDaMoiNhanVien} thiết bị. " +
                    "Chỉ được chọn lại khi đã hoàn thành hết (về 0/3).");
            }

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
            if (phanCong.MaNhanVienThucHien != nhanVien.MaNhanVien)
                return (false, "Bạn không được phân công hồ sơ này.");
            if (phanCong.TrangThai != "Chờ xác nhận" && phanCong.TrangThai != "Đã phân công")
                return (false, "Phân công không ở trạng thái chờ xác nhận.");

            hoSo.TrangThai = "Đang thực hiện";
            phanCong.TrangThai = "Đang thực hiện";

            if (hoSo.MaThieBiNavigation != null)
                hoSo.MaThieBiNavigation.TinhTrangHienTai = "Bảo trì";

            var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
            if (chiTiet != null)
                chiTiet.TrangThai = "Đang thực hiện";

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool, string?)> CapNhatHoSoBaoTriBiTuChoiAsync(int id, CapNhatHoSoBaoTriDto dto)
        {
            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Từ chối")
                return (false, "Chỉ được chỉnh sửa hồ sơ ở trạng thái Từ chối.");

            if (!string.IsNullOrWhiteSpace(dto.NoiDungCongViec))
                hoSo.NoiDungCongViec = dto.NoiDungCongViec.Trim();
            if (dto.ThoiGianDuKien != null)
                hoSo.ThoiGianDuKien = dto.ThoiGianDuKien;

            // Gửi lại duyệt
            hoSo.TrangThai = "Chờ duyệt";
            hoSo.LyDoTuChoi = null;
            hoSo.MaNhanVienDuyet = null;
            hoSo.NgayDuyet = null;

            var chiTiet = await _repo.GetChiTietKeHoachByHoSoBaoTriAsync(hoSo.MaHoSoBaoTri);
            if (chiTiet != null)
                chiTiet.TrangThai = "Chờ duyệt";

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<List<object>> GetLichSuPhanCongAsync()
        {
            var list = await _repo.GetLichSuPhanCongAsync();
            return list.Select(p => (object)new
            {
                p.MaPhanCong,
                TenNhanVienPhanCong = p.MaNhanVienPhanCongNavigation?.HoTen,
                TenNhanVienThucHien = p.MaNhanVienThucHienNavigation?.HoTen,
                p.TrangThai,
                p.NgayPhanCong,
                GioBatDau = p.NgayBatDauDuKien,
                GioKetThuc = p.NgayKetThucDuKien,
            }).ToList();
        }

        public async Task<(bool, string?)> GhiNhanKetQuaAsync(int maPhanCong, GhiNhanKetQuaDto dto)
        {
            var phanCong = await _repo.GetPhanCongByIdAsync(maPhanCong);
            if (phanCong == null) return (false, "Không tìm thấy công việc.");
            if (string.IsNullOrWhiteSpace(dto.SoLieuGhiNhan))
                return (false, "Vui lòng nhập kết quả thực hiện.");

            await _repo.AddKetQuaAsync(new KetQuaThucHien
            {
                MaPhanCong = maPhanCong,
                MaNhanVienGhiNhan = dto.MaNhanVienGhiNhan,
                SoLieuGhiNhan = dto.SoLieuGhiNhan,
                HinhAnh = dto.HinhAnh,
                GhiChu = dto.GhiChu,
                NgayGhiNhan = DateTime.Now,
                XacNhanHoanThanh = true
            });
            phanCong.TrangThai = "Hoàn thành";
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
            if (hoSo.MaThieBiNavigation != null)
                hoSo.MaThieBiNavigation.TinhTrangHienTai = "Sản xuất";
            if (chiTiet != null)
                chiTiet.TrangThai = "Đã hoàn thành";

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

            if (await _repo.NhanVienKhongTheNhanThemAsync(dto.MaNhanVienThucHien))
            {
                var soCv = await _repo.DemCongViecDangThucHienAsync(dto.MaNhanVienThucHien);
                return (false,
                    $"Nhân viên này đang đảm nhận {soCv}/{WorkOrderRepository.SoThietBiToiDaMoiNhanVien} thiết bị. " +
                    "Chỉ được chọn lại khi đã hoàn thành hết (về 0/3).");
            }

            var phanCong = new PhanCongCongViec
            {
                MaNhanVienThucHien = dto.MaNhanVienThucHien,
                MaNhanVienPhanCong = dto.MaNhanVienPhanCong,
                NgayBatDauDuKien = dto.NgayBatDauDuKien,
                NgayKetThucDuKien = dto.NgayKetThucDuKien,
                TrangThai = "Đã phân công",
                NgayPhanCong = DateTime.Now
            };
            await _repo.AddPhanCongAsync(phanCong);
            await _repo.SaveChangesAsync();

            hoSo.MaPhanCong = phanCong.MaPhanCong;
            hoSo.TrangThai = "Đang thực hiện";

            // Khi phân công sửa chữa → trạng thái thiết bị = "Sửa chữa"
            if (hoSo.MaThieBiNavigation != null)
                hoSo.MaThieBiNavigation.TinhTrangHienTai = "Sửa chữa";

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