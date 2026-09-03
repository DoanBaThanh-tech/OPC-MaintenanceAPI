using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Models;

public partial class NhatKyHeThong
{
    public int MaNhatKy { get; set; }

    public int MaNhanVien { get; set; }

    public string TenApi { get; set; } = null!;

    public string PhuongThucHttp { get; set; } = null!;

    public DateTime ThoiGianTruyCap { get; set; }

    public string? DiaChiIp { get; set; }

    public virtual NhanVien MaNhanVienNavigation { get; set; } = null!;
}
