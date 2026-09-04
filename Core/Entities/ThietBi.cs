using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class ThietBi
{
    public int MaThietBi { get; set; }

    public int MaChuKy { get; set; }

    public string TenThietBi { get; set; } = null!;

    public string? LoaiThietBi { get; set; }

    public string? ViTriLapDat { get; set; }

    public DateOnly NgayLapDat { get; set; }

    public string? TinhTrangHienTai { get; set; }

    public string? GhiChu { get; set; }

    public DateOnly? NgayBaoTriGanNhat { get; set; }

    public DateOnly? NgayBaoTriTiepTheo { get; set; }

    public int? SoThangDeXuat { get; set; }

    public virtual ICollection<ChiTietKeHoachBaoTri> ChiTietKeHoachBaoTris { get; set; } = new List<ChiTietKeHoachBaoTri>();

    public virtual ICollection<HoSoBaoTri> HoSoBaoTris { get; set; } = new List<HoSoBaoTri>();

    public virtual ICollection<HoSoSuaChua> HoSoSuaChuas { get; set; } = new List<HoSoSuaChua>();

    public virtual ICollection<LichSuThietBi> LichSuThietBis { get; set; } = new List<LichSuThietBi>();

    public virtual ChuKyBaoTri MaChuKyNavigation { get; set; } = null!;
}
