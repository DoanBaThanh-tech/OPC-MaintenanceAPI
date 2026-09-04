using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class ChiTietYeuCauVatTu
{
    public int MaChiTietYeuCauVatTu { get; set; }

    public int MaYeuCauVatTu { get; set; }

    public int MaVatTu { get; set; }

    public int SoLuongYeuCau { get; set; }

    public virtual VatTu MaVatTuNavigation { get; set; } = null!;

    public virtual HoSoYeuCauVatTu MaYeuCauVatTuNavigation { get; set; } = null!;
}
