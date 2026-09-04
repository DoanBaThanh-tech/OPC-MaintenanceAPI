using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class PhanQuyenVaiTro
{
    public int MaPhanQuyen { get; set; }

    public int MaVaiTro { get; set; }

    public int MaChucNang { get; set; }

    public bool DuocXem { get; set; }

    public bool DuocTao { get; set; }

    public bool DuocSua { get; set; }

    public bool DuocDuyet { get; set; }

    public virtual DanhMucChucNang MaChucNangNavigation { get; set; } = null!;

    public virtual VaiTro MaVaiTroNavigation { get; set; } = null!;
}
