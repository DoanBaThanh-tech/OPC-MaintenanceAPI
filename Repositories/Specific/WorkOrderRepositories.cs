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
        Task<bool> NhanVienTrungLichAsync(int maNhanVien, DateTime tu, DateTime den);
        /// <summary>Số hồ sơ BT + SC đang "Đang thực hiện" của nhân viên (chỉ để thống kê/hiển thị).</summary>
        Task<int> DemCongViecDangThucHienAsync(int maNhanVien);
        // ĐÃ BỎ: NhanVienKhongTheNhanThemAsync (ràng buộc max 3 / phải 0 việc)

        Task<bool> DaCoKetQuaAsync(int maPhanCong);
        Task<int?> GetSoThangChuKyAsync(string? loaiThietBi);
        Task AddHoSoBaoTriAsync(HoSoBaoTri hoSo);
        Task<DateOnly?> GetNgayDuKienBaoTriTheoHoSoBaoTriAsync(int maHoSoBaoTri);
        Task<HoSoBaoTri?> GetHoSoBaoTriByIdAsync(int id);
        Task<List<HoSoBaoTri>> GetHoSoBaoTriByTrangThaiAsync(string? trangThai);
        Task<ChiTietKeHoachBaoTri?> GetChiTietKeHoachByIdAsync(int id);
        Task<bool> TonTaiHoSoBaoTriTheoThietBiThangAsync(int maThietBi, int nam, int thang);
        /// <summary>Cùng thiết bị + tháng đã có hồ sơ/chi tiết khác (loại trừ dòng hiện tại khi sửa).</summary>
        Task<bool> TonTaiChiTietHoacHoSoThietBiThangKhacAsync(int maThietBi, int nam, int thang, int? excludeMaChiTiet, int? excludeMaHoSo);
        Task<int?> GetNamKeHoachTheoHoSoBaoTriAsync(int maHoSoBaoTri);
        Task<bool> ThietBiDangTrongQuyTrinhKhacAsync(int maThietBi, string boQuaLoaiHoSo, int? boQuaMaHoSo);
        Task<ChiTietKeHoachBaoTri?> GetChiTietKeHoachByHoSoBaoTriAsync(int maHoSoBaoTri);
        Task<bool> CoHoSoBaoTriDangMoAsync(int maThietBi, int? loaiTruMaHoSo = null);
        Task<bool> CoHoSoSuaChuaDangMoAsync(int maThietBi, int? loaiTruMaHoSo = null);

        // Hồ sơ sửa chữa
        Task AddHoSoSuaChuaAsync(HoSoSuaChua hoSo);
        Task<HoSoSuaChua?> GetHoSoSuaChuaByIdAsync(int id);
        Task<List<HoSoSuaChua>> GetHoSoSuaChuaByTrangThaiAsync(string? trangThai);
        Task<ThietBi?> GetThietBiByIdAsync(int maThietBi);
        Task<List<PhanCongCongViec>> GetPhanCongTheoHoSoSuaChuaAsync(int maHoSoSuaChua);

        // Phân công + kết quả
        Task AddPhanCongAsync(PhanCongCongViec phanCong);
        Task<PhanCongCongViec?> GetPhanCongByIdAsync(int id);
        Task<List<PhanCongCongViec>> GetLichSuPhanCongAsync();
        Task<List<PhanCongCongViec>> GetLichSuPhanCongChiTietAsync();
        Task<List<PhanCongCongViec>> GetPhanCongCuaNhanVienAsync(int maNhanVien, string? loai, string? trangThaiPhanCong);
        /// <summary>Tất cả phân công gắn với một hồ sơ bảo trì (hỗ trợ nhiều NV).</summary>
        Task<List<PhanCongCongViec>> GetPhanCongTheoHoSoBaoTriAsync(int maHoSoBaoTri);
        Task<HoSoBaoTri?> GetHoSoBaoTriByMaPhanCongAsync(int maPhanCong);
        Task<HoSoSuaChua?> GetHoSoSuaChuaByMaPhanCongAsync(int maPhanCong);
        void RemovePhanCong(PhanCongCongViec phanCong);
        void RemoveKetQua(KetQuaThucHien ketQua);
        Task<KetQuaThucHien?> GetKetQuaByMaPhanCongAsync(int maPhanCong);
        Task AddKetQuaAsync(KetQuaThucHien ketQua);

        // Lịch sử phê duyệt
        Task AddLichSuPheDuyetAsync(LichSuPheDuyet lichSu);
        Task<List<LichSuPheDuyet>> GetLichSuPheDuyetAsync(string? loai, int? nam);


        // Yêu cầu bảo trì thiết bị (Tổ trưởng sản xuất)
        Task AddYeuCauBaoTriAsync(YeuCauBaoTriThietBi yc);
        Task<YeuCauBaoTriThietBi?> GetYeuCauBaoTriByIdAsync(int id);
        Task<List<YeuCauBaoTriThietBi>> GetYeuCauBaoTriListAsync(string? trangThai, int? nam, int? thang);
        Task<bool> TonTaiYeuCauBaoTriThangAsync(int maThietBi, int nam, int thang);
        Task<bool> TonTaiYeuCauBaoTriThangKhacIdAsync(int maThietBi, int nam, int thang, int excludeId);

        Task<int> SaveChangesAsync();
    }

    public class WorkOrderRepository : IWorkOrderRepository
    {
        private readonly OPCDbContext _context;
        public WorkOrderRepository(OPCDbContext context) => _context = context;

        public async Task<bool> CoHoSoBaoTriDangMoAsync(int maThietBi, int? loaiTruMaHoSo = null)
        {
            // Chỉ hồ sơ còn hiệu lực (không tính Nháp / Từ chối / Đã hoàn thành / Đã hủy)
            var q = _context.HoSoBaoTris.Where(h =>
                h.MaThieBi == maThietBi &&
                (h.TrangThai == "Chờ xưởng" ||
                 h.TrangThai == "Chờ duyệt" ||
                 h.TrangThai == "Chờ GĐ duyệt" ||
                 h.TrangThai == "Đã duyệt" ||
                 h.TrangThai == "Đang thực hiện"));

            if (loaiTruMaHoSo.HasValue)
                q = q.Where(h => h.MaHoSoBaoTri != loaiTruMaHoSo.Value);

            return await q.AnyAsync();
        }

        public async Task<bool> CoHoSoSuaChuaDangMoAsync(int maThietBi, int? loaiTruMaHoSo = null)
        {
            var q = _context.HoSoSuaChuas.Where(h =>
                h.MaThieBi == maThietBi &&
                h.TrangThai != "Đã hoàn thành" &&
                h.TrangThai != "Từ chối" &&
                h.TrangThai != "Nháp" &&
                h.TrangThai != "Đã hủy");

            if (loaiTruMaHoSo.HasValue)
                q = q.Where(h => h.MaHoSoSuaChua != loaiTruMaHoSo.Value);

            return await q.AnyAsync();
        }

        public async Task<ThietBi?> GetThietBiByIdAsync(int maThietBi) =>
            await _context.ThietBis.FirstOrDefaultAsync(t => t.MaThietBi == maThietBi);

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

        /// <summary>
        /// true nếu NV đã có phân công (chưa hoàn thành) trùng khung giờ [tu, den].
        /// </summary>
        public async Task<bool> NhanVienTrungLichAsync(int maNhanVien, DateTime tu, DateTime den) =>
            await _context.PhanCongCongViecs.AnyAsync(p =>
                p.MaNhanVienThucHien == maNhanVien &&
                p.TrangThai != "Hoàn thành" &&
                p.NgayBatDauDuKien != null && p.NgayKetThucDuKien != null &&
                p.NgayBatDauDuKien <= den && p.NgayKetThucDuKien >= tu);

        /// <summary>Đếm số việc BT+SC đang thực hiện — chỉ để hiển thị, không chặn phân công.</summary>
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
                .Include(h => h.MaPhanCongNavigation!)
                    .ThenInclude(p => p.MaNhanVienThucHienNavigation)
                .Include(h => h.MaNhanVienTaoNavigation)
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
                .Include(h => h.MaNhanVienTaoNavigation)
                .Include(h => h.MaPhanCongNavigation)!.ThenInclude(p => p!.MaNhanVienThucHienNavigation)
                .FirstOrDefaultAsync(h => h.MaHoSoSuaChua == id);

        public async Task<List<HoSoSuaChua>> GetHoSoSuaChuaByTrangThaiAsync(string? trangThai)
        {
            var q = _context.HoSoSuaChuas
                .Include(h => h.MaThieBiNavigation)
                .Include(h => h.MaNhanVienTaoNavigation)
                .AsQueryable();
            if (!string.IsNullOrWhiteSpace(trangThai))
                q = q.Where(h => h.TrangThai == trangThai);
            return await q.OrderByDescending(h => h.NgayTao).AsNoTracking().ToListAsync();
        }

        public async Task<List<PhanCongCongViec>> GetPhanCongTheoHoSoSuaChuaAsync(int maHoSoSuaChua)
        {
            return await _context.PhanCongCongViecs
                .Include(p => p.MaNhanVienThucHienNavigation)
                .Where(p => p.MaHoSoSuaChua == maHoSoSuaChua
                    || _context.HoSoSuaChuas.Any(h => h.MaHoSoSuaChua == maHoSoSuaChua && h.MaPhanCong == p.MaPhanCong))
                .ToListAsync();
        }

        public async Task AddPhanCongAsync(PhanCongCongViec phanCong) =>
            await _context.PhanCongCongViecs.AddAsync(phanCong);

        public async Task<PhanCongCongViec?> GetPhanCongByIdAsync(int id) =>
            await _context.PhanCongCongViecs
                .Include(p => p.MaNhanVienThucHienNavigation)
                .Include(p => p.MaNhanVienPhanCongNavigation)
                .FirstOrDefaultAsync(p => p.MaPhanCong == id);

        public async Task<List<PhanCongCongViec>> GetLichSuPhanCongAsync() =>
            await _context.PhanCongCongViecs
                .Include(p => p.MaNhanVienPhanCongNavigation)
                .Include(p => p.MaNhanVienThucHienNavigation)
                .OrderByDescending(p => p.NgayPhanCong)
                .AsNoTracking()
                .ToListAsync();

        public async Task<List<PhanCongCongViec>> GetLichSuPhanCongChiTietAsync() =>
            await _context.PhanCongCongViecs
                .Include(p => p.MaNhanVienPhanCongNavigation)
                .Include(p => p.MaNhanVienThucHienNavigation)
                .Include(p => p.HoSoBaoTri)!.ThenInclude(h => h!.MaThieBiNavigation)
                .Include(p => p.HoSoSuaChua)!.ThenInclude(h => h!.MaThieBiNavigation)
                .OrderByDescending(p => p.NgayPhanCong)
                .AsNoTracking()
                .ToListAsync();

        public async Task<List<PhanCongCongViec>> GetPhanCongCuaNhanVienAsync(int maNhanVien, string? loai, string? trangThaiPhanCong)
        {
            // Include cả MaHoSoSuaChuaNavigation: khi phân công N NV, chỉ PC đầu
            // gắn inverse HoSoSuaChua (qua HoSo.MaPhanCong); các PC còn lại dùng MaHoSoSuaChua.
            var q = _context.PhanCongCongViecs
                .Include(p => p.MaNhanVienPhanCongNavigation)
                .Include(p => p.MaNhanVienThucHienNavigation)
                .Include(p => p.HoSoBaoTri)!.ThenInclude(h => h!.MaThieBiNavigation)
                .Include(p => p.MaHoSoBaoTriNavigation)!.ThenInclude(h => h!.MaThieBiNavigation)
                .Include(p => p.HoSoSuaChua)!.ThenInclude(h => h!.MaThieBiNavigation)
                .Include(p => p.MaHoSoSuaChuaNavigation)!.ThenInclude(h => h!.MaThieBiNavigation)
                .Where(p => p.MaNhanVienThucHien == maNhanVien
                            && p.TrangThai != "Đã hủy");

            if (!string.IsNullOrWhiteSpace(trangThaiPhanCong))
                q = q.Where(p => p.TrangThai == trangThaiPhanCong);

            if (!string.IsNullOrWhiteSpace(loai))
            {
                if (loai.Equals("BaoTri", StringComparison.OrdinalIgnoreCase) || loai == "Bảo trì")
                    q = q.Where(p => p.HoSoBaoTri != null || p.MaHoSoBaoTri != null);
                else if (loai.Equals("SuaChua", StringComparison.OrdinalIgnoreCase) || loai == "Sửa chữa")
                    q = q.Where(p => p.HoSoSuaChua != null || p.MaHoSoSuaChua != null);
            }

            return await q.OrderByDescending(p => p.NgayPhanCong).AsNoTracking().ToListAsync();
        }

        public async Task<List<PhanCongCongViec>> GetPhanCongTheoHoSoBaoTriAsync(int maHoSoBaoTri)
        {
            return await _context.PhanCongCongViecs
                .Include(p => p.MaNhanVienThucHienNavigation)
                .Where(p => p.MaHoSoBaoTri == maHoSoBaoTri
                    || _context.HoSoBaoTris.Any(h => h.MaHoSoBaoTri == maHoSoBaoTri && h.MaPhanCong == p.MaPhanCong))
                .ToListAsync();
        }

        public async Task<HoSoBaoTri?> GetHoSoBaoTriByMaPhanCongAsync(int maPhanCong) =>
            await _context.HoSoBaoTris
                .Include(h => h.MaThieBiNavigation)
                .FirstOrDefaultAsync(h => h.MaPhanCong == maPhanCong);

        public async Task<HoSoSuaChua?> GetHoSoSuaChuaByMaPhanCongAsync(int maPhanCong)
        {
            // Ưu tiên MaHoSoSuaChua trên phân công (hỗ trợ nhiều NV); fallback HoSo.MaPhanCong
            var viaPc = await _context.PhanCongCongViecs
                .Where(p => p.MaPhanCong == maPhanCong && p.MaHoSoSuaChua != null)
                .Select(p => p.MaHoSoSuaChua!.Value)
                .FirstOrDefaultAsync();
            if (viaPc > 0)
                return await GetHoSoSuaChuaByIdAsync(viaPc);
            return await _context.HoSoSuaChuas
                .Include(h => h.MaThieBiNavigation)
                .FirstOrDefaultAsync(h => h.MaPhanCong == maPhanCong);
        }

        public void RemovePhanCong(PhanCongCongViec phanCong) =>
            _context.PhanCongCongViecs.Remove(phanCong);

        public void RemoveKetQua(KetQuaThucHien ketQua) =>
            _context.KetQuaThucHiens.Remove(ketQua);

        public async Task<KetQuaThucHien?> GetKetQuaByMaPhanCongAsync(int maPhanCong) =>
            await _context.KetQuaThucHiens.FirstOrDefaultAsync(k => k.MaPhanCong == maPhanCong);

        public async Task AddKetQuaAsync(KetQuaThucHien ketQua) =>
            await _context.KetQuaThucHiens.AddAsync(ketQua);

        public async Task AddLichSuPheDuyetAsync(LichSuPheDuyet lichSu) =>
            await _context.LichSuPheDuyets.AddAsync(lichSu);

        public async Task<List<LichSuPheDuyet>> GetLichSuPheDuyetAsync(string? loai, int? nam)
        {
            var q = _context.LichSuPheDuyets
                .Include(l => l.MaNhanVienDuyetNavigation)
                .Include(l => l.MaHoSoBaoTriNavigation)!.ThenInclude(h => h!.MaThieBiNavigation)
                .Include(l => l.MaHoSoBaoTriNavigation)!.ThenInclude(h => h!.MaNhanVienTaoNavigation)
                .Include(l => l.MaHoSoSuaChuaNavigation)!.ThenInclude(h => h!.MaThieBiNavigation)
                .Include(l => l.MaHoSoSuaChuaNavigation)!.ThenInclude(h => h!.MaNhanVienTaoNavigation)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(loai))
            {
                if (loai.Equals("BaoTri", StringComparison.OrdinalIgnoreCase) || loai == "Bảo trì")
                    q = q.Where(l => l.MaHoSoBaoTri != null);
                else if (loai.Equals("SuaChua", StringComparison.OrdinalIgnoreCase) || loai == "Sửa chữa")
                    q = q.Where(l => l.MaHoSoSuaChua != null);
            }

            if (nam.HasValue)
                q = q.Where(l => l.NgayDuyet.Year == nam.Value);

            return await q.OrderByDescending(l => l.NgayDuyet).AsNoTracking().ToListAsync();
        }

        
        public async Task AddYeuCauBaoTriAsync(YeuCauBaoTriThietBi yc) =>
            await _context.YeuCauBaoTriThietBis.AddAsync(yc);

        public async Task<YeuCauBaoTriThietBi?> GetYeuCauBaoTriByIdAsync(int id) =>
            await _context.YeuCauBaoTriThietBis
                .Include(y => y.MaThietBiNavigation)
                .Include(y => y.MaNhanVienYeuCauNavigation)
                .Include(y => y.HoSoBaoTri)
                .FirstOrDefaultAsync(y => y.MaYeuCauBaoTri == id);

        public async Task<List<YeuCauBaoTriThietBi>> GetYeuCauBaoTriListAsync(string? trangThai, int? nam, int? thang)
        {
            var q = _context.YeuCauBaoTriThietBis
                .Include(y => y.MaThietBiNavigation)
                .Include(y => y.MaNhanVienYeuCauNavigation)
                .Include(y => y.MaNhanVienXacNhanNavigation)
                .AsQueryable();
            if (!string.IsNullOrWhiteSpace(trangThai))
                q = q.Where(y => y.TrangThai == trangThai);
            if (nam.HasValue)
                q = q.Where(y => y.NamBaoTri == nam.Value);
            if (thang.HasValue)
                q = q.Where(y => y.ThangBaoTri == thang.Value);
            return await q.OrderByDescending(y => y.NgayTao).AsNoTracking().ToListAsync();
        }

        public async Task<bool> TonTaiYeuCauBaoTriThangAsync(int maThietBi, int nam, int thang) =>
            await _context.YeuCauBaoTriThietBis.AnyAsync(y =>
                y.MaThietBi == maThietBi
                && y.NamBaoTri == nam
                && y.ThangBaoTri == thang
                && y.TrangThai != "Từ chối");

        public async Task<bool> TonTaiYeuCauBaoTriThangKhacIdAsync(int maThietBi, int nam, int thang, int excludeId) =>
            await _context.YeuCauBaoTriThietBis.AnyAsync(y =>
                y.MaThietBi == maThietBi
                && y.NamBaoTri == nam
                && y.ThangBaoTri == thang
                && y.MaYeuCauBaoTri != excludeId
                && y.TrangThai != "Từ chối");

        public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();

        public async Task<ChiTietKeHoachBaoTri?> GetChiTietKeHoachByIdAsync(int id) =>
            await _context.ChiTietKeHoachBaoTris
                .Include(c => c.MaKeHoachNavigation)
                .FirstOrDefaultAsync(c => c.MaChiTietKeHoach == id);

        public async Task<ChiTietKeHoachBaoTri?> GetChiTietKeHoachByHoSoBaoTriAsync(int maHoSoBaoTri) =>
            await _context.ChiTietKeHoachBaoTris
                .Include(c => c.MaKeHoachNavigation)
                .FirstOrDefaultAsync(c => c.MaHoSoBaoTri == maHoSoBaoTri);

        public async Task<bool> TonTaiHoSoBaoTriTheoThietBiThangAsync(int maThietBi, int nam, int thang) =>
            await _context.ChiTietKeHoachBaoTris.AnyAsync(c =>
                c.MaThietBi == maThietBi
                && c.NgayDuKienBaoTri.Year == nam
                && c.NgayDuKienBaoTri.Month == thang
                && c.MaHoSoBaoTri != null);

        public async Task<bool> TonTaiChiTietHoacHoSoThietBiThangKhacAsync(
            int maThietBi, int nam, int thang, int? excludeMaChiTiet, int? excludeMaHoSo)
        {
            // 1) Đã có chi tiết kế hoạch khác cùng thiết bị + tháng
            var coChiTietKhac = await _context.ChiTietKeHoachBaoTris.AnyAsync(c =>
                c.MaThietBi == maThietBi
                && c.NgayDuKienBaoTri.Year == nam
                && c.NgayDuKienBaoTri.Month == thang
                && (!excludeMaChiTiet.HasValue || c.MaChiTietKeHoach != excludeMaChiTiet.Value));
            if (coChiTietKhac) return true;

            // 2) Đã có hồ sơ bảo trì khác (qua chi tiết) cùng thiết bị + tháng
            var coHoSoKhac = await _context.ChiTietKeHoachBaoTris.AnyAsync(c =>
                c.MaThietBi == maThietBi
                && c.NgayDuKienBaoTri.Year == nam
                && c.NgayDuKienBaoTri.Month == thang
                && c.MaHoSoBaoTri != null
                && (!excludeMaHoSo.HasValue || c.MaHoSoBaoTri != excludeMaHoSo.Value));
            return coHoSoKhac;
        }
    }
}