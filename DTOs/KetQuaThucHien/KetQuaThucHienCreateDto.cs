namespace OPC.MaintenanceAPI.DTOs.KetQuaThucHien
{
    public class KetQuaThucHienCreateDto
    {
        public int MaPhanCong { get; set; }
        public string? SoLieuGhiNhan { get; set; }
        public string? HinhAnh { get; set; }
        public string? GhiChu { get; set; }
        public bool XacNhanHoanThanh { get; set; }
        public int? SoThangDeXuatTiepTheo { get; set; }   // chỉ áp dụng nếu là hồ sơ bảo trì
    }
}