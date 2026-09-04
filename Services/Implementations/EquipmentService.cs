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

        public async Task<List<ThietBiResponseDto>> GetAllAsync() =>
            (await _repo.GetAllAsync()).Select(MapToDto).ToList();

        public async Task<ThietBiResponseDto?> GetByIdAsync(int id)
        {
            var t = await _repo.GetByIdAsync(id);
            return t == null ? null : MapToDto(t);
        }

        // Luồng 5A
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
                TenThietBi = dto.TenThietBi,
                LoaiThietBi = dto.LoaiThietBi,
                ViTriLapDat = dto.ViTriLapDat,
                NgayLapDat = dto.NgayLapDat.Value,   // .Value vì đã chắc chắn khác null ở check trên
                GhiChu = dto.GhiChu,
                TinhTrangHienTai = "Đang hoạt động"
            };
            await _repo.AddAsync(thietBi);
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        // Luồng 5B — Decision: đang có hồ sơ Đang thực hiện thì chỉ cho sửa 2 trường mô tả
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
            return (true, dangXuLy ? "Chỉ cập nhật được Vị trí/Ghi chú vì thiết bị đang có hồ sơ xử lý." : null);
        }

        public async Task<List<LichSuThietBiDto>> GetLichSuAsync(int maThietBi) =>
            (await _repo.GetLichSuAsync(maThietBi)).Select(l => new LichSuThietBiDto
            {
                MaLichSu = l.MaLichSu,
                NgayHoanThanh = l.NgayHoanThanh,
                KetQua = l.KetQua
            }).ToList();

        private static ThietBiResponseDto MapToDto(ThietBi t) => new()
        {
            MaThietBi = t.MaThietBi,
            TenThietBi = t.TenThietBi,
            LoaiThietBi = t.LoaiThietBi,
            ViTriLapDat = t.ViTriLapDat,
            TinhTrangHienTai = t.TinhTrangHienTai,
            NgayBaoTriGanNhat = t.NgayBaoTriGanNhat,
            NgayBaoTriTiepTheo = t.NgayBaoTriTiepTheo
        };
    }
}