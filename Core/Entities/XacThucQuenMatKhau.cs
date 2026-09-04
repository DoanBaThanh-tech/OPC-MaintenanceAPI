namespace OPC.MaintenanceAPI.Core.Entities;
    public class XacThucQuenMatKhau
    {
        public int MaXacThuc { get; set; }
        public int MaNguoiDung { get; set; }
        public string MaOTP { get; set; } = null!;
        public DateTime ThoiGianHetHan { get; set; }
        public string TrangThaiXacThuc { get; set; } = null!;

        public QuanLyNguoiDung MaNguoiDungNavigation { get; set; } = null!;
    }
