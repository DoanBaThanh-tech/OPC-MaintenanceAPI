using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Models;

public partial class KetQuaThucHien
{
    public int MaKetQua { get; set; }

    public int MaPhanCong { get; set; }

    public int MaNhanVienGhiNhan { get; set; }

    public string? SoLieuGhiNhan { get; set; }

    public string? HinhAnh { get; set; }

    public string? GhiChu { get; set; }

    public DateTime NgayGhiNhan { get; set; }

    public bool XacNhanHoanThanh { get; set; }

    public virtual NhanVien MaNhanVienGhiNhanNavigation { get; set; } = null!;

    public virtual PhanCongCongViec MaPhanCongNavigation { get; set; } = null!;
}
