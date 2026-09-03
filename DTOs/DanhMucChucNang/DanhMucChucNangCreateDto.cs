namespace OPC.MaintenanceAPI.DTOs.DanhMucChucNang
{
    public class DanhMucChucNangCreateDto
    {
        public string TenChucNang { get; set; } = null!;
        public string? NhomChucNang { get; set; }
        public string? MoTa { get; set; }
    }
}