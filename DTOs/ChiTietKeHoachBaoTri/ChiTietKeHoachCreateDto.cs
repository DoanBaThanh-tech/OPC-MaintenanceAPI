namespace OPC.MaintenanceAPI.DTOs.ChiTietKeHoachBaoTri
{
    public class ChiTietKeHoachCreateDto
    {
        public int MaKeHoach { get; set; }
        public int MaThietBi { get; set; }
        public DateOnly NgayDuKienBaoTri { get; set; }
    }
}