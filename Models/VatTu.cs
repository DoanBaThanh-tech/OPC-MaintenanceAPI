using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Models;

public partial class VatTu
{
    public int MaVatTu { get; set; }

    public string TenVatTu { get; set; } = null!;

    public string? DonViTinh { get; set; }

    public int SoLuongTonKho { get; set; }

    public int? MucTonKhoToiThieu { get; set; }

    public string? GhiChu { get; set; }

    public virtual ICollection<ChiTietYeuCauVatTu> ChiTietYeuCauVatTus { get; set; } = new List<ChiTietYeuCauVatTu>();

    public virtual ICollection<NhapXuatVatTu> NhapXuatVatTus { get; set; } = new List<NhapXuatVatTu>();
}
