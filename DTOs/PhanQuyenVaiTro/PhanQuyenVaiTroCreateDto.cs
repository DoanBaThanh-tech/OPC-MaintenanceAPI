namespace OPC.MaintenanceAPI.DTOs.PhanQuyenVaiTro
{
    public class PhanQuyenVaiTroCreateDto
    {
        public int MaVaiTro { get; set; }
        public int MaChucNang { get; set; }
        public bool DuocXem { get; set; }
        public bool DuocTao { get; set; }
        public bool DuocSua { get; set; }
        public bool DuocDuyet { get; set; }
    }
}