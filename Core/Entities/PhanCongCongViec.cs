using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class PhanCongCongViec
{
    public int MaPhanCong { get; set; }

    public int MaNhanVienThucHien { get; set; }

    public int MaNhanVienPhanCong { get; set; }

    /// <summary>Liên kết nhiều phân công tới cùng một hồ sơ bảo trì (hỗ trợ chọn nhiều nhân viên).</summary>
    public int? MaHoSoBaoTri { get; set; }

    /// <summary>Liên kết nhiều phân công tới cùng một hồ sơ sửa chữa.</summary>
    public int? MaHoSoSuaChua { get; set; }

    public DateTime? NgayBatDauDuKien { get; set; }
    public DateTime? NgayKetThucDuKien { get; set; }

    public string? TrangThai { get; set; }

    /// Lý do từ chối nhận việc của nhân viên kỹ thuật (nếu có)
    public string? LyDoTuChoi { get; set; }

    public DateTime NgayPhanCong { get; set; }

    public virtual HoSoBaoTri? HoSoBaoTri { get; set; }

    /// <summary>Navigation khi phân công nhiều NV cho cùng hồ sơ bảo trì (qua MaHoSoBaoTri).</summary>
    public virtual HoSoBaoTri? MaHoSoBaoTriNavigation { get; set; }

    public virtual HoSoSuaChua? HoSoSuaChua { get; set; }

    public virtual HoSoSuaChua? MaHoSoSuaChuaNavigation { get; set; }

    public virtual KetQuaThucHien? KetQuaThucHien { get; set; }

    public virtual NhanVien MaNhanVienPhanCongNavigation { get; set; } = null!;

    public virtual NhanVien MaNhanVienThucHienNavigation { get; set; } = null!;
}
