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

        // Thay thế TaoYeuCauNgayBaoTriAsync + LapKeHoachTuYeuCauAsync cũ.
        // Tổ trưởng chọn chu kỳ + năm + danh sách thiết bị (kèm ngày dự kiến) trong 1 lần.
        public async Task<(bool, string?)> TaoKeHoachAsync(int maNguoiDungTao, TaoKeHoachDto dto)
        {
            if (dto.Nam < 2000 || dto.Nam > 2100)
                return (false, "Năm không hợp lệ.");
            if (dto.ThietBiDuocChon == null || dto.ThietBiDuocChon.Count == 0)
                return (false, "Vui lòng chọn ít nhất 1 thiết bị.");

            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungTao);
            if (nhanVien == null) return (false, "Không xác định được người lập kế hoạch.");

            // Validate từng thiết bị trước khi tạo bất kỳ dữ liệu nào
            foreach (var ct in dto.ThietBiDuocChon)
            {
                var thietBi = await _repo.GetThietBiAsync(ct.MaThietBi);
                if (thietBi == null)
                    return (false, $"Không tìm thấy thiết bị #{ct.MaThietBi}.");

                if (await _repo.TonTaiKeHoachTheoThietBiNamAsync(ct.MaThietBi, dto.Nam))
                    return (false, $"Thiết bị '{thietBi.TenThietBi}' đã có kế hoạch trong năm {dto.Nam}.");

                if (ct.NgayDuKienBaoTri.Year != dto.Nam)
                    return (false, $"Ngày dự kiến bảo trì của '{thietBi.TenThietBi}' phải thuộc năm {dto.Nam}.");
            }

            var keHoach = new KeHoachBaoTri
            {
                MaChuKy = dto.MaChuKy,
                Nam = dto.Nam,
                MaNhanVienLap = nhanVien.MaNhanVien,
                NgayLapKeHoach = DateOnly.FromDateTime(DateTime.Now),
                TrangThai = "Đang lập"
            };
            await _repo.AddKeHoachAsync(keHoach);
            await _repo.SaveChangesAsync();

            var chiTiets = dto.ThietBiDuocChon.Select(ct => new ChiTietKeHoachBaoTri
            {
                MaKeHoach = keHoach.MaKeHoach,
                MaThietBi = ct.MaThietBi,
                NgayDuKienBaoTri = ct.NgayDuKienBaoTri
            });
            await _repo.AddChiTietRangeAsync(chiTiets);
            await _repo.SaveChangesAsync();

            return (true, null);
        }
        public async Task<List<int>> GetDanhSachNamDaLapAsync() => await _repo.GetDistinctNamAsync();
        public async Task<List<ChiTietKeHoachDto>> GetChiTietChuaCoHoSoAsync() =>
            (await _repo.GetChiTietChuaCoHoSoAsync()).Select(MapChiTiet).ToList();

        public async Task<List<ChiTietKeHoachDto>> GetChiTietTheoKeHoachAsync(int maKeHoach) =>
            (await _repo.GetChiTietTheoKeHoachAsync(maKeHoach)).Select(MapChiTiet).ToList();

        public async Task<(bool, string?)> ThemLanBaoTriAsync(int maKeHoach, ThemLanBaoTriDto dto)
        {
            var keHoach = await _repo.GetKeHoachByIdAsync(maKeHoach);
            if (keHoach == null) return (false, "Không tìm thấy kế hoạch.");

            if (dto.NgayDuKienBaoTri.Year != keHoach.Nam)
                return (false, $"Lần bảo trì tiếp theo đã vượt qua năm {keHoach.Nam}. " +
                   $"Vui lòng lập kế hoạch bảo trì cho năm {dto.NgayDuKienBaoTri.Year} để tiếp tục.");

            var thietBi = await _repo.GetThietBiAsync(dto.MaThietBi);
            if (thietBi == null) return (false, "Không tìm thấy thiết bị.");

            // Điều kiện: thiết bị phải đã tồn tại trong kế hoạch này rồi (không cho thêm thiết bị lạ
            // qua đường này — thiết bị mới phải đi qua nút "Lập kế hoạch mới")
            var ngayGanNhat = await _repo.GetNgayBaoTriGanNhatAsync(maKeHoach, dto.MaThietBi);
            if (ngayGanNhat == null)
                return (false, "Thiết bị này chưa có trong kế hoạch, vui lòng lập kế hoạch mới thay vì thêm lần bảo trì.");

            // Điều kiện: lần bảo trì mới phải sau lần gần nhất, không được trùng/lùi ngày
            if (dto.NgayDuKienBaoTri <= ngayGanNhat.Value)
                return (false, $"Ngày bảo trì mới phải sau ngày gần nhất ({ngayGanNhat.Value:dd/MM/yyyy}).");

            await _repo.AddChiTietRangeAsync(new[]
            {
                new ChiTietKeHoachBaoTri
                {
                    MaKeHoach = maKeHoach,
                    MaThietBi = dto.MaThietBi,
                    NgayDuKienBaoTri = dto.NgayDuKienBaoTri
                }
            });
            await _repo.SaveChangesAsync();
            return (true, null);
        }

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