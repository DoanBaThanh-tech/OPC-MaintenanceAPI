using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.DTOs.Equipment;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public class EquipmentService : IEquipmentService
    {
        private readonly IEquipmentRepository _repo;
        public EquipmentService(IEquipmentRepository repo) => _repo = repo;

        public async Task<List<ThietBiResponseDto>> GetAllAsync(int? maChuKy = null, string? trangThai = null)
        {
            var list = await _repo.GetAllAsync();
            if (maChuKy.HasValue)
                list = list.Where(t => t.MaChuKy == maChuKy.Value).ToList();

            var mapped = list.Select(MapToDto).ToList();

            if (!string.IsNullOrWhiteSpace(trangThai))
            {
                var tt = TrangThaiThietBiConst.ChuanHoa(trangThai);
                mapped = mapped.Where(t => t.TinhTrangHienTai == tt).ToList();
            }

            return mapped;
        }

        public async Task<int> DongBoTrangThaiTuHoSoAsync() =>
            await _repo.DongBoTrangThaiTuHoSoAsync();

        public async Task<List<NhomThietBiDto>> GetTheoDanhMucAsync(string? trangThai = null)
        {
            // Tự đồng bộ trước khi trả list — tránh lệch sau khi xóa/tạo hồ sơ tay
            await _repo.DongBoTrangThaiTuHoSoAsync();
            var all = await GetAllAsync(trangThai: trangThai);

            return all
                .GroupBy(t => new
                {
                    DanhMuc = string.IsNullOrWhiteSpace(t.TenDanhMuc) ? "Chưa phân loại" : t.TenDanhMuc!,
                    t.MaChuKy,
                    t.SoThangDeXuat
                })
                .OrderBy(g => g.Key.DanhMuc)
                .Select(g => new NhomThietBiDto
                {
                    TenDanhMuc = g.Key.DanhMuc,
                    MaChuKy = g.Key.MaChuKy,
                    SoThangChuKy = g.Key.SoThangDeXuat,
                    SoLuong = g.Count(),
                    SoSanXuat = g.Count(x => x.TinhTrangHienTai == TrangThaiThietBiConst.SanXuat),
                    SoBaoTri = g.Count(x => x.TinhTrangHienTai == TrangThaiThietBiConst.BaoTri),
                    SoSuaChua = g.Count(x => x.TinhTrangHienTai == TrangThaiThietBiConst.SuaChua),
                    DanhSach = g.OrderBy(x => x.TenThietBi).ToList()
                })
                .ToList();
        }

        public async Task<ThongKeThietBiDto> GetThongKeAsync()
        {
            await _repo.DongBoTrangThaiTuHoSoAsync();
            var all = await GetAllAsync();
            return new ThongKeThietBiDto
            {
                TongSo = all.Count,
                SoSanXuat = all.Count(x => x.TinhTrangHienTai == TrangThaiThietBiConst.SanXuat),
                SoBaoTri = all.Count(x => x.TinhTrangHienTai == TrangThaiThietBiConst.BaoTri),
                SoSuaChua = all.Count(x => x.TinhTrangHienTai == TrangThaiThietBiConst.SuaChua)
            };
        }


        public async Task<ThietBiResponseDto?> GetByIdAsync(int id)
        {
            var t = await _repo.GetByIdAsync(id);
            return t == null ? null : MapToDto(t);
        }

        // Luồng 5A — mặc định trạng thái "Sản xuất"
        public async Task<(bool, string?)> TaoMoiAsync(TaoThietBiDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TenThietBi) || string.IsNullOrWhiteSpace(dto.ViTriLapDat))
                return (false, "Vui lòng nhập đầy đủ Tên thiết bị và Vị trí lắp đặt.");

            if (dto.NgayLapDat == null)
                return (false, "Vui lòng nhập Ngày lắp đặt.");

            if (await _repo.ExistsAsync(dto.TenThietBi, dto.ViTriLapDat))
                return (false, "Thiết bị này đã tồn tại trong danh sách.");

            var thietBi = new ThietBi
            {
                TenThietBi = dto.TenThietBi.Trim(),
                LoaiThietBi = dto.LoaiThietBi,
                ViTriLapDat = dto.ViTriLapDat.Trim(),
                NgayLapDat = dto.NgayLapDat.Value,
                GhiChu = dto.GhiChu,
                MaChuKy = dto.MaChuKy ?? 0,
                TinhTrangHienTai = TrangThaiThietBiConst.SanXuat
            };
            await _repo.AddAsync(thietBi);
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        // Luồng 5B — đang có hồ sơ Đang thực hiện thì chỉ cho sửa Vị trí / Ghi chú
        public async Task<(bool, string?)> CapNhatAsync(int id, CapNhatThietBiDto dto)
        {
            var thietBi = await _repo.GetByIdAsync(id);
            if (thietBi == null) return (false, "Thiết bị không còn tồn tại.");

            bool dangXuLy = await _repo.DangCoHoSoDangThucHienAsync(id);

            thietBi.ViTriLapDat = dto.ViTriLapDat ?? thietBi.ViTriLapDat;
            thietBi.GhiChu = dto.GhiChu ?? thietBi.GhiChu;

            if (!dangXuLy)
            {
                thietBi.TenThietBi = dto.TenThietBi ?? thietBi.TenThietBi;
                thietBi.LoaiThietBi = dto.LoaiThietBi ?? thietBi.LoaiThietBi;
                thietBi.NgayLapDat = dto.NgayLapDat ?? thietBi.NgayLapDat;
            }

            await _repo.SaveChangesAsync();
            return (true, dangXuLy
                ? "Chỉ cập nhật được Vị trí/Ghi chú vì thiết bị đang có hồ sơ xử lý."
                : null);
        }

        public async Task<List<LichSuThietBiDto>> GetLichSuAsync(int maThietBi) =>
            (await _repo.GetLichSuAsync(maThietBi)).Select(l => new LichSuThietBiDto
            {
                MaLichSu = l.MaLichSu,
                NgayHoanThanh = l.NgayHoanThanh,
                KetQua = l.KetQua,
                GhiChu = l.GhiChu
            }).ToList();

        private static ThietBiResponseDto MapToDto(ThietBi t)
        {
            var tenDanhMuc = t.MaChuKyNavigation?.LoaiThietBi
                ?? t.LoaiThietBi
                ?? "Chưa phân loại";

            return new ThietBiResponseDto
            {
                MaThietBi = t.MaThietBi,
                TenThietBi = t.TenThietBi,
                LoaiThietBi = t.LoaiThietBi,
                ViTriLapDat = t.ViTriLapDat,
                NgayLapDat = t.NgayLapDat,
                TinhTrangHienTai = TrangThaiThietBiConst.ChuanHoa(t.TinhTrangHienTai),
                GhiChu = t.GhiChu,
                NgayBaoTriGanNhat = t.NgayBaoTriGanNhat,
                NgayBaoTriTiepTheo = t.NgayBaoTriTiepTheo,
                SoThangDeXuat = t.SoThangDeXuat ?? t.MaChuKyNavigation?.SoThangChuKyDeXuat,
                MaChuKy = t.MaChuKy,
                TenDanhMuc = tenDanhMuc,
                MoTaChuKy = t.MaChuKyNavigation?.MoTa
            };
        }
    }
}