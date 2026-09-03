using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Models;

public partial class LichSuPheDuyet
{
    public int MaPheDuyet { get; set; }

    public int? MaHoSoBaoTri { get; set; }

    public int? MaHoSoSuaChua { get; set; }

    public int? MaYeuCauVatTu { get; set; }

    public int MaNhanVienDuyet { get; set; }

    public string? QuyetDinh { get; set; }

    public string? LyDo { get; set; }

    public DateTime NgayDuyet { get; set; }

    public virtual HoSoBaoTri? MaHoSoBaoTriNavigation { get; set; }

    public virtual HoSoSuaChua? MaHoSoSuaChuaNavigation { get; set; }

    public virtual NhanVien MaNhanVienDuyetNavigation { get; set; } = null!;

    public virtual HoSoYeuCauVatTu? MaYeuCauVatTuNavigation { get; set; }
}
