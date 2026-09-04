namespace OPC.MaintenanceAPI.DTOs.MaintenancePlan
{
    public class LapKeHoachDto
    {
        public int Nam { get; set; }
        public int MaNhanVienLap { get; set; }
        public List<ChiTietKeHoachInputDto> ThietBiDuocChon { get; set; } = new();
    }

    public class ChiTietKeHoachInputDto
    {
        public int MaThietBi { get; set; }
        public DateOnly NgayDuKienBaoTri { get; set; }
    }

    public class ChiTietKeHoachDto
    {
        public int MaChiTietKeHoach { get; set; }
        public string? TenThietBi { get; set; }
        public DateOnly NgayDuKienBaoTri { get; set; }
    }
}