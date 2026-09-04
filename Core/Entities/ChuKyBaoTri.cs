using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class ChuKyBaoTri
{
    public int MaChuKy { get; set; }

    public string LoaiThietBi { get; set; } = null!;

    public int SoThangChuKyDeXuat { get; set; }

    public string? MoTa { get; set; }

    public virtual ICollection<KeHoachBaoTri> KeHoachBaoTris { get; set; } = new List<KeHoachBaoTri>();

    public virtual ICollection<ThietBi> ThietBis { get; set; } = new List<ThietBi>();
}
