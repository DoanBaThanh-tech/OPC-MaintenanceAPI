using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.DTOs.WorkOrder;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;
using OPC.MaintenanceAPI.DTOs.Common;
namespace OPC.MaintenanceAPI.Services.Implementations
{
    public class WorkOrderService : IWorkOrderService
    {
        private readonly IWorkOrderRepository _repo;
        public WorkOrderService(IWorkOrderRepository repo) => _repo = repo;

        // ===== BẢO TRÌ =====

        // Luồng 6B
        public async Task<(bool, string?)> TaoHoSoBaoTriAsync(TaoHoSoBaoTriDto dto)
        {
            var hoSo = new HoSoBaoTri
            {
                MaThieBi = dto.MaThietBi,
                MaNhanVienTao = dto.MaNhanVienTao,
                NoiDungCongViec = dto.NoiDungCongViec,
                ThoiGianDuKien = dto.ThoiGianDuKien,
                NgayTao = DateTime.Now,
                TrangThai = dto.GuiDuyet ? "Chờ duyệt" : "Nháp"
            };
            await _repo.AddHoSoBaoTriAsync(hoSo);
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        // Luồng 7
        public async Task<(bool, string?)> DuyetHoSoBaoTriAsync(int id, DuyetHoSoDto dto)
        {
            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (!dto.Duyet && string.IsNullOrWhiteSpace(dto.LyDoTuChoi))
                return (false, "Vui lòng nhập lý do từ chối.");

            hoSo.TrangThai = dto.Duyet ? "Đã duyệt" : "Từ chối";
            hoSo.LyDoTuChoi = dto.Duyet ? null : dto.LyDoTuChoi;
            hoSo.NgayDuyet = DateTime.Now;
            hoSo.MaNhanVienDuyet = dto.MaNhanVienDuyet;

            await _repo.AddLichSuPheDuyetAsync(new LichSuPheDuyet
            {
                MaHoSoBaoTri = id,
                MaNhanVienDuyet = dto.MaNhanVienDuyet,
                QuyetDinh = hoSo.TrangThai,
                LyDo = dto.LyDoTuChoi,
                NgayDuyet = DateTime.Now
            });

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        // Luồng 8
        public async Task<(bool, string?)> PhanCongBaoTriAsync(int maHoSo, PhanCongDto dto)
        {
            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Đã duyệt") return (false, "Hồ sơ chưa được duyệt.");
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

        // Luồng 9 — bước ghi nhận (dùng chung được cho cả bảo trì lẫn sửa chữa vì cùng bảng PhanCongCongViec)
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

        // Luồng 9 — Tổ trưởng xác nhận đóng hồ sơ bảo trì
        public async Task<(bool, string?)> XacNhanHoanThanhBaoTriAsync(int maHoSo, XacNhanDto dto)
        {
            var hoSo = await _repo.GetHoSoBaoTriByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");

            if (!dto.Dat)
            {
                hoSo.TrangThai = "Đang thực hiện"; // yêu cầu làm lại
                await _repo.SaveChangesAsync();
                return (true, "Yêu cầu nhân viên thực hiện lại.");
            }

            hoSo.TrangThai = "Đã hoàn thành";
            if (hoSo.MaThieBiNavigation != null)
                hoSo.MaThieBiNavigation.NgayBaoTriGanNhat = DateOnly.FromDateTime(DateTime.Now);
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        // ===== SỬA CHỮA =====

        // Luồng 10
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

        // Luồng 11
        public async Task<(bool, string?)> DuyetHoSoSuaChuaAsync(int id, DuyetHoSoDto dto)
        {
            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (!dto.Duyet && string.IsNullOrWhiteSpace(dto.LyDoTuChoi))
                return (false, "Vui lòng nhập lý do từ chối.");

            hoSo.TrangThai = dto.Duyet ? "Đã duyệt" : "Từ chối";
            hoSo.LyDoTuChoi = dto.Duyet ? null : dto.LyDoTuChoi;
            hoSo.NgayDuyet = DateTime.Now;
            hoSo.MaNhanVienDuyet = dto.MaNhanVienDuyet;

            await _repo.AddLichSuPheDuyetAsync(new LichSuPheDuyet
            {
                MaHoSoSuaChua = id,
                MaNhanVienDuyet = dto.MaNhanVienDuyet,
                QuyetDinh = hoSo.TrangThai,
                LyDo = dto.LyDoTuChoi,
                NgayDuyet = DateTime.Now
            });

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        // Luồng 15
        public async Task<(bool, string?)> PhanCongSuaChuaAsync(int maHoSo, PhanCongDto dto)
        {
            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
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

        // Luồng 16
        public async Task<(bool, string?)> XacNhanHoanThanhSuaChuaAsync(int maHoSo, XacNhanDto dto)
        {
            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");

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
    }
}