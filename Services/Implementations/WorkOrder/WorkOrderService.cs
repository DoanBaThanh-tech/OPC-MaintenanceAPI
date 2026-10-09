using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Core.Exceptions;
using OPC.MaintenanceAPI.DTOs.WorkOrder;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;
using OPC.MaintenanceAPI.DTOs.Common;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class WorkOrderService : IWorkOrderService
    {
        // ---------- từ WorkOrderService.cs ----------
// ===== KHỞI TẠO =====

        private readonly IWorkOrderRepository _repo;
        private readonly INhanVienRepository _nhanVienRepo;
        private readonly OPC.MaintenanceAPI.Data.OPCDbContext _db;

        public WorkOrderService(
            IWorkOrderRepository repo,
            INhanVienRepository nhanVienRepo,
            OPC.MaintenanceAPI.Data.OPCDbContext db)
        {
            _repo = repo;
            _nhanVienRepo = nhanVienRepo;
            _db = db;
        }

        private async Task LuuBuocDuocChonKhiTaoAsync(
            int? maHoSoBaoTri,
            int? maHoSoSuaChua,
            List<KeHoachBuocItemDto>? danhSach)
        {
            if (danhSach == null || danhSach.Count == 0) return;
            foreach (var b in danhSach
                         .Where(x => x.SoBuoc > 0)
                         .GroupBy(x => x.SoBuoc)
                         .Select(g => g.First())
                         .OrderBy(x => x.SoBuoc))
            {
                _db.TienDoBuocQuyTrinhs.Add(new TienDoBuocQuyTrinh
                {
                    MaHoSoBaoTri = maHoSoBaoTri,
                    MaHoSoSuaChua = maHoSoSuaChua,
                    SoBuoc = b.SoBuoc,
                    MoTaBuoc = string.IsNullOrWhiteSpace(b.MoTaBuoc)
                        ? $"Bước {b.SoBuoc}"
                        : b.MoTaBuoc.Trim(),
                    TrangThai = "DuocChon",
                    MaNhanVien = 0,
                    ThoiDiemCapNhat = DateTime.Now
                });
            }
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Ngày dự kiến khi Xưởng sửa:
        /// - Chỉ trong đúng tháng/năm kế hoạch gốc (không đổi tháng).
        /// - Phải lớn hơn ngày tạo hồ sơ; không ở quá khứ.
        /// </summary>
        private static (bool ok, string? loi) _kiemTraNgayDuKienKhiSua(
            DateOnly ngayMoi, DateOnly ngayDuKienCu, DateTime ngayTao)
        {
            // Phải lớn hơn ngày hiện tại (không chọn hôm nay hoặc quá khứ)
            if (ngayMoi <= DateOnly.FromDateTime(DateTime.Today))
                return (false, "Ngày dự kiến bảo trì phải lớn hơn ngày hiện tại.");

            var ngayTaoOnly = DateOnly.FromDateTime(ngayTao);
            if (ngayMoi <= ngayTaoOnly)
                return (false,
                    $"Ngày dự kiến bảo trì ({ngayMoi:dd/MM/yyyy}) phải lớn hơn ngày tạo hồ sơ ({ngayTaoOnly:dd/MM/yyyy}). " +
                    "Không được đặt trùng hoặc trước ngày tạo.");

            if (ngayMoi.Year != ngayDuKienCu.Year || ngayMoi.Month != ngayDuKienCu.Month)
                return (false,
                    $"Xưởng chỉ được chọn ngày trong tháng kế hoạch {ngayDuKienCu.Month}/{ngayDuKienCu.Year}. " +
                    "Không được đổi sang tháng khác (tháng do Tổ trưởng cơ điện quyết định khi lập kế hoạch).");

            return (true, null);
        }

        /// <summary>
        /// Xưởng từ chối → mở lại bước đã «DaCapNhat» thành «DaXong» để NV chỉnh sửa lần nữa.
        /// </summary>
        private async Task MoLaiTienDoSauTuChoiAsync(int? maHoSoBaoTri, int? maHoSoSuaChua)
        {
            IQueryable<TienDoBuocQuyTrinh> q = _db.TienDoBuocQuyTrinhs;
            if (maHoSoBaoTri.HasValue)
                q = q.Where(t => t.MaHoSoBaoTri == maHoSoBaoTri);
            else if (maHoSoSuaChua.HasValue)
                q = q.Where(t => t.MaHoSoSuaChua == maHoSoSuaChua);
            else
                return;

            var list = await q.Where(t => t.TrangThai == "DaCapNhat").ToListAsync();
            foreach (var t in list)
                t.TrangThai = "DaXong";
        }

        /// <summary>
        /// Khi Xưởng xác nhận quy trình — chuyển HS vật tư sang trạng thái chỉ định
        /// (mặc định nghiệp vụ mới: "Chờ duyệt" = đã gửi Giám đốc, không qua Tổ trưởng).
        /// </summary>
        private async Task CapNhatTrangThaiHoSoVatTuTheoCongViecAsync(
            int? maHoSoBaoTri, int? maHoSoSuaChua, string trangThai)
        {
            IQueryable<HoSoSuDungVatTu> q = _db.HoSoSuDungVatTus;
            if (maHoSoBaoTri.HasValue)
                q = q.Where(h => h.MaHoSoBaoTri == maHoSoBaoTri);
            else if (maHoSoSuaChua.HasValue)
                q = q.Where(h => h.MaHoSoSuaChua == maHoSoSuaChua);
            else
                return;

            var list = await q.ToListAsync();
            var now = DateTime.Now;
            foreach (var h in list)
            {
                if (h.TrangThai is "Chờ gửi" or "Chờ duyệt" or "Đã gửi GĐ")
                {
                    h.TrangThai = trangThai;
                    if (trangThai is "Chờ duyệt" or "Đã gửi GĐ")
                        h.NgayGuiGiamDoc ??= now;
                }
            }
        }

        // ---------- từ XuLyTruyVanVaYeuCau.cs ----------

        // ===== TRUY VẤN =====



        // Helper dùng chung: tính Nam cho từng hồ sơ 1 lần, tái sử dụng cho cả lọc lẫn liệt kê năm có dữ liệu
        private async Task<List<(HoSoBaoTri hoSo, int nam, bool namTuKeHoach)>> LayDanhSachKemNamAsync(string? trangThai)
        {
            var list = await _repo.GetHoSoBaoTriByTrangThaiAsync(trangThai);
            var ketQua = new List<(HoSoBaoTri, int, bool)>();
            foreach (var h in list)
            {
                var namTuKeHoach = await _repo.GetNamKeHoachTheoHoSoBaoTriAsync(h.MaHoSoBaoTri);
                ketQua.Add((h, namTuKeHoach ?? h.NgayTao.Year, namTuKeHoach != null));
            }
            return ketQua;
        }

        /// <summary>
        /// Chỉ khóa đổi phân công khi đã có bước quy trình hoàn thành (DaXong/DaCapNhat)
        /// hoặc đang chờ Xưởng xác nhận. Không khóa vì cờ «người ghi chép» (đã bỏ nghiệp vụ đó).
        /// </summary>
        private async Task<(bool Ok, string? Loi)> KiemTraKhoaNguoiGhiChepAsync(
            List<PhanCongCongViec> pcHienTai,
            int maGhiChepMoi,
            List<int> dsNvMoi,
            int? maHoSoBaoTri,
            int? maHoSoSuaChua)
        {
            if (pcHienTai.Any(p => p.TrangThai == "Chờ xác nhận"))
                return (false, "Quy trình đang chờ Xưởng xác nhận — không được đổi phân công.");

            // Đã có bước Xong trên server → không cho bỏ NV đã làm bước
            if (maHoSoBaoTri.HasValue || maHoSoSuaChua.HasValue)
            {
                IQueryable<TienDoBuocQuyTrinh> q = _db.TienDoBuocQuyTrinhs;
                if (maHoSoBaoTri.HasValue)
                    q = q.Where(t => t.MaHoSoBaoTri == maHoSoBaoTri);
                else
                    q = q.Where(t => t.MaHoSoSuaChua == maHoSoSuaChua);

                var daLam = await q
                    .Where(t => t.TrangThai == "DaXong" || t.TrangThai == "DaCapNhat")
                    .Select(t => t.MaNhanVien)
                    .Distinct()
                    .ToListAsync();

                foreach (var maNv in daLam)
                {
                    if (!dsNvMoi.Contains(maNv))
                        return (false,
                            "Không được bỏ nhân viên đã hoàn thành bước quy trình khỏi phân công.");
                }
            }

            return (true, null);
        }

        /// <summary>
        /// Không bắt buộc chọn người ghi chép — mọi NV đều làm quy trình.
        /// Nếu client gửi mã hợp lệ thì dùng; không thì lấy NV đầu danh sách.
        /// </summary>
        private static (bool Ok, int MaGhiChep, string? Loi) ResolveNguoiGhiChep(
            List<int> dsNv, int? maNhanVienGhiChep)
        {
            if (dsNv.Count == 0)
                return (false, 0, "Vui lòng chọn ít nhất một nhân viên thực hiện.");
            if (maNhanVienGhiChep.HasValue &&
                maNhanVienGhiChep.Value > 0 &&
                dsNv.Contains(maNhanVienGhiChep.Value))
                return (true, maNhanVienGhiChep.Value, null);
            return (true, dsNv[0], null);
        }

        /// <summary>Mọi NV còn phân công hiệu lực đều được Tiến hành / Xong (không còn chỉ người ghi chép).</summary>
        private static bool LaNguoiDuocGhiChep(IEnumerable<PhanCongCongViec> dsPc, int maNhanVien)
        {
            var conHieuLuc = dsPc.Where(p => p.TrangThai is not ("Đã hủy" or "Hoàn thành")).ToList();
            if (conHieuLuc.Count == 0) return false;
            return conHieuLuc.Any(p => p.MaNhanVienThucHien == maNhanVien);
        }

        private static string FormatThoiLuongThucTe(DateTime batDau, DateTime ketThuc)
        {
            var span = ketThuc - batDau;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            var h = (int)span.TotalHours;
            return $"{h} giờ {span.Minutes} phút {span.Seconds} giây";
        }

        private static void GhiNhanKetThucThucTe(HoSoBaoTri hoSo)
        {
            var now = DateTime.Now;
            if (hoSo.ThoiDiemKetThucThucTe == null)
                hoSo.ThoiDiemKetThucThucTe = now;
            hoSo.GioKetThucDuKien = hoSo.ThoiDiemKetThucThucTe.Value.TimeOfDay;
            if (hoSo.ThoiDiemBatDauThucTe != null)
            {
                hoSo.ThoiGianDuKien = FormatThoiLuongThucTe(
                    hoSo.ThoiDiemBatDauThucTe.Value, hoSo.ThoiDiemKetThucThucTe.Value);
                hoSo.GioBatDauDuKien = hoSo.ThoiDiemBatDauThucTe.Value.TimeOfDay;
            }
        }

        private static void GhiNhanKetThucThucTe(HoSoSuaChua hoSo)
        {
            var now = DateTime.Now;
            if (hoSo.ThoiDiemKetThucThucTe == null)
                hoSo.ThoiDiemKetThucThucTe = now;
            hoSo.GioKetThucDuKien = hoSo.ThoiDiemKetThucThucTe.Value.TimeOfDay;
            if (hoSo.ThoiDiemBatDauThucTe != null)
            {
                hoSo.ThoiGianDuKien = FormatThoiLuongThucTe(
                    hoSo.ThoiDiemBatDauThucTe.Value, hoSo.ThoiDiemKetThucThucTe.Value);
                hoSo.GioBatDauDuKien = hoSo.ThoiDiemBatDauThucTe.Value.TimeOfDay;
            }
        }

        /// <summary>Tạo thông báo chỉ cho đúng các NV được phân công.</summary>
        protected async Task TaoThongBaoPhanCongNoiBoAsync(
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
                _db.ThongBaos.Add(new Core.Entities.ThongBao
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

    }
}
