using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Models;

public partial class LichSuThietBi
{
    public int MaLichSu { get; set; }

    public int MaThietBi { get; set; }

    public int? MaHoSoBaoTri { get; set; }

    public int? MaHoSoSuaChua { get; set; }

    public DateTime? NgayHoanThanh { get; set; }

    public string? KetQua { get; set; }

    public string? GhiChu { get; set; }

    public virtual HoSoBaoTri? MaHoSoBaoTriNavigation { get; set; }

    public virtual HoSoSuaChua? MaHoSoSuaChuaNavigation { get; set; }

    public virtual ThietBi MaThietBiNavigation { get; set; } = null!;
}
