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
    }
}
