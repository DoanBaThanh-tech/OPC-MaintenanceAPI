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

            // Bỏ DELETE; giới hạn bản ghi gần nhất để tải nhanh trên mobile
            return list
                .Where(n =>
                {
                    var m = (n.PhuongThucHttp ?? "").ToUpperInvariant();
                    return m is "GET" or "POST" or "PUT" or "PATCH";
                })
                .Take(400)
                .Select(n =>
                {
                    var method = ChuanHoaMethod(n.PhuongThucHttp);
                    var tenChucNang = MapTenChucNang(n.TenApi);
                    return (object)new
                    {
                        n.MaNhatKy,
                        n.MaNhanVien,
                        TenNhanVien = n.MaNhanVienNavigation?.HoTen ?? $"NV#{n.MaNhanVien}",
                        n.TenApi,
                        PhuongThucHttp = method,
                        n.ThoiGianTruyCap,
                        n.DiaChiIp,
                        n.StatusCode,
                        n.QueryString,
                        // Chỉ dùng GET / POST / PUT — không CRUD
                        LoaiHanhDong = method,
                        TenChucNang = tenChucNang,
                        ChiTiet = n.ChiTiet,
                        MoTa = !string.IsNullOrWhiteSpace(n.ChiTiet)
                            ? n.ChiTiet!
                            : MoTaHanhDongApi(method, n.TenApi, tenChucNang)
                    };
                }).ToList();
        }

        private static string ChuanHoaMethod(string? method)
        {
            var m = (method ?? "").ToUpperInvariant();
            return m switch
            {
                "PATCH" => "PUT",
                "GET" or "POST" or "PUT" => m,
                _ => m
            };
        }

        /// <summary>Ánh xạ path API → tên chức năng nghiệp vụ (tiện admin tìm lỗi).</summary>
        private static string MapTenChucNang(string? path)
        {
            var p = (path ?? "").ToLowerInvariant();
            if (p.Contains("phan-cong")) return "Phân công nhân viên";
            if (p.Contains("tien-do") || p.Contains("quy-trinh") || p.Contains("ke-hoach-buoc"))
                return "Quy trình / bước thực hiện";
            if (p.Contains("bao-tri") && p.Contains("xac-nhan")) return "Xác nhận kết quả bảo trì";
            if (p.Contains("sua-chua") && p.Contains("xac-nhan")) return "Xác nhận kết quả sửa chữa";
            if (p.Contains("bao-tri")) return "Hồ sơ bảo trì";
            if (p.Contains("sua-chua")) return "Hồ sơ sửa chữa";
            if (p.Contains("ho-so-vat-tu") || (p.Contains("vat-tu") && p.Contains("ho-so")))
                return "Hồ sơ vật tư";
            if (p.Contains("vat-tu") || p.Contains("inventory")) return "Vật tư / tồn kho";
            if (p.Contains("ke-hoach") || p.Contains("maintenanceplan")) return "Kế hoạch bảo trì";
            if (p.Contains("thiet-bi") || p.Contains("equipment")) return "Thiết bị";
            if (p.Contains("phe-duyet") || p.Contains("duyet") || p.Contains("approval"))
                return "Phê duyệt";
            if (p.Contains("nguoi-dung") || p.Contains("quanlynguoidung") || p.Contains("user"))
                return "Quản lý người dùng";
            if (p.Contains("thong-ke") || p.Contains("dashboard")) return "Thống kê";
            if (p.Contains("/toi") || p.Contains("auth") || p.Contains("dang-nhap") || p.Contains("profile"))
                return "Tài khoản / hồ sơ cá nhân";
            if (p.Contains("nhatky") || p.Contains("system")) return "Hệ thống / nhật ký";
            if (p.Contains("workorder") || p.Contains("work-order")) return "Công việc / hồ sơ";
            return "API khác";
        }

        /// <summary>Mô tả ngắn: chức năng + method + người dùng làm gì.</summary>
        private static string MoTaHanhDongApi(string? method, string? path, string tenChucNang)
        {
            var m = ChuanHoaMethod(method);
            var p = (path ?? "").ToLowerInvariant();
            var hanhDong = m switch
            {
                "GET" => "Xem / lấy dữ liệu",
                "POST" => "Tạo mới / gửi xử lý",
                "PUT" => "Cập nhật / chỉnh sửa",
                _ => "Gọi API"
            };

            string fields;
            if (p.Contains("bao-tri"))
            {
                fields = m == "GET"
                    ? "mã HS, thiết bị, nội dung, trạng thái, ngày, phân công…"
                    : "nội dung CV, ngày dự kiến, trạng thái, bước quy trình…";
            }
            else if (p.Contains("sua-chua"))
            {
                fields = m == "GET"
                    ? "mã HS, thiết bị, mô tả hư hỏng, trạng thái…"
                    : "mô tả hư hỏng, phương án, trạng thái, bước quy trình…";
            }
            else if (p.Contains("vat-tu") || p.Contains("inventory"))
            {
                fields = m == "GET"
                    ? "mã VT, tên, tồn kho, đơn giá, chi tiết HS…"
                    : "số lượng, đơn giá, chi tiết sử dụng…";
            }
            else if (p.Contains("phan-cong"))
            {
                fields = "danh sách NV thực hiện, trạng thái phân công…";
            }
            else if (p.Contains("tien-do") || p.Contains("quy-trinh") || p.Contains("ke-hoach-buoc"))
            {
                fields = m == "GET"
                    ? "số bước, mô tả, trạng thái, NV, vật tư…"
                    : "trạng thái bước, vật tư bước, người thực hiện…";
            }
            else if (p.Contains("ke-hoach") || p.Contains("maintenanceplan"))
            {
                fields = m == "GET"
                    ? "năm, tháng, thiết bị, ngày dự kiến…"
                    : "ngày dự kiến, trạng thái chi tiết KH…";
            }
            else if (p.Contains("thiet-bi") || p.Contains("equipment"))
            {
                fields = m == "GET"
                    ? "tên, danh mục, tình trạng, chu kỳ…"
                    : "tình trạng, ngày bảo trì…";
            }
            else if (p.Contains("/toi") || p.Contains("auth"))
            {
                fields = m == "GET"
                    ? "email, họ tên, vai trò, SĐT…"
                    : "họ tên, SĐT, vai trò…";
            }
            else
            {
                fields = m == "GET" ? "đọc dữ liệu" : "ghi / cập nhật dữ liệu";
            }

            return $"{tenChucNang} · {hanhDong} — {fields}";
        }
    }
}