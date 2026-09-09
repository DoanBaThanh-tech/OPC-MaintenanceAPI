using System;
using System.Collections.Generic;

namespace OPC.MaintenanceAPI.Core.Entities;

public partial class QuanLyNguoiDung
{
    public int MaNguoiDung { get; set; }

    public string Email { get; set; } = null!;

    public string MatKhau { get; set; } = null!;

    public int MaVaiTro { get; set; }

    public string TrangThai { get; set; } = "Chưa kích hoạt";

    public DateTime? LanDangNhapCuoi { get; set; }

    public DateTime NgayTao { get; set; }

    public virtual VaiTro MaVaiTroNavigation { get; set; } = null!;

    public virtual NhanVien? NhanVien { get; set; }
}
