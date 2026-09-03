namespace OPC.MaintenanceAPI.DTOs.VatTu
{
    public class VatTuUpdateDto
    {
        public int MaVatTu { get; set; }
        public string TenVatTu { get; set; } = null!;
        public string? DonViTinh { get; set; }
        public int? MucTonKhoToiThieu { get; set; }
        public string? GhiChu { get; set; }
    }
}