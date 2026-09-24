namespace OPC.MaintenanceAPI.Core.Entities;

public partial class QuyTrinhThietBi
{
    public int MaQuyTrinh { get; set; }
    public int MaThietBi { get; set; }
    public string LoaiCongViec { get; set; } = null!;
    public int SoBuoc { get; set; }
    public string MoTaBuoc { get; set; } = null!;
    public int ThuTu { get; set; }

    public virtual ThietBi? MaThietBiNavigation { get; set; }
}
