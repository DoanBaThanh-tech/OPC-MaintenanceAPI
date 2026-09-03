namespace OPC.MaintenanceAPI.DTOs.HoSoYeuCauVatTu
{
    public class HoSoYeuCauVatTuCreateDto
    {
        public int MaHoSoSuaChua { get; set; }
        public List<ChiTietVatTuDto> ChiTiet { get; set; } = new();
    }
}