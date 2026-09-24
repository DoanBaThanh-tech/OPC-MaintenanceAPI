using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class HoSoSuDungVatTu
{
    public int MaHoSoVatTu { get; set; }
    public int? MaHoSoBaoTri { get; set; }
    public int? MaHoSoSuaChua { get; set; }
    public int? MaThietBi { get; set; }
    public string TenThietBi { get; set; } = null!;
    public string LoaiCongViec { get; set; } = null!;
    public DateTime NgayThucHien { get; set; }
    public int MaNhanVienTH { get; set; }
    public decimal TongTien { get; set; }
    public string TrangThai { get; set; } = "Chờ gửi";
    public DateTime? NgayGuiGiamDoc { get; set; }
    public string? GhiChu { get; set; }
    public DateTime NgayTao { get; set; }

    public virtual HoSoBaoTri? MaHoSoBaoTriNavigation { get; set; }
    public virtual HoSoSuaChua? MaHoSoSuaChuaNavigation { get; set; }
    public virtual ThietBi? MaThietBiNavigation { get; set; }
    public virtual NhanVien? MaNhanVienTHNavigation { get; set; }
    public virtual ICollection<ChiTietSuDungVatTu> ChiTietSuDungVatTus { get; set; } = new List<ChiTietSuDungVatTu>();
}

public partial class ChiTietSuDungVatTu
{
    public int MaChiTiet { get; set; }
    public int MaHoSoVatTu { get; set; }
    public int SoBuoc { get; set; }
    public string MoTaBuoc { get; set; } = null!;
    public int? MaVatTu { get; set; }
    public string TenVatTu { get; set; } = null!;
    public int SoLuong { get; set; }
    public decimal DonGia { get; set; }

    public virtual HoSoSuDungVatTu MaHoSoVatTuNavigation { get; set; } = null!;
    public virtual VatTu? MaVatTuNavigation { get; set; }
}
