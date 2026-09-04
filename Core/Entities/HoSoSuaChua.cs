using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class HoSoSuaChua
{
    public int MaHoSoSuaChua { get; set; }

    public int MaThieBi { get; set; }

    public int MaNhanVienTao { get; set; }

    public int? MaNhanVienDuyet { get; set; }

    public int? MaPhanCong { get; set; }

    public string? MoTaHuHong { get; set; }

    public string? PhuongAnSuaChua { get; set; }

    public DateTime NgayTao { get; set; }

    public string TrangThai { get; set; } = null!;

    public string? LyDoTuChoi { get; set; }

    public DateTime? NgayDuyet { get; set; }

    public virtual HoSoYeuCauVatTu? HoSoYeuCauVatTu { get; set; }

    public virtual ICollection<LichSuPheDuyet> LichSuPheDuyets { get; set; } = new List<LichSuPheDuyet>();

    public virtual LichSuThietBi? LichSuThietBi { get; set; }

    public virtual NhanVien? MaNhanVienDuyetNavigation { get; set; }

    public virtual NhanVien MaNhanVienTaoNavigation { get; set; } = null!;

    public virtual PhanCongCongViec? MaPhanCongNavigation { get; set; }

    public virtual ThietBi MaThieBiNavigation { get; set; } = null!;
}
