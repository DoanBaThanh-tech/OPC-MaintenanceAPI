using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.DTOs.MaintenancePlan;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public class MaintenancePlanService : IMaintenancePlanService
    {
        private readonly IMaintenancePlanRepository _repo;
        private readonly INhanVienRepository _nhanVienRepo;

        public MaintenancePlanService(IMaintenancePlanRepository repo, INhanVienRepository nhanVienRepo)
        {
            _repo = repo;
            _nhanVienRepo = nhanVienRepo;
        }

        public async Task<(bool, string?)> TaoYeuCauNgayBaoTriAsync(int maNguoiDungTao, TaoYeuCauNgayBaoTriDto dto)
        {
            if (dto.Nam < 2000 || dto.Nam > 2100)
                return (false, "Năm bảo trì không hợp lệ.");
            if (dto.NgayBaoTri.Year != dto.Nam)
                return (false, "Ngày bảo trì phải thuộc đúng năm đã chọn.");

            var thietBi = await _repo.GetThietBiAsync(dto.MaThietBi);
            if (thietBi == null) return (false, "Không tìm thấy thiết bị.");
            if (await _repo.TonTaiKeHoachTheoThietBiNamAsync(dto.MaThietBi, dto.Nam))
                return (false, "Thiết bị này đã có kế hoạch trong năm đã chọn.");
            if (await _repo.TonTaiYeuCauTheoThietBiNamAsync(dto.MaThietBi, dto.Nam))
                return (false, "Thiết bị này đã có đăng ký ngày bảo trì trong năm đã chọn.");

            var soThang = thietBi.MaChuKyNavigation.SoThangChuKyDeXuat;
            if (soThang < 1 || soThang > 12)
                return (false, "Thiết bị chưa có chu kỳ bảo trì hợp lệ.");

            var ngayDuKien = thietBi.NgayBaoTriTiepTheo;
            if (!ngayDuKien.HasValue && thietBi.NgayBaoTriGanNhat.HasValue)
                ngayDuKien = thietBi.NgayBaoTriGanNhat.Value.AddMonths(soThang);
            if (ngayDuKien.HasValue)
            {
                var tuNgay = ngayDuKien.Value.AddMonths(-1);
                var denNgay = ngayDuKien.Value.AddMonths(1);
                if (dto.NgayBaoTri < tuNgay || dto.NgayBaoTri > denNgay)
                    return (false, $"Ngày bảo trì phải nằm trong khoảng {tuNgay:dd/MM/yyyy} đến {denNgay:dd/MM/yyyy} theo chu kỳ thiết bị.");
            }

            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungTao);
            if (nhanVien == null) return (false, "Không xác định được người đăng ký.");

            await _repo.AddYeuCauAsync(new YeuCauNgayBaoTri
            {
                MaThietBi = dto.MaThietBi,
                MaNhanVienTao = nhanVien.MaNhanVien,
                Nam = dto.Nam,
                NgayBaoTri = dto.NgayBaoTri,
                TrangThai = "Chờ lập kế hoạch"
            });
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<List<YeuCauNgayBaoTriResponseDto>> GetYeuCauChoLapKeHoachAsync() =>
            (await _repo.GetYeuCauChoLapKeHoachAsync()).Select(x => new YeuCauNgayBaoTriResponseDto
            {
                MaYeuCauNgayBaoTri = x.MaYeuCauNgayBaoTri,
                TenThietBi = x.MaThietBiNavigation?.TenThietBi ?? string.Empty,
                LoaiThietBi = x.MaThietBiNavigation?.LoaiThietBi ?? string.Empty,   
                MaChuKy = x.MaThietBiNavigation?.MaChuKy ?? 0, // Thêm ?? 0 (hoặc ?? "") tùy kiểu dữ liệu của DTO
                SoThangChuKy = x.MaThietBiNavigation?.MaChuKyNavigation?.SoThangChuKyDeXuat ?? 0,
                Nam = x.Nam,
                NgayBaoTri = x.NgayBaoTri,
                TrangThai = x.TrangThai
            }).ToList();

        public async Task<(bool, string?)> LapKeHoachTuYeuCauAsync(int maNguoiDungTao, LapKeHoachDto dto)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungTao);
            if (nhanVien == null) return (false, "Không xác định được người lập kế hoạch.");

            var yeuCau = await _repo.GetYeuCauAsync(dto.MaYeuCauNgayBaoTri);
            if (yeuCau == null || yeuCau.TrangThai != "Chờ lập kế hoạch")
                return (false, "Đăng ký ngày bảo trì không tồn tại hoặc đã được lập kế hoạch.");
            if (await _repo.TonTaiKeHoachTheoThietBiNamAsync(yeuCau.MaThietBi, yeuCau.Nam))
                return (false, "Thiết bị này đã có kế hoạch trong năm đã chọn.");

            var keHoach = new KeHoachBaoTri
            {
                MaChuKy = yeuCau.MaThietBiNavigation!.MaChuKy,
                Nam = yeuCau.Nam,
                MaNhanVienLap = nhanVien.MaNhanVien,
                NgayLapKeHoach = DateOnly.FromDateTime(DateTime.Now),
                TrangThai = "Đang lập"
            };
            await _repo.AddKeHoachAsync(keHoach);
            await _repo.SaveChangesAsync();

            await _repo.AddChiTietRangeAsync(new[] { new ChiTietKeHoachBaoTri
            {
                MaKeHoach = keHoach.MaKeHoach,
                MaThietBi = yeuCau.MaThietBi,
                NgayDuKienBaoTri = yeuCau.NgayBaoTri
            }});
            await _repo.SaveChangesAsync();

            yeuCau.TrangThai = "Đã lập kế hoạch";
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<List<ChiTietKeHoachDto>> GetChiTietChuaCoHoSoAsync() =>
            (await _repo.GetChiTietChuaCoHoSoAsync()).Select(MapChiTiet).ToList();

        public async Task<List<ChiTietKeHoachDto>> GetChiTietTheoKeHoachAsync(int maKeHoach) =>
            (await _repo.GetChiTietTheoKeHoachAsync(maKeHoach)).Select(MapChiTiet).ToList();

        public async Task<List<KeHoachResponseDto>> GetAllKeHoachAsync() =>
            (await _repo.GetAllKeHoachAsync()).Select(k => new KeHoachResponseDto
            {
                MaKeHoach = k.MaKeHoach,
                MaChuKy = k.MaChuKy,
                TenChuKy = k.MaChuKyNavigation?.LoaiThietBi,
                TenThietBi = k.ChiTietKeHoachBaoTris.FirstOrDefault()?.MaThietBiNavigation?.TenThietBi,
                Nam = k.Nam,
                TenNhanVienLap = k.MaNhanVienLapNavigation?.HoTen,
                NgayLapKeHoach = k.NgayLapKeHoach,
                TrangThai = k.TrangThai ?? "Chưa xác định",
                SoThietBi = k.ChiTietKeHoachBaoTris?.Count ?? 0
            }).ToList();

        public async Task<List<ChuKyResponseDto>> GetAllChuKyAsync() =>
            (await _repo.GetAllChuKyAsync()).Select(c => new ChuKyResponseDto
            {
                MaChuKy = c.MaChuKy,
                LoaiThietBi = c.LoaiThietBi,
                SoThangChuKyDeXuat = c.SoThangChuKyDeXuat,
                MoTa = c.MoTa
            }).ToList();

        private static ChiTietKeHoachDto MapChiTiet(ChiTietKeHoachBaoTri c) => new()
        {
            MaChiTietKeHoach = c.MaChiTietKeHoach,
            MaThietBi = c.MaThietBi,
            TenThietBi = c.MaThietBiNavigation?.TenThietBi,
            NgayDuKienBaoTri = c.NgayDuKienBaoTri,
            MaHoSoBaoTri = c.MaHoSoBaoTri
        };
    }
}