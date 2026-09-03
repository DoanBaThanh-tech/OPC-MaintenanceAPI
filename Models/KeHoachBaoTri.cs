using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Models;

public partial class KeHoachBaoTri
{
    public int MaKeHoach { get; set; }

    public int MaChuKy { get; set; }

    public int MaNhanVienLap { get; set; }

    public int Nam { get; set; }

    public DateOnly NgayLapKeHoach { get; set; }

    public string? TrangThai { get; set; }

    public DateTime NgayTao { get; set; }

    public virtual ICollection<ChiTietKeHoachBaoTri> ChiTietKeHoachBaoTris { get; set; } = new List<ChiTietKeHoachBaoTri>();

    public virtual ChuKyBaoTri MaChuKyNavigation { get; set; } = null!;

    public virtual NhanVien MaNhanVienLapNavigation { get; set; } = null!;
}
