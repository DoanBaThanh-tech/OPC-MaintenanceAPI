using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public class ThongBaoService : IThongBaoService
    {
        private readonly OPCDbContext _db;
        private readonly INhanVienRepository _nvRepo;

        public ThongBaoService(OPCDbContext db, INhanVienRepository nvRepo)
        {
            _db = db;
            _nvRepo = nvRepo;
        }

        public async Task TaoThongBaoPhanCongAsync(
            IEnumerable<int> maNhanVienNhans,
            string loai,
            string tenThietBi,
            int? maHoSoBaoTri,
            int? maHoSoSuaChua)
        {
            var isBt = loai == "PhanCongBT";
            var viec = isBt ? "bảo trì" : "sửa chữa";
            var tieuDe = isBt ? "Phân công bảo trì mới" : "Phân công sửa chữa mới";
            var noiDung =
                $"Bạn được phân công {viec} thiết bị «{tenThietBi}». Nhấn Tiến hành để mở công việc.";

            foreach (var maNv in maNhanVienNhans.Distinct())
            {
                _db.ThongBaos.Add(new ThongBao
                {
                    MaNhanVienNhan = maNv,
                    TieuDe = tieuDe,
                    NoiDung = noiDung,
                    Loai = loai,
                    MaHoSoBaoTri = maHoSoBaoTri,
                    MaHoSoSuaChua = maHoSoSuaChua,
                    DaDoc = false,
                    NgayTao = DateTime.Now
                });
            }
            await _db.SaveChangesAsync();
        }

        public async Task<object> LayDanhSachAsync(int maNguoiDung, bool? chiChuaDoc = null)
        {
            var nv = await _nvRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nv == null) return Array.Empty<object>();

            // Chỉ đánh dấu đã đọc khi NVKT ĐÃ BẮT ĐẦU quy trình (có ThoiDiemBatDauThucTe)
            // hoặc hồ sơ Đã hoàn thành — KHÔNG đánh dấu chỉ vì HS = "Đang thực hiện"
            // (trạng thái đó được set ngay lúc Tổ trưởng phân công).
            var chuaDoc = await _db.ThongBaos
                .Where(t => t.MaNhanVienNhan == nv.MaNhanVien && !t.DaDoc)
                .ToListAsync();

            if (chuaDoc.Count > 0)
            {
                var maBt = chuaDoc.Where(t => t.MaHoSoBaoTri != null)
                    .Select(t => t.MaHoSoBaoTri!.Value).Distinct().ToList();
                var maSc = chuaDoc.Where(t => t.MaHoSoSuaChua != null)
                    .Select(t => t.MaHoSoSuaChua!.Value).Distinct().ToList();

                // Đã Tiến hành / đã xong → tắt thông báo
                var btDaXuLy = maBt.Count == 0
                    ? new HashSet<int>()
                    : (await _db.HoSoBaoTris.AsNoTracking()
                        .Where(h => maBt.Contains(h.MaHoSoBaoTri) &&
                                    (h.ThoiDiemBatDauThucTe != null
                                     || h.TrangThai == "Đã hoàn thành"))
                        .Select(h => h.MaHoSoBaoTri)
                        .ToListAsync()).ToHashSet();

                var scDaXuLy = maSc.Count == 0
                    ? new HashSet<int>()
                    : (await _db.HoSoSuaChuas.AsNoTracking()
                        .Where(h => maSc.Contains(h.MaHoSoSuaChua) &&
                                    (h.ThoiDiemBatDauThucTe != null
                                     || h.TrangThai == "Đã hoàn thành"))
                        .Select(h => h.MaHoSoSuaChua)
                        .ToListAsync()).ToHashSet();

                var doi = false;
                foreach (var t in chuaDoc)
                {
                    var daXuLy = false;
                    if (t.MaHoSoBaoTri is int b && btDaXuLy.Contains(b)) daXuLy = true;
                    if (t.MaHoSoSuaChua is int s && scDaXuLy.Contains(s)) daXuLy = true;
                    if (daXuLy)
                    {
                        t.DaDoc = true;
                        doi = true;
                    }
                }
                if (doi) await _db.SaveChangesAsync();
            }

            var q = _db.ThongBaos.AsNoTracking()
                .Where(t => t.MaNhanVienNhan == nv.MaNhanVien);
            if (chiChuaDoc == true)
                q = q.Where(t => !t.DaDoc);

            var list = await q
                .OrderByDescending(t => t.NgayTao)
                .Take(100)
                .Select(t => new
                {
                    t.MaThongBao,
                    t.TieuDe,
                    t.NoiDung,
                    t.Loai,
                    t.MaHoSoBaoTri,
                    t.MaHoSoSuaChua,
                    t.DaDoc,
                    t.NgayTao
                })
                .ToListAsync();
            return list;
        }

        public async Task<(bool ok, string? loi)> DanhDauDaDocAsync(int maNguoiDung, int maThongBao)
        {
            var nv = await _nvRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nv == null) return (false, "Không xác định nhân viên.");

            var tb = await _db.ThongBaos.FirstOrDefaultAsync(t =>
                t.MaThongBao == maThongBao && t.MaNhanVienNhan == nv.MaNhanVien);
            if (tb == null) return (false, "Không tìm thấy thông báo.");
            tb.DaDoc = true;
            await _db.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool ok, string? loi)> DanhDauTatCaDaDocAsync(int maNguoiDung)
        {
            var nv = await _nvRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nv == null) return (false, "Không xác định nhân viên.");

            var ds = await _db.ThongBaos
                .Where(t => t.MaNhanVienNhan == nv.MaNhanVien && !t.DaDoc)
                .ToListAsync();
            foreach (var t in ds) t.DaDoc = true;
            await _db.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool ok, string? loi)> DanhDauTheoHoSoAsync(
            int maNguoiDung, int? maHoSoBaoTri, int? maHoSoSuaChua)
        {
            var nv = await _nvRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nv == null) return (false, "Không xác định nhân viên.");

            var q = _db.ThongBaos.Where(t => t.MaNhanVienNhan == nv.MaNhanVien && !t.DaDoc);
            if (maHoSoBaoTri is > 0)
                q = q.Where(t => t.MaHoSoBaoTri == maHoSoBaoTri);
            else if (maHoSoSuaChua is > 0)
                q = q.Where(t => t.MaHoSoSuaChua == maHoSoSuaChua);
            else
                return (false, "Thiếu mã hồ sơ.");

            var list = await q.ToListAsync();
            foreach (var item in list) item.DaDoc = true;
            await _db.SaveChangesAsync();
            return (true, null);
        }
    }
}
