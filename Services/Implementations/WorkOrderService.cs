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
                if (chiTiet.MaHoSoBaoTri != null) return (false, "Dòng kế hoạch này đã có hồ sơ bảo trì.");
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
                    h.NoiDungCongViec, h.ThoiGianDuKien, h.TrangThai, h.NgayTao,
                    h.MaPhanCong,
                    Nam = namThucTe,
                    NamTuKeHoach = namTuKeHoach != null
                });
            }
            return ketQua;
        }

        // Luồng 7 — có Optimistic Concurrency Control qua RowVersion
        public async Task<(bool, string?)> DuyetHoSoBaoTriAsync(int id, DuyetHoSoDto dto)
        {
            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Chờ duyệt")
                return (false, "Hồ sơ đã được xử lý trước đó.");
            if (dto.QuyetDinh != "Duyệt" && dto.QuyetDinh != "Từ chối")
                return (false, "QuyetDinh chỉ nhận 'Duyệt' hoặc 'Từ chối'.");
            if (dto.QuyetDinh == "Từ chối" && string.IsNullOrWhiteSpace(dto.LyDo))
                return (false, "Vui lòng nhập lý do từ chối.");

            _repo.SetHoSoBaoTriRowVersion(hoSo, Convert.FromBase64String(dto.RowVersion));

            hoSo.MaNhanVienDuyet = dto.MaNhanVienDuyet;
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
                MaHoSoBaoTri = hoSo.MaHoSoBaoTri,
                MaNhanVienDuyet = dto.MaNhanVienDuyet,
                QuyetDinh = dto.QuyetDinh,
                LyDo = dto.LyDo,
                NgayDuyet = DateTime.Now
            });
            await _repo.SaveChangesAsync();

            return (true, null);
        }

        public async Task<(bool, string?)> PhanCongBaoTriAsync(int maHoSo, PhanCongDto dto)
        {
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
                MaNhanVienPhanCong = dto.MaNhanVienPhanCong,
                NgayBatDauDuKien = DateOnly.FromDateTime(dto.NgayBatDauDuKien),
                NgayKetThucDuKien = DateOnly.FromDateTime(dto.NgayKetThucDuKien),
                TrangThai = "Đã phân công",
                NgayPhanCong = DateTime.Now
            };
            await _repo.AddPhanCongAsync(phanCong);
            await _repo.SaveChangesAsync();

            hoSo.MaPhanCong = phanCong.MaPhanCong;
            hoSo.TrangThai = "Đang thực hiện";
            await _repo.SaveChangesAsync();
            return (true, null);
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

            if (!dto.Dat)
            {
                hoSo.TrangThai = "Đang thực hiện";
                await _repo.SaveChangesAsync();
                return (true, "Yêu cầu nhân viên thực hiện lại.");
            }

            hoSo.TrangThai = "Đã hoàn thành";
            if (hoSo.MaThieBiNavigation != null)
            {
                var homNay = DateOnly.FromDateTime(DateTime.Now);
                hoSo.MaThieBiNavigation.NgayBaoTriGanNhat = homNay;

                var soThang = await _repo.GetSoThangChuKyAsync(hoSo.MaThieBiNavigation.LoaiThietBi);
                if (soThang.HasValue)
                    hoSo.MaThieBiNavigation.NgayBaoTriTiepTheo = homNay.AddMonths(soThang.Value);
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
        public async Task<(bool, string?)> DuyetHoSoSuaChuaAsync(int id, DuyetHoSoDto dto)
        {
            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Chờ duyệt")
                return (false, "Hồ sơ đã được xử lý trước đó.");
            if (dto.QuyetDinh != "Duyệt" && dto.QuyetDinh != "Từ chối")
                return (false, "QuyetDinh chỉ nhận 'Duyệt' hoặc 'Từ chối'.");
            if (dto.QuyetDinh == "Từ chối" && string.IsNullOrWhiteSpace(dto.LyDo))
                return (false, "Vui lòng nhập lý do từ chối.");

            _repo.SetHoSoSuaChuaRowVersion(hoSo, Convert.FromBase64String(dto.RowVersion));

            hoSo.MaNhanVienDuyet = dto.MaNhanVienDuyet;
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
                MaNhanVienDuyet = dto.MaNhanVienDuyet,
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
                NgayBatDauDuKien = DateOnly.FromDateTime(dto.NgayBatDauDuKien),
                NgayKetThucDuKien = DateOnly.FromDateTime(dto.NgayKetThucDuKien),
                TrangThai = "Đã phân công",
                NgayPhanCong = DateTime.Now
            };
            await _repo.AddPhanCongAsync(phanCong);
            await _repo.SaveChangesAsync();

            hoSo.MaPhanCong = phanCong.MaPhanCong;
            hoSo.TrangThai = "Đang thực hiện";
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
            if (hoSo.MaThieBiNavigation != null)
                hoSo.MaThieBiNavigation.TinhTrangHienTai = "Hoạt động tốt";
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        // ===== TRUY VẤN =====

        public async Task<List<object>> GetHoSoBaoTriTheoTrangThaiAsync(string? trangThai) =>
        (await _repo.GetHoSoBaoTriByTrangThaiAsync(trangThai)).Select(h => (object)new
        {
            h.MaHoSoBaoTri,
            MaThietBi = h.MaThieBi,   // alias đúng chính tả cho JSON
            TenThietBi = h.MaThieBiNavigation?.TenThietBi,
            h.NoiDungCongViec, h.ThoiGianDuKien, h.TrangThai, h.NgayTao
        }).ToList();

        public async Task<object?> GetHoSoBaoTriByIdAsync(int id)
        {
            var h = await _repo.GetHoSoBaoTriByIdAsync(id);
            if (h == null) return null;
            var namTuKeHoach = await _repo.GetNamKeHoachTheoHoSoBaoTriAsync(h.MaHoSoBaoTri);
            return new
            {
                h.MaHoSoBaoTri,
                MaThietBi = h.MaThieBi,
                TenThietBi = h.MaThieBiNavigation?.TenThietBi,
                h.NoiDungCongViec, h.ThoiGianDuKien, h.TrangThai, h.LyDoTuChoi,
                h.NgayTao, h.NgayDuyet, h.MaPhanCong,
                RowVersion = Convert.ToBase64String(h.RowVersion),
                Nam = namTuKeHoach ?? h.NgayTao.Year,
                NamTuKeHoach = namTuKeHoach != null
            };
        }
    }
}