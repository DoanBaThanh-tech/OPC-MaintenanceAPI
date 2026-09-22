using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class PhanCongCongViec
{
    public int MaPhanCong { get; set; }

    public int MaNhanVienThucHien { get; set; }

    public int MaNhanVienPhanCong { get; set; }

    /// <summary>Liên kết hồ sơ bảo trì (hỗ trợ phân công nhiều NV cho 1 hồ sơ).</summary>
    public int? MaHoSoBaoTri { get; set; }

    public DateTime? NgayBatDauDuKien { get; set; }
    public DateTime? NgayKetThucDuKien { get; set; }

    public string? TrangThai { get; set; }

    /// Lý do từ chối nhận việc của nhân viên kỹ thuật (nếu có) — không dùng ở luồng mới
    public string? LyDoTuChoi { get; set; }

    public DateTime NgayPhanCong { get; set; }

    /// Navigation 1-1 cũ qua HoSoBaoTri.MaPhanCong (giữ tương thích)
    public virtual HoSoBaoTri? HoSoBaoTri { get; set; }

    /// Navigation nhiều phân công → 1 hồ sơ (luồng mới)
    public virtual HoSoBaoTri? MaHoSoBaoTriNavigation { get; set; }

    public virtual HoSoSuaChua? HoSoSuaChua { get; set; }

    public virtual KetQuaThucHien? KetQuaThucHien { get; set; }

    public virtual NhanVien MaNhanVienPhanCongNavigation { get; set; } = null!;

    public virtual NhanVien MaNhanVienThucHienNavigation { get; set; } = null!;
}