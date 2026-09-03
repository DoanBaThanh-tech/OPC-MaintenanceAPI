using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Models;

public partial class VaiTro
{
    public int MaVaiTro { get; set; }

    public string TenVaiTro { get; set; } = null!;

    public int? CapDoQuyen { get; set; }

    public string? MoTa { get; set; }

    public bool TrangThai { get; set; }

    public DateTime NgayTao { get; set; }

    public virtual ICollection<PhanQuyenVaiTro> PhanQuyenVaiTros { get; set; } = new List<PhanQuyenVaiTro>();

    public virtual ICollection<QuanLyNguoiDung> QuanLyNguoiDungs { get; set; } = new List<QuanLyNguoiDung>();
}
