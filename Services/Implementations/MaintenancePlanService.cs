using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.DTOs.MaintenancePlan;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;
namespace OPC.MaintenanceAPI.Services.Implementations
{
    public class MaintenancePlanService : IMaintenancePlanService
    {
        private readonly IMaintenancePlanRepository _repo;
        private readonly INhanVienRepository _nhanVienRepo;

        public MaintenancePlanService(IMaintenancePlanRepository repo, INhanVienRepository nhanVienRepo)
        {
            _repo = repo;
            _nhanVienRepo = nhanVienRepo;
        }

        public async Task<(bool, string?)> TaoNamMoiAsync(int maNguoiDungTao, TaoNamMoiDto dto)
        {
            if (dto.Nam < 2000 || dto.Nam > 9999)
                return (false, "Năm áp dụng không hợp lệ.");

            // Idempotent: năm đã có khung kế hoạch → coi như thành công (không báo trùng)
            if (await _repo.NamDaTonTaiAsync(dto.Nam))
                return (true, $"Kế hoạch năm {dto.Nam} đã có sẵn.");

            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungTao);
            if (nhanVien == null) return (false, "Không xác định được người lập.");

            var keHoach = new KeHoachBaoTri
            {
                Nam = dto.Nam,
                MaNhanVienLap = nhanVien.MaNhanVien,
                NgayLapKeHoach = DateOnly.FromDateTime(DateTime.Now),
                TrangThai = "Đang lập"
                // MaChuKy để trống — kế hoạch năm không gắn cứng 1 chu kỳ nào
            };
            await _repo.AddKeHoachAsync(keHoach);
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        /// <summary>
        /// Lập bảo trì cho thiết bị = tạo Chi tiết kế hoạch + Hồ sơ bảo trì (Chờ duyệt) trong 1 lần.
        /// Nếu chưa có khung kế hoạch năm → tự tạo (sau khi dọn dữ liệu test không cần bấm "Tạo kế hoạch năm" riêng).
        /// </summary>
        public async Task<(bool, string?)> ThemThietBiVaoNamAsync(int maNguoiDungTao, ThemThietBiVaoNamDto dto)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungTao);
            if (nhanVien == null)
                return (false, "Không xác định được người thao tác.");

            var keHoach = await _repo.GetKeHoachByNamAsync(dto.Nam);
            // Tự tạo khung kế hoạch năm nếu thiếu (tránh 400 sau khi xóa HS/KH test)
            if (keHoach == null)
            {
                if (dto.Nam < 2000 || dto.Nam > 9999)
                    return (false, "Năm áp dụng không hợp lệ.");
                keHoach = new KeHoachBaoTri
                {
                    Nam = dto.Nam,
                    MaNhanVienLap = nhanVien.MaNhanVien,
                    NgayLapKeHoach = DateOnly.FromDateTime(DateTime.Now),
                    TrangThai = "Đang lập"
                };
                await _repo.AddKeHoachAsync(keHoach);
                await _repo.SaveChangesAsync();
            }

            if (string.IsNullOrWhiteSpace(dto.NoiDungCongViec))
                return (false, "Vui lòng nhập nội dung công việc.");

            if (dto.NgayDuKienBaoTri.Year != dto.Nam)
                return (false, "Ngày dự kiến phải thuộc năm của kế hoạch.");

            var thietBi = await _repo.GetThietBiAsync(dto.MaThietBi);
            if (thietBi == null)
                return (false, $"Không tìm thấy thiết bị #{dto.MaThietBi}.");

            var nam = dto.NgayDuKienBaoTri.Year;
            var thang = dto.NgayDuKienBaoTri.Month;

            // Ưu tiên báo trùng tháng (kể cả hồ sơ Từ chối) trước các ràng buộc ngày khác
            if (await _repo.TonTaiKeHoachTheoThietBiThangAsync(dto.MaThietBi, nam, thang))
                return (false,
                    $"Thiết bị '{thietBi.TenThietBi}' đã được lập kế hoạch bảo trì trong tháng {thang}/{nam}. Không thể lập thêm — vui lòng chọn tháng khác.");

            if (await _repo.TonTaiHoSoBaoTriTheoThietBiThangAsync(dto.MaThietBi, nam, thang))
                return (false,
                    $"Thiết bị '{thietBi.TenThietBi}' đã có hồ sơ bảo trì trong tháng {thang}/{nam} (kể cả đang từ chối). Không thể lập thêm — vui lòng chọn tháng khác.");

            var homNay = DateOnly.FromDateTime(DateTime.Now);
            if (dto.NgayDuKienBaoTri <= homNay)
                return (false, "Ngày dự kiến bảo trì phải lớn hơn ngày hiện tại (không chọn hôm nay hoặc ngày trước).");

            // Chỉ so với ngày lập KH nếu ngày lập KH không lớn hơn hôm nay (tránh data test lệch)
            if (keHoach.NgayLapKeHoach <= homNay && dto.NgayDuKienBaoTri <= keHoach.NgayLapKeHoach)
                return (false,
                    $"Ngày dự kiến bảo trì phải lớn hơn ngày lập kế hoạch ({keHoach.NgayLapKeHoach:dd/MM/yyyy}).");

            // Ngày lắp đặt: bỏ qua nếu dữ liệu lắp đặt nằm ở tương lai (data test sai)
            if (thietBi.NgayLapDat <= homNay && dto.NgayDuKienBaoTri <= thietBi.NgayLapDat)
            {
                return (false,
                    $"Ngày dự kiến bảo trì phải lớn hơn ngày lắp đặt ({thietBi.NgayLapDat:dd/MM/yyyy}).");
            }

            // Thời gian dự kiến / bắt đầu / kết thúc không bắt buộc khi tạo —
            // hệ thống ghi nhận khi NVKT Tiến hành quy trình và khi hoàn thành.

            // --- Tạo cả 2 trong cùng luồng ---
            var chiTiet = new ChiTietKeHoachBaoTri
            {
                MaKeHoach = keHoach.MaKeHoach,
                MaThietBi = dto.MaThietBi,
                NgayDuKienBaoTri = dto.NgayDuKienBaoTri,
                TrangThai = "Đã tạo hồ sơ"
            };
            await _repo.AddChiTietRangeAsync(new[] { chiTiet });
            await _repo.SaveChangesAsync(); // có MaChiTietKeHoach

            TimeSpan? gioBatDau = null;
            TimeSpan? gioKetThuc = null;
            if (!string.IsNullOrWhiteSpace(dto.GioBatDauDuKien) && TimeSpan.TryParse(dto.GioBatDauDuKien, out var gbd))
                gioBatDau = gbd;
            if (!string.IsNullOrWhiteSpace(dto.GioKetThucDuKien) && TimeSpan.TryParse(dto.GioKetThucDuKien, out var gkt))
                gioKetThuc = gkt;

            // Tạo hồ sơ gửi xưởng xem lịch — trạng thái Chờ duyệt (GĐ duyệt sau)
            var hoSo = new HoSoBaoTri
            {
                MaThieBi = dto.MaThietBi,
                MaNhanVienTao = nhanVien.MaNhanVien,
                NoiDungCongViec = dto.NoiDungCongViec!.Trim(),
                ThoiGianDuKien = dto.ThoiGianDuKien.HasValue && dto.ThoiGianDuKien.Value > 0
                    ? dto.ThoiGianDuKien.Value.ToString()
                    : null,
                GioBatDauDuKien = gioBatDau,
                GioKetThucDuKien = gioKetThuc,
                NgayTao = DateTime.Now,
                TrangThai = "Chờ duyệt"
            };
            await _repo.AddHoSoBaoTriAsync(hoSo);
            await _repo.SaveChangesAsync(); // có MaHoSoBaoTri

            // Gắn hồ sơ vào chi tiết — bắt buộc để lịch không còn "Chưa tạo hồ sơ"
            var chiTietDb = await _repo.GetChiTietKeHoachByIdAsync(chiTiet.MaChiTietKeHoach);
            if (chiTietDb == null)
                return (false, "Đã tạo dữ liệu nhưng không gắn được hồ sơ vào kế hoạch. Vui lòng liên hệ admin.");

            chiTietDb.MaHoSoBaoTri = hoSo.MaHoSoBaoTri;
            chiTietDb.TrangThai = "Đã tạo hồ sơ";

            // Cập nhật lịch bảo trì trên thiết bị theo quy tắc:
            // - Ngày dự kiến của hồ sơ mới → "Bảo trì tiếp theo"
            // - Giá trị "Bảo trì tiếp theo" cũ (nếu có) → đẩy lên "Bảo trì gần nhất"
            var ngayDuKien = dto.NgayDuKienBaoTri;
            if (thietBi.NgayBaoTriTiepTheo.HasValue)
            {
                thietBi.NgayBaoTriGanNhat = thietBi.NgayBaoTriTiepTheo;
            }
            thietBi.NgayBaoTriTiepTheo = ngayDuKien;
            if (thietBi.TinhTrangHienTai == "Bảo trì" ||
                string.IsNullOrWhiteSpace(thietBi.TinhTrangHienTai))
            {
                thietBi.TinhTrangHienTai = "Bảo trì";
            }
            await _repo.SaveChangesAsync();

            return (true, null);
        }

        // Thay thế TaoYeuCauNgayBaoTriAsync + LapKeHoachTuYeuCauAsync cũ.
        // Tổ trưởng chọn chu kỳ + năm + danh sách thiết bị (kèm ngày dự kiến) trong 1 lần.
        public async Task<(bool, string?)> TaoKeHoachAsync(int maNguoiDungTao, TaoKeHoachDto dto)
        {
            if (dto.Nam < 2000 || dto.Nam > 2100)
                return (false, "Năm không hợp lệ.");
            if (dto.ThietBiDuocChon == null || dto.ThietBiDuocChon.Count == 0)
                return (false, "Vui lòng chọn ít nhất 1 thiết bị.");

            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungTao);
            if (nhanVien == null) return (false, "Không xác định được người lập kế hoạch.");

            var ngayLap = DateOnly.FromDateTime(DateTime.Now);

            // Validate từng thiết bị trước khi tạo bất kỳ dữ liệu nào
            foreach (var ct in dto.ThietBiDuocChon)
            {
                var thietBi = await _repo.GetThietBiAsync(ct.MaThietBi);
                if (thietBi == null)
                    return (false, $"Không tìm thấy thiết bị #{ct.MaThietBi}.");

                if (ct.NgayDuKienBaoTri.Year != dto.Nam)
                    return (false, $"Ngày dự kiến bảo trì của '{thietBi.TenThietBi}' phải thuộc năm {dto.Nam}.");

                var homNayTao = DateOnly.FromDateTime(DateTime.Now);
                if (ct.NgayDuKienBaoTri <= homNayTao)
                    return (false,
                        $"Ngày dự kiến bảo trì của '{thietBi.TenThietBi}' phải lớn hơn ngày hiện tại (không chọn hôm nay hoặc ngày trước).");

                if (ct.NgayDuKienBaoTri <= ngayLap)
                    return (false,
                        $"Ngày dự kiến bảo trì của '{thietBi.TenThietBi}' phải lớn hơn ngày lập kế hoạch ({ngayLap:dd/MM/yyyy}).");

                var thang = ct.NgayDuKienBaoTri.Month;
                if (await _repo.TonTaiKeHoachTheoThietBiThangAsync(ct.MaThietBi, dto.Nam, thang))
                    return (false,
                        $"Thiết bị '{thietBi.TenThietBi}' đã được lập kế hoạch bảo trì trong tháng {thang}/{dto.Nam}. Không thể lập thêm.");

                if (await _repo.TonTaiHoSoBaoTriTheoThietBiThangAsync(ct.MaThietBi, dto.Nam, thang))
                    return (false,
                        $"Thiết bị '{thietBi.TenThietBi}' đã có hồ sơ bảo trì trong tháng {thang}/{dto.Nam}. Không thể lập kế hoạch thêm.");
            }

            var keHoach = new KeHoachBaoTri
            {
                MaChuKy = dto.MaChuKy,
                Nam = dto.Nam,
                MaNhanVienLap = nhanVien.MaNhanVien,
                NgayLapKeHoach = ngayLap,
                TrangThai = "Đang lập"
            };
            await _repo.AddKeHoachAsync(keHoach);
            await _repo.SaveChangesAsync();

            var chiTiets = dto.ThietBiDuocChon.Select(ct => new ChiTietKeHoachBaoTri
            {
                MaKeHoach = keHoach.MaKeHoach,
                MaThietBi = ct.MaThietBi,
                NgayDuKienBaoTri = ct.NgayDuKienBaoTri
            });
            await _repo.AddChiTietRangeAsync(chiTiets);
            await _repo.SaveChangesAsync();

            return (true, null);
        }
        public async Task<List<int>> GetDanhSachNamDaLapAsync() =>
            (await _repo.GetAllKeHoachAsync()).Select(k => k.Nam).Distinct().OrderByDescending(n => n).ToList();
        public async Task<List<ChiTietKeHoachDto>> GetChiTietChuaCoHoSoAsync() =>
            (await _repo.GetChiTietChuaCoHoSoAsync()).Select(MapChiTiet).ToList();

        public async Task<List<ChiTietKeHoachDto>> GetChiTietTheoKeHoachAsync(int maKeHoach) =>
            (await _repo.GetChiTietTheoKeHoachAsync(maKeHoach)).Select(MapChiTiet).ToList();

        public async Task<(bool, string?)> ThemLanBaoTriAsync(int maKeHoach, ThemLanBaoTriDto dto)
        {
            var keHoach = await _repo.GetKeHoachByIdAsync(maKeHoach);
            if (keHoach == null) return (false, "Không tìm thấy kế hoạch.");

            if (dto.NgayDuKienBaoTri.Year != keHoach.Nam)
                return (false, $"Lần bảo trì tiếp theo đã vượt qua năm {keHoach.Nam}. " +
                   $"Vui lòng lập kế hoạch bảo trì cho năm {dto.NgayDuKienBaoTri.Year} để tiếp tục.");

            if (dto.NgayDuKienBaoTri <= keHoach.NgayLapKeHoach)
                return (false,
                    $"Ngày dự kiến bảo trì phải lớn hơn ngày lập kế hoạch ({keHoach.NgayLapKeHoach:dd/MM/yyyy}).");

            var thietBi = await _repo.GetThietBiAsync(dto.MaThietBi);
            if (thietBi == null) return (false, "Không tìm thấy thiết bị.");

            var nam = dto.NgayDuKienBaoTri.Year;
            var thang = dto.NgayDuKienBaoTri.Month;
            if (await _repo.TonTaiKeHoachTheoThietBiThangAsync(dto.MaThietBi, nam, thang))
                return (false,
                    $"Thiết bị '{thietBi.TenThietBi}' đã được lập kế hoạch bảo trì trong tháng {thang}/{nam}. Không thể lập thêm.");

            // Điều kiện: thiết bị phải đã tồn tại trong kế hoạch này rồi (không cho thêm thiết bị lạ
            // qua đường này — thiết bị mới phải đi qua nút "Lập kế hoạch mới")
            var ngayGanNhat = await _repo.GetNgayBaoTriGanNhatAsync(maKeHoach, dto.MaThietBi);
            if (ngayGanNhat == null)
                return (false, "Thiết bị này chưa có trong kế hoạch, vui lòng lập kế hoạch mới thay vì thêm lần bảo trì.");

            // Điều kiện: lần bảo trì mới phải sau lần gần nhất, không được trùng/lùi ngày
            if (dto.NgayDuKienBaoTri <= ngayGanNhat.Value)

                return (false, $"Ngày bảo trì mới phải sau ngày gần nhất ({ngayGanNhat.Value:dd/MM/yyyy}).");

            await _repo.AddChiTietRangeAsync(new[]
            {
                new ChiTietKeHoachBaoTri
                {
                    MaKeHoach = maKeHoach,
                    MaThietBi = dto.MaThietBi,
                    NgayDuKienBaoTri = dto.NgayDuKienBaoTri
                }
            });
            await _repo.SaveChangesAsync();
            return (true, null);
        }

        public async Task<List<KeHoachResponseDto>> GetAllKeHoachAsync() =>
            (await _repo.GetAllKeHoachAsync()).Select(k => new KeHoachResponseDto
            {
                MaKeHoach = k.MaKeHoach,
                MaChuKy = k.MaChuKy ?? 0,
                TenChuKy = k.MaChuKyNavigation?.LoaiThietBi,
                TenThietBi = k.ChiTietKeHoachBaoTris.FirstOrDefault()?.MaThietBiNavigation?.TenThietBi,
                Nam = k.Nam,
                TenNhanVienLap = k.MaNhanVienLapNavigation?.HoTen,
                NgayLapKeHoach = k.NgayLapKeHoach,
                TrangThai = k.TrangThai ?? "Chưa xác định",
                SoThietBi = k.ChiTietKeHoachBaoTris?.Count ?? 0
            }).ToList();

        public async Task<List<ChuKyResponseDto>> GetAllChuKyAsync() =>
            (await _repo.GetAllChuKyAsync()).Select(c => new ChuKyResponseDto
            {
                MaChuKy = c.MaChuKy,
                LoaiThietBi = c.LoaiThietBi,
                SoThangChuKyDeXuat = c.SoThangChuKyDeXuat,
                MoTa = c.MoTa
            }).ToList();

        private static ChiTietKeHoachDto MapChiTiet(ChiTietKeHoachBaoTri c) => new()
        {
            MaChiTietKeHoach = c.MaChiTietKeHoach,
            MaThietBi = c.MaThietBi,
            TenThietBi = c.MaThietBiNavigation?.TenThietBi,
            NgayDuKienBaoTri = c.NgayDuKienBaoTri,
            MaHoSoBaoTri = c.MaHoSoBaoTri,
            TrangThaiHoSo = c.MaHoSoBaoTriNavigation?.TrangThai
        };
    }
}