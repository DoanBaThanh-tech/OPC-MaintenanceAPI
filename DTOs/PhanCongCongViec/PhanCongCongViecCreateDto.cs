namespace OPC.MaintenanceAPI.DTOs.PhanCongCongViec
{
    public class PhanCongCongViecCreateDto
    {
        public int? MaHoSoBaoTri { get; set; }     // chỉ 1 trong 2 field này có giá trị
        public int? MaHoSoSuaChua { get; set; }
        public int MaNhanVienThucHien { get; set; }
        public DateOnly? NgayBatDauDuKien { get; set; }
        public DateOnly? NgayKetThucDuKien { get; set; }
    }
}