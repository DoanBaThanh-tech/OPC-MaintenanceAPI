using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class AiChatService
    {
        private static string ChuDeCua(string q)
        {
            if (LaHoiChoPhanCong(q)) return "cho_phan_cong_bt";
            if (Co(q, "thiet bi", "may moc")) return "thiet_bi";
            if (Co(q, "duyet") && Co(q, "chot ngay", "len lich", "xep lich")) return "phe_duyet_chot_ngay";
            if (Co(q, "hoan thanh") && Co(q, "thang", "nam"))
                return Co(q, "sua chua", "sc") && !Co(q, "bao tri", "bt") ? "sc_hoan_thanh_thang" : "bt_hoan_thanh_thang";
            if (Co(q, "buoc", "quy trinh")) return "buoc_quy_trinh";
            if (Co(q, "nhan vien") && Co(q, "phan cong")) return "nhan_vien_dang_phan_cong";
            if (Co(q, "chi tiet", "mo ho so")) return "chi_tiet_ho_so";
            if (Co(q, "vat tu", "nguyen vat lieu")) return "vat_tu";
            if (Co(q, "lich bao tri", "lich du kien")) return "lich_bao_tri";
            return "chung";
        }

        private static string MoTaChuDe(string chuDe) => chuDe switch
        {
            "cho_phan_cong_bt" => "hồ sơ bảo trì chờ phân công",
            "thiet_bi" => "thiết bị",
            "bt_hoan_thanh_thang" => "hồ sơ bảo trì hoàn thành trong tháng",
            "sc_hoan_thanh_thang" => "hồ sơ sửa chữa hoàn thành trong tháng",
            "buoc_quy_trinh" => "bước quy trình",
            "vat_tu" => "hồ sơ vật tư",
            _ => "nội dung bạn hỏi"
        };

        /// <summary>Chỉ tổng hợp COUNT/MAX tại DB. Đây là dấu kiểm toán, không dùng để thay dữ liệu hiện tại.</summary>
        private async Task<string> TinhFingerprintAsync(int maNd, string vt, string q, CancellationToken ct)
        {
            var bt = _db.HoSoBaoTris.AsNoTracking().AsQueryable();
            var sc = _db.HoSoSuaChuas.AsNoTracking().AsQueryable();
            var pc = _db.PhanCongCongViecs.AsNoTracking().AsQueryable();
            var tb = _db.ThietBis.AsNoTracking().AsQueryable();
            var kh = _db.ChiTietKeHoachBaoTris.AsNoTracking().AsQueryable();
            if (vt == "Nhân viên kỹ thuật")
            {
                var maNv = await _db.NhanViens.AsNoTracking().Where(n => n.MaNguoiDung == maNd)
                    .Select(n => (int?)n.MaNhanVien).FirstOrDefaultAsync(ct);
                pc = pc.Where(p => p.MaNhanVienThucHien == maNv);
                bt = bt.Where(h => pc.Any(p => p.MaHoSoBaoTri == h.MaHoSoBaoTri));
                sc = sc.Where(h => pc.Any(p => p.MaHoSoSuaChua == h.MaHoSoSuaChua));
                // Không tổng hợp danh mục/lịch toàn công ty cho NVKT.
                tb = tb.Where(_ => false);
                kh = kh.Where(c => c.MaHoSoBaoTri != null && bt.Any(h => h.MaHoSoBaoTri == c.MaHoSoBaoTri));
            }

            var btAgg = await bt.GroupBy(h => h.TrangThai).Select(g => new
            {
                K = g.Key, N = g.Count(), Id = g.Max(h => h.MaHoSoBaoTri),
                Tao = g.Max(h => h.NgayTao), Duyet = g.Max(h => h.NgayDuyet),
                BatDau = g.Max(h => h.ThoiDiemBatDauThucTe), KetThuc = g.Max(h => h.ThoiDiemKetThucThucTe)
            }).ToListAsync(ct);
            var scAgg = await sc.GroupBy(h => h.TrangThai).Select(g => new
            {
                K = g.Key, N = g.Count(), Id = g.Max(h => h.MaHoSoSuaChua),
                Tao = g.Max(h => h.NgayTao), Duyet = g.Max(h => h.NgayDuyet),
                BatDau = g.Max(h => h.ThoiDiemBatDauThucTe), KetThuc = g.Max(h => h.ThoiDiemKetThucThucTe)
            }).ToListAsync(ct);
            var pcAgg = await pc.GroupBy(p => p.TrangThai).Select(g => new
            {
                K = g.Key, N = g.Count(), Id = g.Max(p => p.MaPhanCong), Ngay = g.Max(p => p.NgayPhanCong)
            }).ToListAsync(ct);
            var tbAgg = await tb.GroupBy(t => t.TinhTrangHienTai).Select(g => new
            {
                K = g.Key, N = g.Count(), Id = g.Max(t => t.MaThietBi),
                GanNhat = g.Max(t => t.NgayBaoTriGanNhat), TiepTheo = g.Max(t => t.NgayBaoTriTiepTheo)
            }).ToListAsync(ct);
            var khAgg = await kh.GroupBy(_ => 1).Select(g => new
            {
                N = g.Count(), Id = g.Max(c => c.MaChiTietKeHoach), Ngay = g.Max(c => c.NgayDuKienBaoTri)
            }).FirstOrDefaultAsync(ct);
            var summary = JsonSerializer.Serialize(new
            {
                Bt = btAgg.OrderBy(x => x.K, StringComparer.Ordinal),
                Sc = scAgg.OrderBy(x => x.K, StringComparer.Ordinal),
                Pc = pcAgg.OrderBy(x => x.K, StringComparer.Ordinal),
                Tb = tbAgg.OrderBy(x => x.K, StringComparer.Ordinal), Kh = khAgg
            });
            return $"v=4|u={maNd}|r={Bam(vt)}|q={Bam(q)}|day={DateTime.Today:yyyyMMdd}|db={Bam(summary)}";
        }

        // Giữ nguyên chữ ký để không sửa bất kỳ hàm đếm nào: dữ liệu nhớ không được ghi đè số DB.
        private Task<int?> LaySoNhoAsync(string vt, string chuDe, bool boQua, CancellationToken ct) =>
            Task.FromResult<int?>(null);

        private async Task<bool> LuuSoDaXacNhanAsync(int maNd, string vt, string cauHoi,
            KetQuaXuLy ketQuaDb, CancellationToken ct)
        {
            if (ketQuaDb.SoLuong == null) return false;
            var fp = await TinhFingerprintAsync(maNd, vt, cauHoi, ct);
            return ChatbotTriNhoStore.GhiNhan(new TriNhoChat
            {
                MaNguoiDung = maNd, VaiTro = vt, ChuDe = ketQuaDb.ChuDe,
                Loai = "so_lieu", SoLuong = ketQuaDb.SoLuong, DaDoiChieuDb = true, Fingerprint = fp
            });
        }

        private static int? TrichSoTuNhien(string q)
        {
            if (Co(q, "bao nhieu", "co may") || Regex.IsMatch(q, @"\d\s*[/\-.]\s*\d")) return null;
            if (Co(q, "khong co", "chua co", "lam gi co")) return 0;
            var m = Regex.Match(q,
                @"(?:so dung la|so dung|dung la|phai la|thuc te la|chi co|chi la|that ra la|tren app la)\s*(\d{1,6})\b|^(\d{1,6})\s*(?:ho so|thiet bi|viec|cai)?\s*(?:thoi)?$");
            var value = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return m.Success && int.TryParse(value, out var number) ? number : null;
        }

        private static bool CoMotSoDemXacDinh(KetQuaXuLy kq, string q)
        {
            if (!kq.DaXuLy || kq.SoLuong == null || string.IsNullOrWhiteSpace(kq.TraLoi)) return false;
            // Chỉ xác nhận nhánh đếm đầy đủ; lịch sử PC nay COUNT toàn bộ query đúng loại, loại hủy.
            // Không xác minh các nhánh capped, nhiều metric tiền/% hoặc số bước.
            if (kq.ChuDe is not ("cho_phan_cong_bt" or "bt_hoan_thanh_thang" or "sc_hoan_thanh_thang"
                or "thiet_bi" or "ho_so_theo_ngay" or "ho_so_theo_trang_thai" or "lich_su_phan_cong")) return false;
            if (Co(q, "bao tri", "bt") && Co(q, "sua chua", "sc") || Co(q, "va", "dong thoi")) return false;
            var answer = ChuanHoa(kq.TraLoi);
            if (Co(answer, "chua xac dinh", "ngoai pham vi", "chi xem", "chi tra")) return false;
            // Không đồng nhất số phân công với số nhân viên, tổng bước với bước còn lại, hoặc hai bộ đếm.
            if (kq.ChuDe is "nhan_vien_dang_phan_cong" or "buoc_quy_trinh" or "phe_duyet_chot_ngay") return false;
            var first = Regex.Match(kq.TraLoi, @"\*\*(\d+)\*\*");
            if (first.Success) return int.Parse(first.Groups[1].Value) == kq.SoLuong.Value;
            return kq.SoLuong == 0 && Co(answer, "khong co", "chua co");
        }

        private async Task<string?> XuLyDayTuUserAsync(int maNd, string vt, string ten, string q,
            IReadOnlyList<(string role, string content)>? lichSu, CancellationToken ct, string? cauHoiDich = null)
        {
            var lessons = new List<string>();
            var wantsLesson = Co(q, "nho", "tu gio", "lan sau", "dung", "toi hoi", "minh hoi", "y toi la", "y minh la", "khong phai");
            if (wantsLesson)
            {
                if (Co(q, "tra loi ngan", "ngan gon", "bot dai")) lessons.Add("tra_loi_ngan");
                if (Co(q, "chi dem", "chi can so", "khong liet ke")) lessons.Add("chi_dem");
                if (Co(q, "tach y", "tach tung y", "tach cau nhieu ve")) lessons.Add("tach_nhieu_y");
                if (Co(q, "khong phai lich su", "khong phai bang phan cong") && Co(q, "ho so", "hs")) lessons.Add("ho_so_khong_lich_su");
                if (Co(q, "phan biet bao tri va sua chua", "khong lay so bao tri cho sua chua")) lessons.Add("phan_biet_bt_sc");
                if (Co(q, "ngay cu the", "dung ngay") && Co(q, "khong phai ca thang", "dung doi thanh thang", "khong doi thanh thang")) lessons.Add("ngay_khong_thang");
            }
            if (lessons.Count > 0)
            {
                var saved = lessons.All(id => ChatbotTriNhoStore.GhiNhan(new TriNhoChat
                {
                    MaNguoiDung = maNd, VaiTro = vt, ChuDe = "trinh_bay_va_cach_hieu", Loai = "hieu_ngu_canh", BaiHoc = id
                }));
                if (VetHienTai.Value is { } trace) trace.BaiHoc = BaiHocCua(maNd, vt);
                var acknowledgement = await DienDatQuyetDinhAsync(vt, q,
                    saved ? "bai_hoc_da_luu" : "chua_luu_duoc", lichSu, ct);
                if (Co(q, "toi hoi", "minh hoi", "y toi la", "y minh la") && Co(q, "ho so", "hs"))
                {
                    var clarified = Regex.Split(q, @"\b(?:chu|khong phai)\b")[0];
                    var retry = await XuLyYDinhAsync(maNd, vt, ten, clarified, clarified, ct, boQuaTriNho: true);
                    if (retry.DaXuLy && retry.TraLoi != null) return retry.TraLoi + "\n" + acknowledgement;
                }
                return acknowledgement;
            }

            var proposed = TrichSoTuNhien(q);
            var isCorrection = Co(q, "so dung", "dung la", "phai la", "thuc te la", "chi co", "chi la", "tren app la",
                "khong co", "chua co", "lam gi co", "sai roi", "khong dung", "chua dung")
                || Regex.IsMatch(q, @"^\d+\s*(?:ho so|thiet bi|viec|cai)?\s*(?:thoi)?$");
            if (!isCorrection || proposed == null) return null;
            var previous = cauHoiDich ?? TrichCauHoiTruocTuLichSu(lichSu);
            if (previous == null || cauHoiDich == null && !(lichSu?.Any(x => x.role == "assistant") ?? false)) return null;
            var previousQ = HoanThienCauNoiTiep(ChuanHoa(previous), LichSuTruocCauHoi(lichSu, previous));
            var denied = await KiemTraPhamViAsync(maNd, vt, previousQ, ct);
            if (denied != null) return denied;
            previousQ = ApDungCachHieu(previousQ, BaiHocCua(maNd, vt));
            var check = await XuLyYDinhAsync(maNd, vt, ten, previousQ, previous, ct, boQuaTriNho: true);
            if (!CoMotSoDemXacDinh(check, previousQ))
            {
                if (VetHienTai.Value is { } trace) trace.DoiChieu = "khong_du_bo_loc";
                return await DienDatQuyetDinhAsync(vt, q, "chua_xac_minh_so", lichSu, ct);
            }
            if (check.SoLuong != proposed)
            {
                if (VetHienTai.Value is { } trace) trace.DoiChieu = "lech_khong_luu";
                return $"Mình đối chiếu lại: **{check.SoLuong}**, khác **{proposed}** bạn thấy.\n"
                    + check.TraLoi + "\n" + await DienDatQuyetDinhAsync(vt, q, "lech_so", lichSu, ct);
            }
            var persisted = await LuuSoDaXacNhanAsync(maNd, vt, previousQ, check, ct);
            if (VetHienTai.Value is { } tr) tr.DoiChieu = persisted ? "khop_da_luu_kiem_toan" : "khop_chua_luu";
            // Không chuyển số đã xác nhận thành bài học; lần sau luôn truy vấn lại.
            return check.TraLoi + "\n" + await DienDatQuyetDinhAsync(vt, q,
                persisted ? "so_khop_da_luu" : "chua_luu_duoc", lichSu, ct);
        }

        private async Task<string> XuLyBaoSaiAsync(int maNd, string vt, string ten, string? cauHoiGoc,
            IReadOnlyList<(string role, string content)>? lichSu, CancellationToken ct)
        {
            var original = !string.IsNullOrWhiteSpace(cauHoiGoc) ? cauHoiGoc.Trim() : TrichCauHoiTruocTuLichSu(lichSu);
            if (string.IsNullOrWhiteSpace(original))
                return await DienDatQuyetDinhAsync(vt, "", "thieu_cau_goc", lichSu, ct);
            var q = HoanThienCauNoiTiep(ChuanHoa(original), LichSuTruocCauHoi(lichSu, original));
            var denied = await KiemTraPhamViAsync(maNd, vt, q, ct);
            if (denied != null) return denied;
            q = ApDungCachHieu(q, BaiHocCua(maNd, vt));
            var result = await XuLyYDinhAsync(maNd, vt, ten, q, original, ct, boQuaTriNho: true);
            if (VetHienTai.Value is { } trace) trace.DoiChieu = result.DaXuLy ? "bao_sai_tra_db_lai" : "bao_sai_chua_co_nhanh";
            if (result.DaXuLy && result.TraLoi != null)
                return result.TraLoi + "\n" + await DienDatQuyetDinhAsync(vt, q, "da_kiem_tra_lai", lichSu, ct);
            // Không lưu số chỉ vì bấm báo sai, không xóa bài học an toàn, không nhờ LLM đoán số.
            return await DienDatQuyetDinhAsync(vt, q, "chua_xac_minh_so", lichSu, ct);
        }
    }
}
