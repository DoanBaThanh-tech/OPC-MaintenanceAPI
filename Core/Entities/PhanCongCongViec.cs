using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class PhanCongCongViec
{
    public int MaPhanCong { get; set; }

    public int MaNhanVienThucHien { get; set; }

    public int MaNhanVienPhanCong { get; set; }

    public DateTime? NgayBatDauDuKien { get; set; }
    public DateTime? NgayKetThucDuKien { get; set; }

    public string? TrangThai { get; set; }

    public DateTime NgayPhanCong { get; set; }

    public virtual HoSoBaoTri? HoSoBaoTri { get; set; }

    public virtual HoSoSuaChua? HoSoSuaChua { get; set; }

    public virtual KetQuaThucHien? KetQuaThucHien { get; set; }

    public virtual NhanVien MaNhanVienPhanCongNavigation { get; set; } = null!;

    public virtual NhanVien MaNhanVienThucHienNavigation { get; set; } = null!;
}
