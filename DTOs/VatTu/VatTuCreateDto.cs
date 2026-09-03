namespace OPC.MaintenanceAPI.DTOs.VatTu
{
    public class VatTuCreateDto
    {
        public string TenVatTu { get; set; } = null!;
        public string? DonViTinh { get; set; }
        public int SoLuongTonKho { get; set; }
        public int? MucTonKhoToiThieu { get; set; }
        public string? GhiChu { get; set; }
    }
}