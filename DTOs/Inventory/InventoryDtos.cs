namespace OPC.MaintenanceAPI.DTOs.Inventory
{
    public class KiemTraVatTuDto
    {
        public int MaVatTu { get; set; }
        public int SoLuongCanDung { get; set; }
    }

    public class TaoYeuCauVatTuDto
    {
        public int MaHoSoSuaChua { get; set; }
        public int MaNhanVienTao { get; set; }
        public List<ChiTietYeuCauInputDto> ChiTiet { get; set; } = new();
    }

    public class ChiTietYeuCauInputDto
    {
        public int MaVatTu { get; set; }
        public int SoLuongYeuCau { get; set; }
    }

    public class NhapKhoDto
    {
        public int MaVatTu { get; set; }
        public int SoLuong { get; set; }
        public int MaNhanVienGiaoDich { get; set; }
    }

    public class VatTuDto
    {
        public int MaVatTu { get; set; }
        public string TenVatTu { get; set; } = "";
        public string? DonViTinh { get; set; }
        public int SoLuongTonKho { get; set; }
        public decimal DonGia { get; set; }
    }

    public class TaoHoSoVatTuDto
    {
        public int? MaHoSoBaoTri { get; set; }
        public int? MaHoSoSuaChua { get; set; }
        public int? MaThietBi { get; set; }
        public string TenThietBi { get; set; } = "";
        public string LoaiCongViec { get; set; } = "";
        public DateTime? NgayThucHien { get; set; }
        public List<ChiTietSuDungInputDto> ChiTiet { get; set; } = new();
    }

    public class ChiTietSuDungInputDto
    {
        public int SoBuoc { get; set; }
        public string MoTaBuoc { get; set; } = "";
        public int? MaVatTu { get; set; }
        public string TenVatTu { get; set; } = "";
        public int SoLuong { get; set; }
        public decimal DonGia { get; set; }
    }

    public class HoSoVatTuResponseDto
    {
        public int MaHoSoVatTu { get; set; }
        public int? MaHoSoBaoTri { get; set; }
        public int? MaHoSoSuaChua { get; set; }
        public int? MaThietBi { get; set; }
        public string TenThietBi { get; set; } = "";
        public string LoaiCongViec { get; set; } = "";
        public DateTime NgayThucHien { get; set; }
        public string? TenNhanVien { get; set; }
        public decimal TongTien { get; set; }
        public string TrangThai { get; set; } = "";
        public DateTime? NgayGuiGiamDoc { get; set; }
        public List<ChiTietSuDungResponseDto> ChiTiet { get; set; } = new();
    }

    public class ChiTietSuDungResponseDto
    {
        public int SoBuoc { get; set; }
        public string MoTaBuoc { get; set; } = "";
        public int? MaVatTu { get; set; }
        public string TenVatTu { get; set; } = "";
        public int SoLuong { get; set; }
        public decimal DonGia { get; set; }
        public decimal ThanhTien { get; set; }
    }

    public class BuocQuyTrinhDto
    {
        public int SoBuoc { get; set; }
        public string MoTaBuoc { get; set; } = "";
    }
}
