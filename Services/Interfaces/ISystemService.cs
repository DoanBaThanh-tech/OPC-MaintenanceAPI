using OPC.MaintenanceAPI.DTOs.System;
using OPC.MaintenanceAPI.Core.Entities;

namespace OPC.MaintenanceAPI.Services.Interfaces
{
    public interface ISystemService
    {
        // Vai trò
        Task<List<object>> GetAllVaiTroAsync();
        Task<VaiTro> TaoVaiTroAsync(VaiTroDto dto);
        Task<VaiTro> CapNhatVaiTroAsync(int maVaiTro, VaiTroDto dto);
        Task XoaVaiTroAsync(int maVaiTro);

        // Phân quyền
        Task<List<ChucNangQuyenDto>> GetMaTranPhanQuyenAsync(int maVaiTro);
        Task LuuPhanQuyenAsync(int maVaiTro, CapNhatPhanQuyenDto dto);

        // Nhật ký hệ thống
        Task<List<NhatKyHeThong>> TimNhatKyAsync(NhatKyFilterDto filter);
    }
}