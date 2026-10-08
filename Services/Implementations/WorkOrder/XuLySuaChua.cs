using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.Core.Exceptions;
using OPC.MaintenanceAPI.DTOs.WorkOrder;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;
using OPC.MaintenanceAPI.DTOs.Common;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class WorkOrderService
    {

        // ---------- từ XuLySuaChua.cs ----------

        // ===== SỬA CHỮA =====


        public async Task<(bool, string?)> TaoHoSoSuaChuaAsync(int maNguoiDungTao, TaoHoSoSuaChuaDto dto)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungTao);
            if (nhanVien == null) return (false, "Không xác định được người tạo hồ sơ.");

            if (string.IsNullOrWhiteSpace(dto.MoTaHuHong))
                return (false, "Vui lòng mô tả hư hỏng thiết bị.");

            var tb = await _repo.GetThietBiByIdAsync(dto.MaThietBi);
            if (tb == null) return (false, "Không tìm thấy thiết bị.");

            // Đang bảo trì / sửa chữa → không tạo SC (và ngược lại với BT)
            var ttTb = OPC.MaintenanceAPI.DTOs.Equipment.TrangThaiThietBiConst.ChuanHoa(tb.TinhTrangHienTai);
            if (ttTb == "Bảo trì" || await _repo.CoHoSoBaoTriDangMoAsync(dto.MaThietBi))
                return (false, "Thiết bị đang bảo trì — không thể tạo hồ sơ sửa chữa. Hoàn tất bảo trì trước.");
            if (ttTb == "Sửa chữa" || await _repo.CoHoSoSuaChuaDangMoAsync(dto.MaThietBi))
                return (false, "Thiết bị đã có hồ sơ sửa chữa chưa hoàn thành — không thể tạo thêm.");

            // Đã có BT hoặc SC trong tháng hiện tại → không tạo thêm SC
            var nowSc = DateTime.Now;
            if (await _repo.TonTaiHoSoBtHoacScTrongThangAsync(dto.MaThietBi, nowSc.Year, nowSc.Month))
                return (false,
                    $"Thiết bị này đã có hồ sơ bảo trì hoặc sửa chữa trong tháng {nowSc.Month}/{nowSc.Year}. Không thể tạo thêm hồ sơ sửa chữa.");

            // Gửi Tổ trưởng phân công (không qua GĐ)
            var trangThai = dto.GuiDuyet ? "Chờ phân công" : "Nháp";

            // Thời gian dự kiến (giờ/phút) + giờ bắt đầu/kết thúc — cùng ngày, không tràn
            TimeSpan? gbd = null;
            TimeSpan? gkt = null;
            if (!string.IsNullOrWhiteSpace(dto.GioBatDauDuKien) &&
                TimeSpan.TryParse(dto.GioBatDauDuKien, out var parsedGbd))
                gbd = parsedGbd;
            if (!string.IsNullOrWhiteSpace(dto.GioKetThucDuKien) &&
                TimeSpan.TryParse(dto.GioKetThucDuKien, out var parsedGkt))
                gkt = parsedGkt;
            if (gbd != null && gkt != null && gkt <= gbd)
                return (false, "Giờ kết thúc phải sau giờ bắt đầu (trong cùng ngày).");

            var hoSo = new HoSoSuaChua
            {
                MaThieBi = dto.MaThietBi,
                MaNhanVienTao = nhanVien.MaNhanVien,
                MoTaHuHong = dto.MoTaHuHong.Trim(),
                PhuongAnSuaChua = string.IsNullOrWhiteSpace(dto.PhuongAnSuaChua)
                    ? null
                    : dto.PhuongAnSuaChua.Trim(),
                ThoiGianDuKien = string.IsNullOrWhiteSpace(dto.ThoiGianDuKien)
                    ? null
                    : dto.ThoiGianDuKien.Trim(),
                GioBatDauDuKien = gbd,
                GioKetThucDuKien = gkt,
                NgayTao = DateTime.Now,
                TrangThai = trangThai
            };
            await _repo.AddHoSoSuaChuaAsync(hoSo);

            // Tạo hồ sơ SC → thiết bị Sản xuất → Sửa chữa ngay (kể cả nháp đã gửi)
            tb.TinhTrangHienTai = "Sửa chữa";
            // Lịch sửa chữa trên thiết bị: lần đầu → gần nhất; lần sau → tiếp theo
            var ngaySc = DateOnly.FromDateTime(hoSo.NgayTao);
            if (tb.NgayBaoTriGanNhat == null)
                tb.NgayBaoTriGanNhat = ngaySc;
            else
                tb.NgayBaoTriTiepTheo = ngaySc;

            await _repo.SaveChangesAsync();
            await LuuBuocDuocChonKhiTaoAsync(null, hoSo.MaHoSoSuaChua, dto.DanhSachBuoc);
            return (true, null);
        }

        public async Task<List<object>> GetHoSoSuaChuaTheoTrangThaiAsync(string? trangThai)
        {
            var list = await _repo.GetHoSoSuaChuaByTrangThaiAsync(trangThai);
            var ketQua = new List<object>();
            foreach (var h in list)
            {
                var dsPc = await _repo.GetPhanCongTheoHoSoSuaChuaAsync(h.MaHoSoSuaChua);
                var ttPc = dsPc
                    .Where(p => p.TrangThai is not "Đã hủy")
                    .OrderByDescending(p => p.TrangThai == "Chờ xác nhận" ? 2
                        : p.TrangThai is "Hoàn thành" ? 0 : 1)
                    .ThenByDescending(p => p.MaPhanCong)
                    .Select(p => p.TrangThai)
                    .FirstOrDefault();

                ketQua.Add(new
                {
                    h.MaHoSoSuaChua,
                    MaThietBi = h.MaThieBi,
                    TenThietBi = h.MaThieBiNavigation?.TenThietBi,
                    h.MoTaHuHong,
                    h.PhuongAnSuaChua,
                    h.TrangThai,
                    h.NgayTao,
                    TenNhanVienTao = h.MaNhanVienTaoNavigation?.HoTen,
                    h.MaPhanCong,
                    TrangThaiPhanCong = ttPc,
                    ChoXuongXacNhanQuyTrinh = (h.TrangThai is "Đang thực hiện" or "Chờ xác nhận")
                        && dsPc.Any(p => p.TrangThai == "Chờ xác nhận")
                });
            }
            return ketQua;
        }

        public async Task<object?> GetChiTietHoSoSuaChuaAsync(int id)
        {
            var h = await _repo.GetHoSoSuaChuaByIdAsync(id);
            if (h == null) return null;

            var dsPc = await _repo.GetPhanCongTheoHoSoSuaChuaAsync(h.MaHoSoSuaChua);
            var dangPc = dsPc
                .Where(p => p.TrangThai is "Đã phân công" or "Chờ xác nhận" or "Xác nhận" or "Đang thực hiện" or "Hoàn thành" or "Từ chối")
                .Select(p => new
                {
                    MaNhanVien = p.MaNhanVienThucHien,
                    TenNhanVien = p.MaNhanVienThucHienNavigation?.HoTen,
                    p.TrangThai,
                    p.MaPhanCong
                })
                .ToList();

            // Ưu tiên PC đang chờ Xưởng xác nhận quy trình (để hiện nút Xác nhận / Từ chối)
            var pcChoXn = dsPc.FirstOrDefault(p => p.TrangThai == "Chờ xác nhận");
            var pcChinh = pcChoXn
                ?? (h.MaPhanCong != null
                    ? dsPc.FirstOrDefault(p => p.MaPhanCong == h.MaPhanCong)
                    : null)
                ?? dsPc.OrderByDescending(p => p.MaPhanCong).FirstOrDefault();

            var choXuongXn = (h.TrangThai is "Đang thực hiện" or "Chờ xác nhận")
                && dsPc.Any(p => p.TrangThai == "Chờ xác nhận");

            // Quy trình đã chọn lúc tạo (kèm mô tả đã sửa)
            var dsBuoc = await _db.TienDoBuocQuyTrinhs
                .Where(x => x.MaHoSoSuaChua == h.MaHoSoSuaChua)
                .OrderBy(x => x.SoBuoc)
                .Select(x => new
                {
                    x.SoBuoc,
                    x.MoTaBuoc,
                    x.TrangThai,
                    x.TenNhanVien
                })
                .ToListAsync();

            return new
            {
                h.MaHoSoSuaChua,
                MaThietBi = h.MaThieBi,
                TenThietBi = h.MaThieBiNavigation?.TenThietBi,
                TenNhanVienTao = h.MaNhanVienTaoNavigation?.HoTen,
                h.MoTaHuHong,
                h.PhuongAnSuaChua,
                h.ThoiGianDuKien,
                GioBatDauDuKien = h.GioBatDauDuKien == null
                    ? null
                    : $"{(int)h.GioBatDauDuKien.Value.TotalHours:D2}:{h.GioBatDauDuKien.Value.Minutes:D2}",
                GioKetThucDuKien = h.GioKetThucDuKien == null
                    ? null
                    : $"{(int)h.GioKetThucDuKien.Value.TotalHours:D2}:{h.GioKetThucDuKien.Value.Minutes:D2}",
                ThoiDiemBatDauThucTe = h.ThoiDiemBatDauThucTe,
                ThoiDiemKetThucThucTe = h.ThoiDiemKetThucThucTe,
                h.TrangThai,
                h.LyDoTuChoi,
                h.NgayTao,
                h.NgayDuyet,
                h.MaPhanCong,
                TrangThaiPhanCong = pcChinh?.TrangThai,
                LyDoTuChoiPhanCong = pcChinh?.LyDoTuChoi,
                ChoXuongXacNhanQuyTrinh = choXuongXn,
                DanhSachNhanVienPhanCong = dangPc,
                MaNhanVienThucHiens = dangPc.Select(x => x.MaNhanVien).ToList(),
                TenNhanVienThucHiens = string.Join(", ", dangPc.Select(x => x.TenNhanVien).Where(t => !string.IsNullOrEmpty(t))),
                DanhSachBuocQuyTrinh = dsBuoc,
                RowVersion = Convert.ToBase64String(h.RowVersion)
            };
        }

        // Luồng 11 — cũng áp dụng Optimistic Concurrency Control
        public async Task<(bool, string?)> DuyetHoSoSuaChuaAsync(int id, int maNguoiDungDuyet, DuyetHoSoDto dto)
        {
            var nhanVienDuyet = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungDuyet);
            if (nhanVienDuyet == null) return (false, "Không xác định được người duyệt.");

            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(id);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai != "Chờ duyệt")
                return (false, "Hồ sơ đã được xử lý trước đó.");
            if (dto.QuyetDinh != "Duyệt" && dto.QuyetDinh != "Từ chối")
                return (false, "QuyetDinh chỉ nhận 'Duyệt' hoặc 'Từ chối'.");
            if (dto.QuyetDinh == "Từ chối" && string.IsNullOrWhiteSpace(dto.LyDo))
                return (false, "Vui lòng nhập lý do từ chối.");

            _repo.SetHoSoSuaChuaRowVersion(hoSo, Convert.FromBase64String(dto.RowVersion));

            hoSo.MaNhanVienDuyet = nhanVienDuyet.MaNhanVien;
            hoSo.NgayDuyet = DateTime.Now;
            hoSo.TrangThai = dto.QuyetDinh == "Duyệt" ? "Đã duyệt" : "Từ chối";
            hoSo.LyDoTuChoi = dto.QuyetDinh == "Từ chối" ? dto.LyDo : null;

            try
            {
                await _repo.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return (false, "Hồ sơ này vừa được người khác xử lý trước bạn. Vui lòng tải lại.");
            }

            await _repo.AddLichSuPheDuyetAsync(new LichSuPheDuyet
            {
                MaHoSoSuaChua = hoSo.MaHoSoSuaChua,
                MaNhanVienDuyet = nhanVienDuyet.MaNhanVien,
                QuyetDinh = dto.QuyetDinh,
                LyDo = dto.LyDo,
                NgayDuyet = DateTime.Now
            });
            await _repo.SaveChangesAsync();

            return (true, null);
        }

        public async Task<(bool, string?)> PhanCongSuaChuaAsync(int maHoSo, int maNguoiDungPhanCong, PhanCongDto dto)
        {
            var nhanVienPhanCong = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDungPhanCong);
            if (nhanVienPhanCong == null) return (false, "Không xác định được người phân công.");

            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai is not ("Chờ phân công" or "Đã duyệt" or "Đang thực hiện"))
                return (false, "Chỉ phân công khi hồ sơ chờ phân công / đã duyệt / đang thực hiện.");

            // NVKT đã tiến hành quy trình → không được đổi / cập nhật phân công
            if (hoSo.ThoiDiemBatDauThucTe != null)
                return (false,
                    "Nhân viên kỹ thuật đã tiến hành quy trình — không được cập nhật phân công.");

            var dsNv = new List<int>();
            if (dto.MaNhanVienThucHiens != null && dto.MaNhanVienThucHiens.Count > 0)
                dsNv.AddRange(dto.MaNhanVienThucHiens.Distinct());
            else if (dto.MaNhanVienThucHien.HasValue && dto.MaNhanVienThucHien.Value > 0)
                dsNv.Add(dto.MaNhanVienThucHien.Value);
            if (dsNv.Count == 0)
                return (false, "Vui lòng chọn ít nhất một nhân viên thực hiện.");

            // Bắt buộc đã chọn ≥1 bước quy trình trước khi phân công SC
            // Đã chọn lúc tạo hồ sơ SC hoặc đã có tiến độ bước
            var soBuocKeHoachSc = await _db.TienDoBuocQuyTrinhs
                .CountAsync(t => t.MaHoSoSuaChua == maHoSo);
            if (soBuocKeHoachSc <= 0)
                return (false,
                    "Hồ sơ chưa có bước quy trình. Vui lòng tạo lại hồ sơ và chọn quy trình, hoặc Lưu bước trên chi tiết hồ sơ trước khi phân công.");

            var coTienDoNvktSc = await _db.TienDoBuocQuyTrinhs.AnyAsync(t =>
                t.MaHoSoSuaChua == maHoSo && t.TrangThai != "DuocChon" && t.MaNhanVien > 0);
            if (coTienDoNvktSc)
                return (false,
                    "Nhân viên kỹ thuật đã bắt đầu thực hiện bước — không được cập nhật phân công.");

            var (okGhiChep, maGhiChep, loiGhiChep) = ResolveNguoiGhiChep(dsNv, dto.MaNhanVienGhiChep);
            if (!okGhiChep)
                return (false, loiGhiChep);

            // Không bắt buộc ngày/giờ dự kiến khi phân công
            if (dto.NgayBatDauDuKien.HasValue && dto.NgayKetThucDuKien.HasValue &&
                dto.NgayKetThucDuKien < dto.NgayBatDauDuKien)
                return (false, "Ngày kết thúc không được trước ngày bắt đầu.");

            // Hủy mềm PC cũ — không đổi khi đang chờ Xưởng
            var pcCu = await _repo.GetPhanCongTheoHoSoSuaChuaAsync(maHoSo);
            if (pcCu.Any(p => p.TrangThai == "Chờ xác nhận"))
                return (false, "Quy trình đang chờ Xưởng xác nhận — không được đổi phân công / người ghi chép.");

            var (okKhoaGc, loiKhoaGc) = await KiemTraKhoaNguoiGhiChepAsync(
                pcCu, maGhiChep, dsNv, maHoSoBaoTri: null, maHoSoSuaChua: maHoSo);
            if (!okKhoaGc)
                return (false, loiKhoaGc);

            foreach (var pc in pcCu.Where(p => p.TrangThai is "Đã phân công" or "Xác nhận" or "Đang thực hiện"))
                pc.TrangThai = "Đã hủy";

            int? maPhanCongDau = null;
            foreach (var maNv in dsNv)
            {
                var phanCong = new PhanCongCongViec
                {
                    MaNhanVienThucHien = maNv,
                    MaNhanVienPhanCong = nhanVienPhanCong.MaNhanVien,
                    MaHoSoSuaChua = maHoSo,
                    NgayBatDauDuKien = dto.NgayBatDauDuKien,
                    NgayKetThucDuKien = dto.NgayKetThucDuKien,
                    TrangThai = "Đã phân công",
                    // Mọi NV được phân công đều được Tiến hành quy trình
                    LaNguoiGhiChep = true,
                    NgayPhanCong = DateTime.Now
                };
                await _repo.AddPhanCongAsync(phanCong);
                await _repo.SaveChangesAsync();
                maPhanCongDau ??= phanCong.MaPhanCong;
            }

            hoSo.MaPhanCong = maPhanCongDau;
            // Giữ "Chờ phân công" trên hồ sơ cho đến khi NVKT bấm "Tiến hành sửa chữa"
            // → hồ sơ vẫn hiện trên thanh trạng thái Tổ trưởng để quản lý.
            if (hoSo.TrangThai is "Chờ phân công" or "Đã duyệt")
                hoSo.TrangThai = "Chờ phân công";
            if (hoSo.MaThieBiNavigation != null)
                hoSo.MaThieBiNavigation.TinhTrangHienTai = "Sửa chữa";

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        /// <summary>
        /// NVKT bấm "Tiến hành sửa chữa" → hồ sơ + mọi phân công đồng bộ "Đang thực hiện"
        /// (hiển thị cho mọi vai trò trên thanh trạng thái).
        /// </summary>
        public async Task<(bool, string?)> NhanVienBatDauSuaChuaAsync(int maHoSo, int maNguoiDung)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nhanVien == null) return (false, "Không xác định được nhân viên.");

            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");
            if (hoSo.TrangThai == "Đã hoàn thành" || hoSo.TrangThai == "Đã hủy" || hoSo.TrangThai == "Từ chối")
                return (false, "Hồ sơ đã kết thúc, không thể tiến hành.");

            var dsPc = await _repo.GetPhanCongTheoHoSoSuaChuaAsync(maHoSo);
            if (!LaNguoiDuocGhiChep(dsPc, nhanVien.MaNhanVien))
                return (false, "Bạn không được phân công trên hồ sơ này.");

            // Đồng bộ tất cả phân công còn mở + hồ sơ → Đang thực hiện
            foreach (var pc in dsPc.Where(p => p.TrangThai is not ("Đã hủy" or "Hoàn thành")))
                pc.TrangThai = "Đang thực hiện";

            hoSo.TrangThai = "Đang thực hiện";
            // Ghi nhận thời điểm bắt đầu thực tế lần đầu
            if (hoSo.ThoiDiemBatDauThucTe == null)
            {
                var now = DateTime.Now;
                hoSo.ThoiDiemBatDauThucTe = now;
                hoSo.GioBatDauDuKien = now.TimeOfDay;
            }
            if (hoSo.MaThieBiNavigation != null)
                hoSo.MaThieBiNavigation.TinhTrangHienTai = "Sửa chữa";

            await _repo.SaveChangesAsync();
            return (true, null);
        }

        /// <summary>NVKT hoàn thành sửa chữa — đồng bộ mọi phân công + hồ sơ + thiết bị.</summary>
        public async Task<(bool, string?)> NhanVienHoanThanhSuaChuaAsync(int maHoSo, int maNguoiDung)
        {
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nhanVien == null) return (false, "Không xác định được nhân viên.");

            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");

            if (hoSo.TrangThai == "Đã hoàn thành")
                return (true, null);

            var dsPc = await _repo.GetPhanCongTheoHoSoSuaChuaAsync(maHoSo);
            if ((hoSo.TrangThai is "Đang thực hiện" or "Chờ xác nhận")
                && dsPc.Any(p => p.TrangThai == "Chờ xác nhận"))
                return (true, null);

            if (hoSo.TrangThai is not ("Đang thực hiện" or "Chờ phân công" or "Đã duyệt" or "Từ chối" or "Chờ xác nhận"))
                return (false, "Hồ sơ không ở trạng thái đang sửa chữa.");

            if (!LaNguoiDuocGhiChep(dsPc, nhanVien.MaNhanVien))
                return (false, "Chỉ người ghi chép quy trình mới được bấm Xong / gửi Xưởng.");

            // Xong → PC chờ Xưởng; HS giữ Đang thực hiện
            foreach (var pc in dsPc.Where(p => p.TrangThai is not ("Đã hủy" or "Hoàn thành")))
            {
                pc.TrangThai = "Chờ xác nhận";
                pc.LyDoTuChoi = null;
                pc.MaHoSoSuaChua ??= maHoSo;
            }

            hoSo.TrangThai = "Đang thực hiện";
            hoSo.LyDoTuChoi = null;

            await _repo.SaveChangesAsync();
            return (true, null);
        }



        public async Task<(bool, string?)> XacNhanHoanThanhSuaChuaAsync(int maHoSo, XacNhanDto dto)
        {
            var hoSo = await _repo.GetHoSoSuaChuaByIdAsync(maHoSo);
            if (hoSo == null) return (false, "Không tìm thấy hồ sơ.");

            if (hoSo.MaPhanCong == null || !await _repo.DaCoKetQuaAsync(hoSo.MaPhanCong.Value))
                return (false, "Chưa có kết quả thực hiện được ghi nhận, không thể xác nhận hoàn thành.");

            if (!dto.Dat)
            {
                hoSo.TrangThai = "Đang thực hiện";
                await _repo.SaveChangesAsync();
                return (true, "Yêu cầu nhân viên thực hiện lại.");
            }

            hoSo.TrangThai = "Đã hoàn thành";
            GhiNhanKetThucThucTe(hoSo);
            // Hoàn thành sửa chữa → trở về Sản xuất
            if (hoSo.MaThieBiNavigation != null)
                hoSo.MaThieBiNavigation.TinhTrangHienTai = "Sản xuất";
            await _repo.SaveChangesAsync();
            return (true, null);
        }
    }
}
