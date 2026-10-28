using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Data;

namespace OPC.MaintenanceAPI.Services.Implementations;

/// <summary>One source of material quantities, stored costs and director-chart dates.</summary>
public static class ThongKeVatTuDongBo
{
    public sealed class HoSoThongKe
    {
        public int MaHoSoVatTu { get; init; }
        public DateOnly NgayThongKe { get; init; }
        public DateOnly NgayThucHien { get; init; }
        public decimal SoLuong { get; init; }
        public decimal TongTien { get; init; }
    }

    public sealed class TheoNgay
    {
        public DateOnly Ngay { get; init; }
        public decimal SoLuong { get; init; }
        public decimal TongTien { get; init; }
    }

    /// <summary>
    /// Fresh, untracked, eligible projection. Bounds are inclusive/exclusive.
    /// IMPORTANT: attribution is deliberately dependent on the requested chart year.
    /// With both links: completed BT whose first plan/completion is in that year wins;
    /// then completed SC in that year; then BT completion fallback if a BT ID exists;
    /// then SC completion fallback; finally unlinked submission/execution date.
    /// A fallback link that is no longer completed (including cancellation) is excluded.
    /// Do not replace this with a global plan-first rule: that would change the app.
    /// </summary>
    public static IQueryable<HoSoThongKe> TaoTruyVanHoSo(
        OPCDbContext db, int nam, DateOnly? tu = null, DateOnly? den = null,
        bool theoNgayThucHien = false)
    {
        if (nam is < 2026 or > 2100) throw new ArgumentOutOfRangeException(nameof(nam));

        var accepted = new[] { "Xác nhận", "Đã xác nhận", "Đã duyệt", "Xac nhan", "Da duyet" };
        var source = db.HoSoSuDungVatTus.AsNoTracking()
            .Where(h => accepted.Contains(h.TrangThai))
            .Select(h => new
            {
                HoSo = h,
                BtDone = h.MaHoSoBaoTriNavigation != null
                    && h.MaHoSoBaoTriNavigation.TrangThai == "Đã hoàn thành",
                ScDone = h.MaHoSoSuaChuaNavigation != null
                    && h.MaHoSoSuaChuaNavigation.TrangThai == "Đã hoàn thành",
                NgayBt = h.MaHoSoBaoTriNavigation == null ? (DateOnly?)null
                    : DateOnly.FromDateTime(h.MaHoSoBaoTriNavigation.ThoiDiemKetThucThucTe
                        ?? h.MaHoSoBaoTriNavigation.NgayDuyet ?? h.MaHoSoBaoTriNavigation.NgayTao),
                NgaySc = h.MaHoSoSuaChuaNavigation == null ? (DateOnly?)null
                    : DateOnly.FromDateTime(h.MaHoSoSuaChuaNavigation.ThoiDiemKetThucThucTe
                        ?? h.MaHoSoSuaChuaNavigation.NgayDuyet ?? h.MaHoSoSuaChuaNavigation.NgayTao),
                NgayKeHoach = db.ChiTietKeHoachBaoTris.AsNoTracking()
                    .Where(c => h.MaHoSoBaoTri != null && c.MaHoSoBaoTri == h.MaHoSoBaoTri)
                    .OrderBy(c => c.MaChiTietKeHoach)
                    .Select(c => (DateOnly?)c.NgayDuKienBaoTri).FirstOrDefault()
            })
            .Select(x => new
            {
                x.HoSo,
                Ngay = x.BtDone && (x.NgayKeHoach ?? x.NgayBt)!.Value.Year == nam
                    ? x.NgayKeHoach ?? x.NgayBt
                    : x.ScDone && x.NgaySc!.Value.Year == nam ? x.NgaySc
                    : x.HoSo.MaHoSoBaoTri != null ? (x.BtDone ? x.NgayBt : null)
                    : x.HoSo.MaHoSoSuaChua != null ? (x.ScDone ? x.NgaySc : null)
                    : (DateOnly?)DateOnly.FromDateTime(x.HoSo.NgayGuiGiamDoc ?? x.HoSo.NgayThucHien)
            })
            .Where(x => x.Ngay != null)
            .Select(x => new HoSoThongKe
            {
                MaHoSoVatTu = x.HoSo.MaHoSoVatTu,
                NgayThongKe = x.Ngay!.Value,
                NgayThucHien = DateOnly.FromDateTime(x.HoSo.NgayThucHien),
                // Raw detail quantity, not distinct material types; cast BEFORE SUM to avoid int overflow.
                SoLuong = x.HoSo.ChiTietSuDungVatTus.Sum(c => (decimal)c.SoLuong),
                // The chart uses the stored header amount, never quantity * unit price.
                TongTien = x.HoSo.TongTien
            });

        if (theoNgayThucHien)
        {
            source = source.Where(x => x.NgayThucHien.Year == nam);
            if (tu != null) source = source.Where(x => x.NgayThucHien >= tu.Value);
            if (den != null) source = source.Where(x => x.NgayThucHien < den.Value);
        }
        else
        {
            source = source.Where(x => x.NgayThongKe.Year == nam);
            if (tu != null) source = source.Where(x => x.NgayThongKe >= tu.Value);
            if (den != null) source = source.Where(x => x.NgayThongKe < den.Value);
        }
        return source;
    }

    /// <summary>Public SQL aggregate for translation/source parity tests; no entity graph or caps.</summary>
    public static IQueryable<TheoNgay> TaoTruyVanTheoNgay(
        OPCDbContext db, int nam, DateOnly? tu = null, DateOnly? den = null,
        bool theoNgayThucHien = false) =>
        TaoTruyVanHoSo(db, nam, tu, den, theoNgayThucHien)
            .GroupBy(x => theoNgayThucHien ? x.NgayThucHien : x.NgayThongKe)
            .Select(g => new TheoNgay
            {
                Ngay = g.Key,
                SoLuong = g.Sum(x => x.SoLuong),
                TongTien = g.Sum(x => x.TongTien)
            });

    public static Task<List<TheoNgay>> LayTheoNgayAsync(
        OPCDbContext db, int nam, DateOnly? tu = null, DateOnly? den = null,
        bool theoNgayThucHien = false, CancellationToken ct = default) =>
        TaoTruyVanTheoNgay(db, nam, tu, den, theoNgayThucHien).ToListAsync(ct);

    /// <summary>Header-level rows, used when one requested period crosses a year boundary.</summary>
    public static Task<List<HoSoThongKe>> LayHoSoAsync(
        OPCDbContext db, int nam, DateOnly? tu = null, DateOnly? den = null,
        bool theoNgayThucHien = false, CancellationToken ct = default) =>
        TaoTruyVanHoSo(db, nam, tu, den, theoNgayThucHien).ToListAsync(ct);
}
