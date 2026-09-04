using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class NhapXuatVatTu
{
    public int MaGiaoDich { get; set; }

    public int MaVatTu { get; set; }

    public int MaNhanVienGiaoDich { get; set; }

    public int? MaYeuCauVatTu { get; set; }

    public string LoaiGiaoDich { get; set; } = null!;

    public int SoLuong { get; set; }

    public DateTime NgayGiaoDich { get; set; }

    public string? GhiChu { get; set; }

    public virtual NhanVien MaNhanVienGiaoDichNavigation { get; set; } = null!;

    public virtual VatTu MaVatTuNavigation { get; set; } = null!;

    public virtual HoSoYeuCauVatTu? MaYeuCauVatTuNavigation { get; set; }
}
