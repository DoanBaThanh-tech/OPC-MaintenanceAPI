using Microsoft.EntityFrameworkCore;
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
            var hoSoSuaChua = await _repo.GetHoSoSuaChuaByIdAsync(dto.MaHoSoSuaChua);
            if (hoSoSuaChua == null)
                return (false, "Không tìm thấy hồ sơ sửa chữa.");
            if (hoSoSuaChua.TrangThai != "Đã duyệt" && hoSoSuaChua.TrangThai != "Đang thực hiện")
                return (false, "Chỉ được yêu cầu vật tư cho hồ sơ sửa chữa đã duyệt hoặc đang thực hiện.");

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
        // Luồng 13 — Giám đốc duyệt
        public async Task<(bool, string?)> DuyetYeuCauVatTuAsync(int id, DuyetHoSoDto dto)
        {
            var hoSo = await _repo.GetHoSoYeuCauByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Chờ duyệt")
                return (false, "Hồ sơ đã được xử lý trước đó.");
            if (dto.MaNhanVienDuyet == hoSo.MaNhanVienTao)
                return (false, "Người duyệt không được là người tạo hồ sơ.");
            if (dto.QuyetDinh != "Duyệt" && dto.QuyetDinh != "Từ chối")
                return (false, "QuyetDinh chỉ nhận 'Duyệt' hoặc 'Từ chối'.");
            if (dto.QuyetDinh == "Từ chối" && string.IsNullOrWhiteSpace(dto.LyDo))
                return (false, "Vui lòng nhập lý do từ chối.");

            hoSo.TrangThai = dto.QuyetDinh == "Duyệt" ? "Đã duyệt" : "Từ chối";
            hoSo.LyDoTuChoi = dto.QuyetDinh == "Từ chối" ? dto.LyDo : null;
            hoSo.NgayDuyet = DateTime.Now;
            hoSo.MaNhanVienDuyet = dto.MaNhanVienDuyet;

            await _repo.AddLichSuPheDuyetAsync(new LichSuPheDuyet
            {
                MaYeuCauVatTu = id,
                MaNhanVienDuyet = dto.MaNhanVienDuyet,
                QuyetDinh = hoSo.TrangThai,
                LyDo = dto.LyDo,
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
        public async Task<bool> DaXuatChoYeuCauAsync(int maYeuCauVatTu)
        {
            return await _repo.DaXuatChoYeuCauAsync(maYeuCauVatTu);
        }

        public async Task<(bool, string?)> XuatKhoAsync(int maYeuCauVatTu, int maNhanVienGiaoDich)
        {
            var hoSo = await _repo.GetHoSoYeuCauByIdAsync(maYeuCauVatTu);
            if (hoSo == null || hoSo.TrangThai != "Đã duyệt")
                return (false, "Hồ sơ chưa được duyệt.");

            // CHẶN #1: đã xuất rồi thì không cho xuất lại
            if (await _repo.DaXuatChoYeuCauAsync(maYeuCauVatTu))
                return (false, "Yêu cầu này đã được xuất kho trước đó.");

            var chiTiets = await _repo.GetChiTietByHoSoAsync(maYeuCauVatTu);
            foreach (var ct in chiTiets)
            {
                if (ct.MaVatTuNavigation == null || ct.MaVatTuNavigation.SoLuongTonKho < ct.SoLuongYeuCau)
                    return (false, $"Không đủ tồn kho cho vật tư mã {ct.MaVatTu}.");
            }

            try
            {
                foreach (var ct in chiTiets)
                {
                    await _repo.AddGiaoDichAsync(new NhapXuatVatTu
                    {
                        MaVatTu = ct.MaVatTu,
                        MaYeuCauVatTu = maYeuCauVatTu,
                        MaNhanVienGiaoDich = maNhanVienGiaoDich,
                        LoaiGiaoDich = "Xuất",
                        SoLuong = ct.SoLuongYeuCau,
                        NgayGiaoDich = DateTime.Now
                    });
                    ct.MaVatTuNavigation!.SoLuongTonKho -= ct.SoLuongYeuCau; // EF theo dõi RowVersion tự động
                }
                await _repo.SaveChangesAsync(); // CHẶN #2: nếu vật tư vừa bị trừ bởi giao dịch khác, ném DbUpdateConcurrencyException
            }
            catch (DbUpdateConcurrencyException)
            {
                return (false, "Tồn kho vừa bị thay đổi bởi giao dịch khác. Vui lòng tải lại và thử xuất kho lại.");
            }

            var vatTuThap = chiTiets.Where(c => c.MaVatTuNavigation!.SoLuongTonKho < c.MaVatTuNavigation.MucTonKhoToiThieu)
                                    .Select(c => c.MaVatTuNavigation!.TenVatTu);
            var canhBao = vatTuThap.Any() ? $"Cảnh báo tồn kho thấp: {string.Join(", ", vatTuThap)}" : null;
            return (true, canhBao);
        }

    }
}