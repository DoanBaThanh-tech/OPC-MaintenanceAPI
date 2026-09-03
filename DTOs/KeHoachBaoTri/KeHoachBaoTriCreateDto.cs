namespace OPC.MaintenanceAPI.DTOs.KeHoachBaoTri
{
    public class KeHoachBaoTriCreateDto
    {
        public int MaChuKy { get; set; }
        public int Nam { get; set; }
        public DateOnly NgayLapKeHoach { get; set; }
    }
}