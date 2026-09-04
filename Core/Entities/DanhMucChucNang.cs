using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class DanhMucChucNang
{
    public int MaChucNang { get; set; }

    public string TenChucNang { get; set; } = null!;

    public string? NhomChucNang { get; set; }

    public string? MoTa { get; set; }

    public virtual ICollection<PhanQuyenVaiTro> PhanQuyenVaiTros { get; set; } = new List<PhanQuyenVaiTro>();
}
