using OPC.MaintenanceAPI.Core.Exceptions;
using OPC.MaintenanceAPI.DTOs.System;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;
 
namespace OPC.MaintenanceAPI.Services.Implementations
{
    public class SystemService : ISystemService
    {
        private readonly ISystemRepository _repo;
        public SystemService(ISystemRepository repo) => _repo = repo;
 
        // ---------- VAI TRÒ ----------
        public async Task<List<object>> GetAllVaiTroAsync()
        {
            var data = await _repo.GetAllVaiTroWithUserCountAsync();
            return data.Select(x => (object)new
            {
                x.VaiTro.MaVaiTro,
                x.VaiTro.TenVaiTro,
                x.VaiTro.CapDoQuyen,
                SoNguoiDung = x.SoNguoiDung
            }).ToList();
        }
 
        public async Task<VaiTro> TaoVaiTroAsync(VaiTroDto dto)
        {
            var vaiTro = new VaiTro { TenVaiTro = dto.TenVaiTro, CapDoQuyen = dto.CapDoQuyen };
            await _repo.AddAsync(vaiTro);
            await _repo.SaveChangesAsync();
            return vaiTro;
        }
 
        public async Task<VaiTro> CapNhatVaiTroAsync(int maVaiTro, VaiTroDto dto)
        {
            var vaiTro = await _repo.GetByIdAsync(maVaiTro)
                ?? throw new NotFoundException($"Không tìm thấy vai trò #{maVaiTro}");
 
            vaiTro.TenVaiTro = dto.TenVaiTro;
            vaiTro.CapDoQuyen = dto.CapDoQuyen;
            _repo.Update(vaiTro);
            await _repo.SaveChangesAsync();
            return vaiTro;
        }
 
        public async Task XoaVaiTroAsync(int maVaiTro)
        {
            var vaiTro = await _repo.GetByIdAsync(maVaiTro)
                ?? throw new NotFoundException($"Không tìm thấy vai trò #{maVaiTro}");
 
            // Quy tắc nghiệp vụ: không cho xoá vai trò đang có người dùng
            if (await _repo.VaiTroDangCoNguoiDungAsync(maVaiTro))
                throw new BusinessRuleException("Không thể xoá vai trò đang có người dùng sử dụng.");
 
            _repo.Delete(vaiTro);
            await _repo.SaveChangesAsync();
        }
 
        // ---------- PHÂN QUYỀN ----------
        public async Task<List<ChucNangQuyenDto>> GetMaTranPhanQuyenAsync(int maVaiTro)
        {
            var tatCaChucNang = await _repo.GetChucNangGroupedAsync();
            var quyenHienCo = await _repo.GetPhanQuyenByVaiTroAsync(maVaiTro);
 
            // Ghép 2 danh sách: mọi chức năng đều hiện ra, đã cấp quyền thì đánh dấu true
            return tatCaChucNang.Select(cn =>
            {
                var quyen = quyenHienCo.FirstOrDefault(q => q.MaChucNang == cn.MaChucNang);
                return new ChucNangQuyenDto
                {
                    MaChucNang = cn.MaChucNang,
                    TenChucNang = cn.TenChucNang ?? "",
                    NhomChucNang = cn.NhomChucNang ?? "",
                    DuocXem = quyen?.DuocXem ?? false,
                    DuocTao = quyen?.DuocTao ?? false,
                    DuocSua = quyen?.DuocSua ?? false,
                    DuocDuyet = quyen?.DuocDuyet ?? false,
                };
            }).ToList();
        }
 
        public async Task LuuPhanQuyenAsync(int maVaiTro, CapNhatPhanQuyenDto dto)
        {
            if (await _repo.GetByIdAsync(maVaiTro) == null)
                throw new NotFoundException($"Không tìm thấy vai trò #{maVaiTro}");
 
            // Xoá hết quyền cũ rồi ghi lại toàn bộ - đơn giản, tránh so sánh từng dòng thay đổi gì
            await _repo.XoaPhanQuyenTheoVaiTroAsync(maVaiTro);
 
            var danhSachMoi = dto.DanhSachQuyen
                .Where(q => q.DuocXem || q.DuocTao || q.DuocSua || q.DuocDuyet) // chỉ lưu dòng có ít nhất 1 quyền bật
                .Select(q => new PhanQuyenVaiTro
                {
                    MaVaiTro = maVaiTro,
                    MaChucNang = q.MaChucNang,
                    DuocXem = q.DuocXem,
                    DuocTao = q.DuocTao,
                    DuocSua = q.DuocSua,
                    DuocDuyet = q.DuocDuyet
                }).ToList();
 
            await _repo.ThemDanhSachPhanQuyenAsync(danhSachMoi);
            await _repo.SaveChangesAsync();
        }
 
        // ---------- NHẬT KÝ ----------
        public async Task<List<NhatKyHeThong>> TimNhatKyAsync(NhatKyFilterDto filter) =>
            await _repo.GetNhatKyAsync(filter.TuKhoa, filter.PhuongThucHTTP, filter.TuNgay, filter.DenNgay);
    }
}