namespace OPC.MaintenanceAPI.DTOs.Inventory
{
    public class KiemTraVatTuDto
    {
        public int MaVatTu { get; set; }
        public int SoLuongCanDung { get; set; }
    }

    public class TaoYeuCauVatTuDto
    {
        public int MaHoSoSuaChua { get; set; }
        public int MaNhanVienTao { get; set; }
        public List<ChiTietYeuCauInputDto> ChiTiet { get; set; } = new();
    }

    public class ChiTietYeuCauInputDto
    {
        public int MaVatTu { get; set; }
        public int SoLuongYeuCau { get; set; }
    }

    public class NhapKhoDto
    {
        public int MaVatTu { get; set; }
        public int SoLuong { get; set; }
        public int MaNhanVienGiaoDich { get; set; }
    }
}