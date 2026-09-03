namespace OPC.MaintenanceAPI.DTOs.VaiTro
{
    public class VaiTroCreateDto
    {
        public string TenVaiTro { get; set; } = null!;
        public int? CapDoQuyen { get; set; }
        public string? MoTa { get; set; }
    }
}