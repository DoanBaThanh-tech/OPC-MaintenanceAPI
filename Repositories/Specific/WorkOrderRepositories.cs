using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Data;

namespace OPC.MaintenanceAPI.Repositories.Specific
{
    public interface IWorkOrderRepository
    {   
        void SetHoSoBaoTriRowVersion(HoSoBaoTri hoSo, byte[] rowVersion);
        void SetHoSoSuaChuaRowVersion(HoSoSuaChua hoSo, byte[] rowVersion);
        // Hồ sơ bảo trì
        // Kiểm tra trùng lịch + đã có kết quả — 2 dòng mới cần thêm
        Task<bool> NhanVienTrungLichAsync(int maNhanVien, DateTime tu, DateTime den);
        /// <summary>Số hồ sơ BT + SC đang "Đang thực hiện" của nhân viên (tối đa 3).</summary>
        Task<int> DemCongViecDangThucHienAsync(int maNhanVien);
        /// <summary>
        /// Không nhận thêm khi đã khóa (đủ 3 chưa xong hết) hoặc đang đủ 3 việc.
        /// Chỉ nhận lại sau khi hoàn thành hết cả 3 (KhoaPhanCong = false, số việc = 0).
        /// </summary>
        Task<bool> NhanVienKhongTheNhanThemAsync(int maNhanVien);
        Task<bool> DaCoKetQuaAsync(int maPhanCong);
        Task<int?> GetSoThangChuKyAsync(string? loaiThietBi);
        Task AddHoSoBaoTriAsync(HoSoBaoTri hoSo);
        Task<DateOnly?> GetNgayDuKienBaoTriTheoHoSoBaoTriAsync(int maHoSoBaoTri);
        Task<HoSoBaoTri?> GetHoSoBaoTriByIdAsync(int id);
        Task<List<HoSoBaoTri>> GetHoSoBaoTriByTrangThaiAsync(string? trangThai);
        Task<ChiTietKeHoachBaoTri?> GetChiTietKeHoachByIdAsync(int id);
        /// <summary>Thiết bị đã có hồ sơ bảo trì gắn chi tiết kế hoạch trong tháng/năm.</summary>
        Task<bool> TonTaiHoSoBaoTriTheoThietBiThangAsync(int maThietBi, int nam, int thang);
        Task<int?> GetNamKeHoachTheoHoSoBaoTriAsync(int maHoSoBaoTri);
        // Rule 2: khoá tài nguyên chéo giữa Bảo trì và Sửa chữa
        Task<bool> ThietBiDangTrongQuyTrinhKhacAsync(int maThietBi, string boQuaLoaiHoSo, int? boQuaMaHoSo);
        // Hồ sơ sửa chữa
        Task AddHoSoSuaChuaAsync(HoSoSuaChua hoSo);
        Task<HoSoSuaChua?> GetHoSoSuaChuaByIdAsync(int id);
        Task<List<HoSoSuaChua>> GetHoSoSuaChuaByTrangThaiAsync(string trangThai);
        
        // Phân công + kết quả
        Task AddPhanCongAsync(PhanCongCongViec phanCong);
        Task<PhanCongCongViec?> GetPhanCongByIdAsync(int id);
        Task<List<PhanCongCongViec>> GetLichSuPhanCongAsync();
        Task AddKetQuaAsync(KetQuaThucHien ketQua);

        // Lịch sử phê duyệt
        Task AddLichSuPheDuyetAsync(LichSuPheDuyet lichSu);

        Task<int> SaveChangesAsync();
    }

    public class WorkOrderRepository : IWorkOrderRepository
    {
        private readonly OPCDbContext _context;
        public WorkOrderRepository(OPCDbContext context) => _context = context;

        public async Task<DateOnly?> GetNgayDuKienBaoTriTheoHoSoBaoTriAsync(int maHoSoBaoTri) =>
        await _context.ChiTietKeHoachBaoTris
            .Where(c => c.MaHoSoBaoTri == maHoSoBaoTri)
            .Select(c => (DateOnly?)c.NgayDuKienBaoTri)
            .FirstOrDefaultAsync();

        public async Task<int?> GetNamKeHoachTheoHoSoBaoTriAsync(int maHoSoBaoTri) =>
            await _context.ChiTietKeHoachBaoTris
                .Where(c => c.MaHoSoBaoTri == maHoSoBaoTri)
                .Include(c => c.MaKeHoachNavigation)
                .Select(c => (int?)c.MaKeHoachNavigation!.Nam)
                .FirstOrDefaultAsync();

        public async Task<int?> GetSoThangChuKyAsync(string? loaiThietBi)
        {
            if (loaiThietBi == null) return null;
            var chuKy = await _context.ChuKyBaoTris.FirstOrDefaultAsync(c => c.LoaiThietBi == loaiThietBi);
            return chuKy?.SoThangChuKyDeXuat;
        }
        public async Task<bool> NhanVienTrungLichAsync(int maNhanVien, DateTime tu, DateTime den) =>
        await _context.PhanCongCongViecs.AnyAsync(p =>
            p.MaNhanVienThucHien == maNhanVien &&
            p.TrangThai != "Hoàn thành" &&
            p.NgayBatDauDuKien != null && p.NgayKetThucDuKien != null &&
            p.NgayBatDauDuKien <= den && p.NgayKetThucDuKien >= tu);

        /// <summary>Giới hạn số thiết bị mỗi NV được đảm nhận đồng thời (bảo trì + sửa chữa).</summary>
        public const int SoThietBiToiDaMoiNhanVien = 3;

        public async Task<int> DemCongViecDangThucHienAsync(int maNhanVien)
        {
            var soBaoTri = await _context.HoSoBaoTris.CountAsync(h =>
                h.TrangThai == "Đang thực hiện"
                && h.MaPhanCong != null
                && _context.PhanCongCongViecs.Any(p =>
                    p.MaPhanCong == h.MaPhanCong
                    && p.MaNhanVienThucHien == maNhanVien));

            var soSuaChua = await _context.HoSoSuaChuas.CountAsync(h =>
                h.TrangThai == "Đang thực hiện"
                && h.MaPhanCong != null
                && _context.PhanCongCongViecs.Any(p =>
                    p.MaPhanCong == h.MaPhanCong
                    && p.MaNhanVienThucHien == maNhanVien));

            return soBaoTri + soSuaChua;
        }

        public async Task<bool> NhanVienKhongTheNhanThemAsync(int maNhanVien)
        {
            // Chỉ được phân công khi đang 0 việc (0/3).
            // 1/3, 2/3, 3/3 đều không chọn — phải về 0 mới nhận thiết bị tiếp theo.
            var so = await DemCongViecDangThucHienAsync(maNhanVien);
            return so > 0;
        }


        // Điều kiện: kiểm tra thiết bị có đang "Đang thực hiện" ở hồ sơ bảo trì HOẶC sửa chữa nào khác không

    // boQuaLoaiHoSo/boQuaMaHoSo dùng để loại trừ chính hồ sơ đang xử lý (tránh tự chặn chính mình)
        public async Task<bool> ThietBiDangTrongQuyTrinhKhacAsync(int maThietBi, string boQuaLoaiHoSo, int? boQuaMaHoSo)

        {
            var coBaoTri = await _context.HoSoBaoTris.AnyAsync(h =>
                h.MaThieBi == maThietBi &&
                h.TrangThai == "Đang thực hiện" &&
                !(boQuaLoaiHoSo == "BaoTri" && h.MaHoSoBaoTri == boQuaMaHoSo));

            var coSuaChua = await _context.HoSoSuaChuas.AnyAsync(h =>
                h.MaThieBi == maThietBi &&
                h.TrangThai == "Đang thực hiện" &&
                !(boQuaLoaiHoSo == "SuaChua" && h.MaHoSoSuaChua == boQuaMaHoSo));

            return coBaoTri || coSuaChua;
        }
            public void SetHoSoBaoTriRowVersion(HoSoBaoTri hoSo, byte[] rowVersion) =>
        _context.Entry(hoSo).Property(x => x.RowVersion).OriginalValue = rowVersion;

        public void SetHoSoSuaChuaRowVersion(HoSoSuaChua hoSo, byte[] rowVersion) =>
            _context.Entry(hoSo).Property(x => x.RowVersion).OriginalValue = rowVersion;
    public async Task<bool> DaCoKetQuaAsync(int maPhanCong) =>
        await _context.KetQuaThucHiens.AnyAsync(k => k.MaPhanCong == maPhanCong);
        public async Task AddHoSoBaoTriAsync(HoSoBaoTri hoSo) => await _context.HoSoBaoTris.AddAsync(hoSo);

        public async Task<HoSoBaoTri?> GetHoSoBaoTriByIdAsync(int id) =>
            await _context.HoSoBaoTris
                .Include(h => h.MaThieBiNavigation)
                .Include(h => h.MaPhanCongNavigation)
                .Include(h => h.MaNhanVienTaoNavigation)   // ← 
                .FirstOrDefaultAsync(h => h.MaHoSoBaoTri == id);

        public async Task<List<HoSoBaoTri>> GetHoSoBaoTriByTrangThaiAsync(string? trangThai) =>
            await _context.HoSoBaoTris
            .Include(h => h.MaThieBiNavigation)
            .Include(h => h.MaNhanVienTaoNavigation)
            .Where(h => trangThai == null || h.TrangThai == trangThai)
            .ToListAsync();

        public async Task AddHoSoSuaChuaAsync(HoSoSuaChua hoSo) => await _context.HoSoSuaChuas.AddAsync(hoSo);

        public async Task<HoSoSuaChua?> GetHoSoSuaChuaByIdAsync(int id) =>
            await _context.HoSoSuaChuas
                .Include(h => h.MaThieBiNavigation)
                .Include(h => h.MaPhanCongNavigation)
                .FirstOrDefaultAsync(h => h.MaHoSoSuaChua == id);

        public async Task<List<HoSoSuaChua>> GetHoSoSuaChuaByTrangThaiAsync(string trangThai) =>
        await _context.HoSoSuaChuas
            .Include(h => h.MaThieBiNavigation)
            .Where(h => h.TrangThai == trangThai)
            .AsNoTracking()
            .ToListAsync();

        public async Task AddPhanCongAsync(PhanCongCongViec phanCong) =>
            await _context.PhanCongCongViecs.AddAsync(phanCong);

        public async Task<PhanCongCongViec?> GetPhanCongByIdAsync(int id) =>
            await _context.PhanCongCongViecs.FirstOrDefaultAsync(p => p.MaPhanCong == id);

        public async Task<List<PhanCongCongViec>> GetLichSuPhanCongAsync() =>
            await _context.PhanCongCongViecs
                .Include(p => p.MaNhanVienPhanCongNavigation)
                .Include(p => p.MaNhanVienThucHienNavigation)
                .OrderByDescending(p => p.NgayPhanCong)
                .AsNoTracking()
                .ToListAsync();

        public async Task AddKetQuaAsync(KetQuaThucHien ketQua) => await _context.KetQuaThucHiens.AddAsync(ketQua);

        public async Task AddLichSuPheDuyetAsync(LichSuPheDuyet lichSu) =>
            await _context.LichSuPheDuyets.AddAsync(lichSu);

        public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();

        public async Task<ChiTietKeHoachBaoTri?> GetChiTietKeHoachByIdAsync(int id) =>
            await _context.ChiTietKeHoachBaoTris.FirstOrDefaultAsync(c => c.MaChiTietKeHoach == id);

        public async Task<bool> TonTaiHoSoBaoTriTheoThietBiThangAsync(int maThietBi, int nam, int thang) =>
            await _context.ChiTietKeHoachBaoTris.AnyAsync(c =>
                c.MaThietBi == maThietBi
                && c.NgayDuKienBaoTri.Year == nam
                && c.NgayDuKienBaoTri.Month == thang
                && c.MaHoSoBaoTri != null);
    }
}