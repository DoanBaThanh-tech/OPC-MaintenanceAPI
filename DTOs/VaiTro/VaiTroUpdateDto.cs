namespace OPC.MaintenanceAPI.DTOs.VaiTro
{
    public class VaiTroUpdateDto
    {
        public int MaVaiTro { get; set; }
        public string TenVaiTro { get; set; } = null!;
        public int? CapDoQuyen { get; set; }
        public string? MoTa { get; set; }
        public bool TrangThai { get; set; }
    }
}