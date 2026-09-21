using System;

namespace OPC.MaintenanceAPI.Core.Entities;

/// <summary>
/// Yêu cầu bảo trì thiết bị do Tổ trưởng cơ điện gửi → Xưởng (Tổ trưởng sản xuất)
/// Đồng ý / Từ chối trước khi được tạo kế hoạch / hồ sơ bảo trì.
/// </summary>
public partial class YeuCauBaoTriThietBi
{
    public int MaYeuCauBaoTri { get; set; }

    public int MaThietBi { get; set; }

    /// <summary>Tổ trưởng cơ điện gửi yêu cầu.</summary>
    public int MaNhanVienYeuCau { get; set; }

    /// <summary>Xưởng (Tổ trưởng sản xuất) xác nhận / từ chối.</summary>
    public int? MaNhanVienXacNhan { get; set; }

    /// <summary>Tháng bảo trì (1–12), khớp với NgayBaoTri.</summary>
    public int ThangBaoTri { get; set; }

    /// <summary>Năm bảo trì.</summary>
    public int NamBaoTri { get; set; }

    public DateOnly NgayBaoTri { get; set; }

    /// <summary>Số giờ dự kiến (> 0).</summary>
    public decimal ThoiGianDuKien { get; set; }

    public TimeSpan GioBatDau { get; set; }

    public TimeSpan GioKetThuc { get; set; }

    /// <summary>Chờ xác nhận | Đã xác nhận | Từ chối | Đã tạo hồ sơ</summary>
    public string TrangThai { get; set; } = "Chờ xác nhận";

    public string? LyDoTuChoi { get; set; }

    public string? GhiChu { get; set; }

    public DateTime NgayTao { get; set; }

    public DateTime? NgayXacNhan { get; set; }

    public virtual ThietBi MaThietBiNavigation { get; set; } = null!;
    public virtual NhanVien MaNhanVienYeuCauNavigation { get; set; } = null!;
    public virtual NhanVien? MaNhanVienXacNhanNavigation { get; set; }
    public virtual HoSoBaoTri? HoSoBaoTri { get; set; }
}
