using System;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class NhatKyHeThong
{
    public int MaNhatKy { get; set; }
    public int MaNhanVien { get; set; }
    public string TenApi { get; set; } = null!;
    public string PhuongThucHttp { get; set; } = null!;
    public DateTime ThoiGianTruyCap { get; set; }
    public string? DiaChiIp { get; set; }
    public int? StatusCode { get; set; }
    public string? QueryString { get; set; }
    public string? LoaiHanhDong { get; set; }
    public string? ChiTiet { get; set; }

    public virtual NhanVien MaNhanVienNavigation { get; set; } = null!;
}
