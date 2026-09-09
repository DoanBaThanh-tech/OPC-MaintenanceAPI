namespace OPC.MaintenanceAPI.Core.Entities
{
    public class YeuCauNgayBaoTri
    {
        public int MaYeuCauNgayBaoTri { get; set; }
        public int MaThietBi { get; set; }
        public int MaNhanVienTao { get; set; }
        public int Nam { get; set; }
        public DateOnly NgayBaoTri { get; set; }
        public string TrangThai { get; set; } = "Chưa lập kế hoạch"; // / "Đã lập kế hoạch"
        public int? MaKeHoach { get; set; }
        public DateTime NgayTao { get; set; } = DateTime.Now;

        public ThietBi? MaThietBiNavigation { get; set; }
        public NhanVien? MaNhanVienTaoNavigation { get; set; }
        public KeHoachBaoTri? MaKeHoachNavigation { get; set; }
    }
}