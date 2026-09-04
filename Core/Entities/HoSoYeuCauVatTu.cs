using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class HoSoYeuCauVatTu
{
    public int MaYeuCauVatTu { get; set; }

    public int MaHoSoSuaChua { get; set; }

    public int MaNhanVienTao { get; set; }

    public int? MaNhanVienDuyet { get; set; }

    public DateTime NgayTao { get; set; }

    public string TrangThai { get; set; } = null!;

    public string? LyDoTuChoi { get; set; }

    public DateTime? NgayDuyet { get; set; }

    public virtual ICollection<ChiTietYeuCauVatTu> ChiTietYeuCauVatTus { get; set; } = new List<ChiTietYeuCauVatTu>();

    public virtual ICollection<LichSuPheDuyet> LichSuPheDuyets { get; set; } = new List<LichSuPheDuyet>();

    public virtual HoSoSuaChua MaHoSoSuaChuaNavigation { get; set; } = null!;

    public virtual NhanVien? MaNhanVienDuyetNavigation { get; set; }

    public virtual NhanVien MaNhanVienTaoNavigation { get; set; } = null!;

    public virtual ICollection<NhapXuatVatTu> NhapXuatVatTus { get; set; } = new List<NhapXuatVatTu>();
}
