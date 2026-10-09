using System;

namespace OPC.MaintenanceAPI.Core.Entities;

/// <summary>Thông báo đẩy tới đúng 1 nhân viên (vd. khi được phân công).</summary>
public class ThongBao
{
    public int MaThongBao { get; set; }
    public int MaNhanVienNhan { get; set; }
    public string TieuDe { get; set; } = null!;
    public string NoiDung { get; set; } = null!;
    /// <summary>PhanCongBT | PhanCongSC</summary>
    public string Loai { get; set; } = null!;
    public int? MaHoSoBaoTri { get; set; }
    public int? MaHoSoSuaChua { get; set; }
    public bool DaDoc { get; set; }
    public DateTime NgayTao { get; set; }
}
