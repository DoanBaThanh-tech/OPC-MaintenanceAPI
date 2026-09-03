namespace OPC.MaintenanceAPI.DTOs.HoSoBaoTri
{
    public class HoSoBaoTriCreateDto
    {
        public int MaChiTietKeHoach { get; set; }   // hồ sơ bắt buộc xuất phát từ 1 dòng kế hoạch
        public string? NoiDungCongViec { get; set; }
        public string? ThoiGianDuKien { get; set; }
    }
}