namespace OPC.MaintenanceAPI.DTOs.WorkOrder
{
    public class TaoHoSoBaoTriDto
    {
        public int MaThietBi { get; set; }
        public string? NoiDungCongViec { get; set; }
        public string? ThoiGianDuKien { get; set; }
        public bool GuiDuyet { get; set; }
        public int? MaChiTietKeHoach { get; set; }
        /// <summary>Yêu cầu bảo trì đã được xưởng (Tổ trưởng sản xuất) xác nhận.</summary>
        public int? MaYeuCauBaoTri { get; set; }
    }

    public class TaoHoSoSuaChuaDto
    {
        public int MaThietBi { get; set; }
        public int MaNhanVienTao { get; set; }
        public string MoTaHuHong { get; set; } = null!;
        public string? PhuongAnSuaChua { get; set; }
        public bool GuiDuyet { get; set; }
    }

    public class PhanCongDto
    {
        public int MaNhanVienThucHien { get; set; }
        public int MaNhanVienPhanCong { get; set; }
        public DateTime NgayBatDauDuKien { get; set; }
        public DateTime NgayKetThucDuKien { get; set; }
    }

    public class GhiNhanKetQuaDto
    {
        public int MaNhanVienGhiNhan { get; set; }
        public string? SoLieuGhiNhan { get; set; }
        public string? HinhAnh { get; set; }
        public string? GhiChu { get; set; }
        public DateTime? NgayGhiNhan { get; set; }
    }

    public class TuChoiNhanViecDto
    {
        public string LyDo { get; set; } = null!;
    }

    public class XacNhanDto
    {
        public bool Dat { get; set; }
    }

    public class CapNhatHoSoBaoTriDto
    {
        public string? NoiDungCongViec { get; set; }
        public string? ThoiGianDuKien { get; set; }
        public string? GioBatDauDuKien { get; set; }
        public string? GioKetThucDuKien { get; set; }
        public DateOnly? NgayDuKienBaoTri { get; set; }
    }


    public class TaoYeuCauBaoTriDto
    {
        public int MaThietBi { get; set; }
        public int ThangBaoTri { get; set; }
        public int NamBaoTri { get; set; }
        public DateOnly NgayBaoTri { get; set; }
        public decimal ThoiGianDuKien { get; set; }
        public TimeSpan GioBatDau { get; set; }
        public TimeSpan GioKetThuc { get; set; }
        public string? GhiChu { get; set; }
    }

    public class XacNhanYeuCauBaoTriDto
    {
        /// <summary>Xác nhận | Từ chối</summary>
        public string QuyetDinh { get; set; } = null!;
        public string? LyDo { get; set; }
    }

    /// <summary>Tổ trưởng cơ điện sửa yêu cầu bị xưởng từ chối rồi gửi lại.</summary>
    public class SuaYeuCauBaoTriDto
    {
        public int MaThietBi { get; set; }
        public int ThangBaoTri { get; set; }
        public int NamBaoTri { get; set; }
        public DateOnly NgayBaoTri { get; set; }
        public decimal ThoiGianDuKien { get; set; }
        public TimeSpan GioBatDau { get; set; }
        public TimeSpan GioKetThuc { get; set; }
        public string? GhiChu { get; set; }
    }
}
