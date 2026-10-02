using System;

namespace OPC.MaintenanceAPI.Core.Entities;

/// <summary>
/// Tiến độ từng bước quy trình BT/SC — lưu khi NVKT Xong bước; khóa bước khi đang làm.
/// </summary>
public partial class TienDoBuocQuyTrinh
{
    public int MaTienDo { get; set; }
    public int? MaHoSoBaoTri { get; set; }
    public int? MaHoSoSuaChua { get; set; }
    public int SoBuoc { get; set; }
    public string MoTaBuoc { get; set; } = "";
    public int MaNhanVien { get; set; }
    public string? TenNhanVien { get; set; }
    /// <summary>DangLam | DaXong</summary>
    public string TrangThai { get; set; } = "DangLam";
    /// <summary>JSON vật tư đã chọn (optional).</summary>
    public string? JsonVatTu { get; set; }
    public DateTime ThoiDiemCapNhat { get; set; }
}
