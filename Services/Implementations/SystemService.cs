using OPC.MaintenanceAPI.Core.Exceptions;
using OPC.MaintenanceAPI.DTOs.System;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;
 
namespace OPC.MaintenanceAPI.Services.Implementations
{
    public class SystemService : ISystemService
    {
        private readonly ISystemRepository _repo;
        public SystemService(ISystemRepository repo) => _repo = repo;
 
        // ---------- VAI TRÒ ----------
        private static readonly HashSet<string> VaiTroHienThi = new(StringComparer.OrdinalIgnoreCase)
        {
            "Tổ trưởng cơ điện", "Xưởng", "Giám đốc", "Nhân viên kỹ thuật"
        };

        public async Task<List<object>> GetAllVaiTroAsync()
        {
            var data = await _repo.GetAllVaiTroWithUserCountAsync();
            return data
                .Where(x => VaiTroHienThi.Contains(x.VaiTro.TenVaiTro ?? ""))
                .GroupBy(x => x.VaiTro.TenVaiTro, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.SoNguoiDung).First())
                .OrderBy(x => x.VaiTro.TenVaiTro)
                .Select(x => (object)new
                {
                    x.VaiTro.MaVaiTro,
                    x.VaiTro.TenVaiTro,
                    x.VaiTro.CapDoQuyen,
                    SoNguoiDung = x.SoNguoiDung
                }).ToList();
        }

        public async Task<List<object>> GetDanhSachNhanVienAsync(string? vaiTro = null)
        {
            return await _repo.GetDanhSachNhanVienAsync(vaiTro);
        }

        public async Task<VaiTro> TaoVaiTroAsync(VaiTroDto dto)
        {
            var vaiTro = new VaiTro { TenVaiTro = dto.TenVaiTro, CapDoQuyen = dto.CapDoQuyen };
            await _repo.AddAsync(vaiTro);
            await _repo.SaveChangesAsync();
            return vaiTro;
        }
 
        public async Task<VaiTro> CapNhatVaiTroAsync(int maVaiTro, VaiTroDto dto)
        {
            var vaiTro = await _repo.GetByIdAsync(maVaiTro)
                ?? throw new NotFoundException($"Không tìm thấy vai trò #{maVaiTro}");
 
            vaiTro.TenVaiTro = dto.TenVaiTro;
            vaiTro.CapDoQuyen = dto.CapDoQuyen;
            _repo.Update(vaiTro);
            await _repo.SaveChangesAsync();
            return vaiTro;
        }

        public async Task XoaVaiTroAsync(int maVaiTro)
        {
            var vaiTro = await _repo.GetByIdAsync(maVaiTro)
                ?? throw new NotFoundException($"Không tìm thấy vai trò #{maVaiTro}");
 
            // Quy tắc nghiệp vụ: không cho xoá vai trò đang có người dùng
            if (await _repo.VaiTroDangCoNguoiDungAsync(maVaiTro))
                throw new BusinessRuleException("Không thể xoá vai trò đang có người dùng sử dụng.");
 
            _repo.Delete(vaiTro);
            await _repo.SaveChangesAsync();
        }
 
        // ---------- PHÂN QUYỀN ----------
        public async Task<List<ChucNangQuyenDto>> GetMaTranPhanQuyenAsync(int maVaiTro)
        {
            var tatCaChucNang = await _repo.GetChucNangGroupedAsync();
            var quyenHienCo = await _repo.GetPhanQuyenByVaiTroAsync(maVaiTro);
 
            // Ghép 2 danh sách: mọi chức năng đều hiện ra, đã cấp quyền thì đánh dấu true
            return tatCaChucNang.Select(cn =>
            {
                var quyen = quyenHienCo.FirstOrDefault(q => q.MaChucNang == cn.MaChucNang);
                return new ChucNangQuyenDto
                {
                    MaChucNang = cn.MaChucNang,
                    TenChucNang = cn.TenChucNang ?? "",
                    NhomChucNang = cn.NhomChucNang ?? "",
                    DuocXem = quyen?.DuocXem ?? false,
                    DuocTao = quyen?.DuocTao ?? false,
                    DuocSua = quyen?.DuocSua ?? false,
                    DuocDuyet = quyen?.DuocDuyet ?? false,
                };
            }).ToList();
        }
 
        public async Task LuuPhanQuyenAsync(int maVaiTro, CapNhatPhanQuyenDto dto)
        {
            if (await _repo.GetByIdAsync(maVaiTro) == null)
                throw new NotFoundException($"Không tìm thấy vai trò #{maVaiTro}");
 
            // Xoá hết quyền cũ rồi ghi lại toàn bộ - đơn giản, tránh so sánh từng dòng thay đổi gì
            await _repo.XoaPhanQuyenTheoVaiTroAsync(maVaiTro);
 
            var danhSachMoi = dto.DanhSachQuyen
                .Where(q => q.DuocXem || q.DuocTao || q.DuocSua || q.DuocDuyet) // chỉ lưu dòng có ít nhất 1 quyền bật
                .Select(q => new PhanQuyenVaiTro
                {
                    MaVaiTro = maVaiTro,
                    MaChucNang = q.MaChucNang,
                    DuocXem = q.DuocXem,
                    DuocTao = q.DuocTao,
                    DuocSua = q.DuocSua,
                    DuocDuyet = q.DuocDuyet
                }).ToList();
 
            await _repo.ThemDanhSachPhanQuyenAsync(danhSachMoi);
            await _repo.SaveChangesAsync();
        }
 
        // ---------- NHẬT KÝ ----------
        public async Task<List<object>> TimNhatKyAsync(NhatKyFilterDto filter)
        {
            var list = await _repo.GetNhatKyAsync(
                filter.TuKhoa, filter.PhuongThucHTTP, filter.TuNgay, filter.DenNgay);

            // Giới hạn 300 bản ghi gần nhất để tải nhanh trên mobile
            return list.Take(400).Select(n => (object)new
            {
                n.MaNhatKy,
                n.MaNhanVien,
                TenNhanVien = n.MaNhanVienNavigation?.HoTen ?? $"NV#{n.MaNhanVien}",
                n.TenApi,
                n.PhuongThucHttp,
                n.ThoiGianTruyCap,
                n.DiaChiIp,
                n.StatusCode,
                n.QueryString,
                LoaiHanhDong = n.LoaiHanhDong ?? MapLoai(n.PhuongThucHttp),
                ChiTiet = n.ChiTiet,
                MoTa = !string.IsNullOrWhiteSpace(n.ChiTiet)
                    ? n.ChiTiet!
                    : MoTaHanhDongApi(n.PhuongThucHttp, n.TenApi)
            }).ToList();
        }

        private static string MapLoai(string? method) => (method ?? "").ToUpperInvariant() switch
        {
            "GET" => "Read",
            "POST" => "Create",
            "PUT" or "PATCH" => "Update",
            "DELETE" => "Delete",
            _ => method ?? "Other"
        };

        /// <summary>Mô tả ngắn: người dùng gọi API gì / chỉnh sửa gì.</summary>
        private static string MoTaHanhDongApi(string? method, string? path)
        {
            var m = (method ?? "").ToUpperInvariant();
            var p = (path ?? "").ToLowerInvariant();
            var hanhDong = m switch
            {
                "GET" => "Xem",
                "POST" => "Tạo / gửi",
                "PUT" or "PATCH" => "Cập nhật",
                "DELETE" => "Xóa",
                _ => string.IsNullOrEmpty(m) ? "Gọi API" : m
            };

            string doiTuong;
            string fields;
            if (p.Contains("bao-tri"))
            {
                doiTuong = "hồ sơ bảo trì";
                fields = m == "GET"
                    ? "Đọc: mã HS, thiết bị, nội dung, trạng thái, ngày, phân công…"
                    : "Ghi: nội dung CV, ngày dự kiến, trạng thái, bước quy trình…";
            }
            else if (p.Contains("sua-chua"))
            {
                doiTuong = "hồ sơ sửa chữa";
                fields = m == "GET"
                    ? "Đọc: mã HS, thiết bị, mô tả hư hỏng, trạng thái…"
                    : "Ghi: mô tả hư hỏng, phương án, trạng thái, bước quy trình…";
            }
            else if (p.Contains("vat-tu") || p.Contains("inventory"))
            {
                doiTuong = "vật tư / hồ sơ vật tư";
                fields = m == "GET"
                    ? "Đọc: mã VT, tên, tồn kho, đơn giá, chi tiết HS…"
                    : "Ghi: số lượng, đơn giá, chi tiết sử dụng…";
            }
            else if (p.Contains("phan-cong"))
            {
                doiTuong = "phân công";
                fields = "Ghi: danh sách NV thực hiện, trạng thái PC…";
            }
            else if (p.Contains("tien-do") || p.Contains("quy-trinh") || p.Contains("ke-hoach-buoc"))
            {
                doiTuong = "quy trình / bước";
                fields = m == "GET"
                    ? "Đọc: số bước, mô tả, trạng thái, NV, JSON vật tư…"
                    : "Ghi: trạng thái bước, vật tư bước, người thực hiện…";
            }
            else if (p.Contains("ke-hoach") || p.Contains("maintenanceplan"))
            {
                doiTuong = "kế hoạch bảo trì";
                fields = m == "GET"
                    ? "Đọc: năm, tháng, thiết bị, ngày dự kiến…"
                    : "Ghi: ngày dự kiến, trạng thái chi tiết KH…";
            }
            else if (p.Contains("thiet-bi") || p.Contains("equipment"))
            {
                doiTuong = "thiết bị";
                fields = m == "GET"
                    ? "Đọc: tên, danh mục, tình trạng, chu kỳ…"
                    : "Ghi: tình trạng, ngày bảo trì…";
            }
            else if (p.Contains("/toi") || p.Contains("auth"))
            {
                doiTuong = "tài khoản / hồ sơ cá nhân";
                fields = m == "GET"
                    ? "Đọc: email, họ tên, vai trò, SĐT…"
                    : "Ghi: họ tên, SĐT, ngày vào làm / vai trò (admin)…";
            }
            else if (p.Contains("nhatky") || p.Contains("system"))
            {
                doiTuong = "hệ thống";
                fields = "Đọc nhật ký / cấu hình";
            }
            else
            {
                doiTuong = string.IsNullOrEmpty(path) ? "API khác" : path!;
                fields = m == "GET" ? "Đọc dữ liệu" : "Ghi / cập nhật dữ liệu";
            }

            return $"{hanhDong} · {doiTuong} — {fields}";
        }
    }
}