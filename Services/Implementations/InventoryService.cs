using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.DTOs.Inventory;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;
using OPC.MaintenanceAPI.DTOs.Common;
namespace OPC.MaintenanceAPI.Services.Implementations
{
    public class InventoryService : IInventoryService 
    {
        private readonly IInventoryRepository _repo;
        public InventoryService(IInventoryRepository repo) => _repo = repo;

        // Luồng 12
        public async Task<(bool, string?, bool)> KiemTraTonKhoAsync(List<KiemTraVatTuDto> danhSach)
        {
            foreach (var item in danhSach)
            {
                var vatTu = await _repo.GetVatTuByIdAsync(item.MaVatTu);
                if (vatTu == null) return (false, $"Vật tư mã {item.MaVatTu} không tồn tại.", false);
                if (vatTu.SoLuongTonKho < item.SoLuongCanDung)
                    return (true, null, false); // thiếu vật tư — Decision đưa sang Luồng 13
            }
            return (true, null, true); // đủ vật tư — Decision đưa sang Luồng 15
        }

        // Luồng 13
        public async Task<(bool, string?)> TaoYeuCauVatTuAsync(TaoYeuCauVatTuDto dto)
        {
            if (dto.ChiTiet == null || dto.ChiTiet.Count == 0)
                return (false, "Vui lòng chọn ít nhất 1 vật tư.");
            if (dto.ChiTiet.Any(c => c.SoLuongYeuCau <= 0))
                return (false, "Số lượng yêu cầu phải lớn hơn 0.");

            var hoSo = new HoSoYeuCauVatTu
            {
                MaHoSoSuaChua = dto.MaHoSoSuaChua,
                MaNhanVienTao = dto.MaNhanVienTao,
                NgayTao = DateTime.Now,
                TrangThai = "Chờ duyệt"
            };
            await _repo.AddHoSoYeuCauAsync(hoSo);
            await _repo.SaveChangesAsync();

            var chiTiets = dto.ChiTiet.Select(c => new ChiTietYeuCauVatTu
            {
                MaYeuCauVatTu = hoSo.MaYeuCauVatTu,
                MaVatTu = c.MaVatTu,
                SoLuongYeuCau = c.SoLuongYeuCau
            });
            await _repo.AddChiTietRangeAsync(chiTiets);
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        // Luồng 13 — Giám đốc duyệt
        public async Task<(bool, string?)> DuyetYeuCauVatTuAsync(int id, DuyetHoSoDto dto)
        {
            var hoSo = await _repo.GetHoSoYeuCauByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (!dto.Duyet && string.IsNullOrWhiteSpace(dto.LyDoTuChoi))
                return (false, "Vui lòng nhập lý do từ chối.");

            hoSo.TrangThai = dto.Duyet ? "Đã duyệt" : "Từ chối";
            hoSo.LyDoTuChoi = dto.Duyet ? null : dto.LyDoTuChoi;
            hoSo.NgayDuyet = DateTime.Now;
            hoSo.MaNhanVienDuyet = dto.MaNhanVienDuyet;

            await _repo.AddLichSuPheDuyetAsync(new LichSuPheDuyet
            {
                MaYeuCauVatTu = id,
                MaNhanVienDuyet = dto.MaNhanVienDuyet,
                QuyetDinh = hoSo.TrangThai,
                LyDo = dto.LyDoTuChoi,
                NgayDuyet = DateTime.Now
            });

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        // Luồng 14 — Nhập kho
        public async Task<(bool, string?)> NhapKhoAsync(NhapKhoDto dto)
        {
            if (dto.SoLuong <= 0) return (false, "Số lượng nhập phải lớn hơn 0.");

            var vatTu = await _repo.GetVatTuByIdAsync(dto.MaVatTu);
            if (vatTu == null) return (false, "Vật tư không tồn tại.");

            await _repo.AddGiaoDichAsync(new NhapXuatVatTu
            {
                MaVatTu = dto.MaVatTu,
                MaNhanVienGiaoDich = dto.MaNhanVienGiaoDich,
                LoaiGiaoDich = "Nhập",
                SoLuong = dto.SoLuong,
                NgayGiaoDich = DateTime.Now
            });
            vatTu.SoLuongTonKho += dto.SoLuong;
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        // Luồng 14 — Xuất kho
        public async Task<(bool, string?)> XuatKhoAsync(int maYeuCauVatTu)
        {
            var hoSo = await _repo.GetHoSoYeuCauByIdAsync(maYeuCauVatTu);
            if (hoSo == null || hoSo.TrangThai != "Đã duyệt")
                return (false, "Hồ sơ chưa được duyệt.");

            var chiTiets = await _repo.GetChiTietByHoSoAsync(maYeuCauVatTu);
            foreach (var ct in chiTiets)
            {
                if (ct.MaVatTuNavigation == null || ct.MaVatTuNavigation.SoLuongTonKho < ct.SoLuongYeuCau)
                    return (false, $"Không đủ tồn kho cho vật tư mã {ct.MaVatTu}.");
            }

            foreach (var ct in chiTiets)
            {
                await _repo.AddGiaoDichAsync(new NhapXuatVatTu
                {
                    MaVatTu = ct.MaVatTu,
                    MaYeuCauVatTu = maYeuCauVatTu,
                    LoaiGiaoDich = "Xuất",
                    SoLuong = ct.SoLuongYeuCau,
                    NgayGiaoDich = DateTime.Now
                });
                ct.MaVatTuNavigation!.SoLuongTonKho -= ct.SoLuongYeuCau;
            }

            await _repo.SaveChangesAsync();

            // Decision cảnh báo tồn kho thấp — trả ra cho Controller quyết định hiển thị
            var vatTuThap = chiTiets.Where(c => c.MaVatTuNavigation!.SoLuongTonKho < c.MaVatTuNavigation.MucTonKhoToiThieu)
                                     .Select(c => c.MaVatTuNavigation!.TenVatTu);
            var canhBao = vatTuThap.Any() ? $"Cảnh báo tồn kho thấp: {string.Join(", ", vatTuThap)}" : null;

            return (true, canhBao);
        }
    }
}