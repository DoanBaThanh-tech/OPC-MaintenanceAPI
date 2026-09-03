namespace OPC.MaintenanceAPI.DTOs.NhapXuatVatTu
{
    public class NhapXuatVatTuCreateDto
    {
        public int MaVatTu { get; set; }
        public int? MaYeuCauVatTu { get; set; }
        public string LoaiGiaoDich { get; set; } = null!;   // "Nhập" / "Xuất"
        public int SoLuong { get; set; }
        public string? GhiChu { get; set; }
    }
}