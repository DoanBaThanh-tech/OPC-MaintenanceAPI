using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Models;

public partial class NhanVien
{
    public int MaNhanVien { get; set; }

    public int MaNguoiDung { get; set; }

    public string HoTen { get; set; } = null!;

    public string? Email { get; set; }

    public string? SoDienThoai { get; set; }

    public string? ChucVu { get; set; }

    public DateOnly? NgayVaoLam { get; set; }

    public string? TrangThai { get; set; }

    public DateTime NgayTao { get; set; }

    public virtual ICollection<HoSoBaoTri> HoSoBaoTriMaNhanVienDuyetNavigations { get; set; } = new List<HoSoBaoTri>();

    public virtual ICollection<HoSoBaoTri> HoSoBaoTriMaNhanVienTaoNavigations { get; set; } = new List<HoSoBaoTri>();

    public virtual ICollection<HoSoSuaChua> HoSoSuaChuaMaNhanVienDuyetNavigations { get; set; } = new List<HoSoSuaChua>();

    public virtual ICollection<HoSoSuaChua> HoSoSuaChuaMaNhanVienTaoNavigations { get; set; } = new List<HoSoSuaChua>();

    public virtual ICollection<HoSoYeuCauVatTu> HoSoYeuCauVatTuMaNhanVienDuyetNavigations { get; set; } = new List<HoSoYeuCauVatTu>();

    public virtual ICollection<HoSoYeuCauVatTu> HoSoYeuCauVatTuMaNhanVienTaoNavigations { get; set; } = new List<HoSoYeuCauVatTu>();

    public virtual ICollection<KeHoachBaoTri> KeHoachBaoTris { get; set; } = new List<KeHoachBaoTri>();

    public virtual ICollection<KetQuaThucHien> KetQuaThucHiens { get; set; } = new List<KetQuaThucHien>();

    public virtual ICollection<LichSuPheDuyet> LichSuPheDuyets { get; set; } = new List<LichSuPheDuyet>();

    public virtual QuanLyNguoiDung MaNguoiDungNavigation { get; set; } = null!;

    public virtual ICollection<NhapXuatVatTu> NhapXuatVatTus { get; set; } = new List<NhapXuatVatTu>();

    public virtual ICollection<NhatKyHeThong> NhatKyHeThongs { get; set; } = new List<NhatKyHeThong>();

    public virtual ICollection<PhanCongCongViec> PhanCongCongViecMaNhanVienPhanCongNavigations { get; set; } = new List<PhanCongCongViec>();

    public virtual ICollection<PhanCongCongViec> PhanCongCongViecMaNhanVienThucHienNavigations { get; set; } = new List<PhanCongCongViec>();
}
