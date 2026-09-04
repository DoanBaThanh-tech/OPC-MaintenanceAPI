using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class ChiTietKeHoachBaoTri
{
    public int MaChiTietKeHoach { get; set; }

    public int MaKeHoach { get; set; }

    public int MaThietBi { get; set; }

    public int? MaHoSoBaoTri { get; set; }

    public DateOnly NgayDuKienBaoTri { get; set; }

    public string? TrangThai { get; set; }

    public string? GhiChu { get; set; }

    public virtual HoSoBaoTri? MaHoSoBaoTriNavigation { get; set; }

    public virtual KeHoachBaoTri MaKeHoachNavigation { get; set; } = null!;

    public virtual ThietBi MaThietBiNavigation { get; set; } = null!;
}
