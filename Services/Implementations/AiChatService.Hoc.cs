using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public partial class AiChatService
    {
        // ===================== HỌC TỪ NGƯỜI DÙNG + BÁO SAI =====================

        /// <summary>Chủ đề ổn định để gắn ghi nhớ — không dùng cả câu làm khóa.</summary>
        private static string ChuDeCua(string q)
        {
            if (LaHoiChoPhanCong(q)) return "cho_phan_cong_bt";
            if (Co(q, "thiet bi", "may moc")) return "thiet_bi";
            if (Co(q, "duyet") && Co(q, "chot ngay", "len lich", "xep lich")) return "phe_duyet_chot_ngay";
            if (Co(q, "hoan thanh") && Co(q, "thang", "nam")) return "bt_hoan_thanh_thang";
            if (Co(q, "buoc", "quy trinh")) return "buoc_quy_trinh";
            if (Co(q, "nhan vien") && Co(q, "phan cong")) return "nhan_vien_dang_phan_cong";
            if (Co(q, "chi tiet", "mo ho so")) return "chi_tiet_ho_so";
            if (Co(q, "vat tu", "nguyen vat lieu")) return "vat_tu";
            if (Co(q, "lich bao tri", "lich du kien")) return "lich_bao_tri";
            return "chung";
        }

        private static string MoTaChuDe(string chuDe) => chuDe switch
        {
            "cho_phan_cong_bt" => "số hồ sơ bảo trì đang chờ phân công",
            "thiet_bi" => "số thiết bị",
            "phe_duyet_chot_ngay" => "số hồ sơ đã duyệt / đã chốt ngày",
            "bt_hoan_thanh_thang" => "số hồ sơ bảo trì đã hoàn thành trong tháng",
            "buoc_quy_trinh" => "số bước quy trình",
            "nhan_vien_dang_phan_cong" => "số nhân viên đang được phân công",
            "chi_tiet_ho_so" => "thông tin chi tiết hồ sơ",
            "vat_tu" => "số hồ sơ vật tư",
            "lich_bao_tri" => "lịch bảo trì",
            _ => "nội dung bạn hỏi"
        };

        private static List<string> KhoaTokens(string q) =>
            q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 2)
                .Where(t => !new HashSet<string>
                {
                    "co", "bao", "nhieu", "la", "gi", "cua", "toi", "minh", "va", "cho",
                    "trong", "nay", "do", "cac", "nhung", "dang", "hien", "thi", "nhu", "the"
                }.Contains(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

        /// <summary>Dấu vân tay dữ liệu — số người dùng xác nhận chỉ dùng khi dữ liệu chưa đổi.</summary>
        private async Task<string> TinhFingerprintAsync(CancellationToken ct)
        {
            var bt = await _db.HoSoBaoTris.AsNoTracking().Select(h => h.TrangThai).ToListAsync(ct);
            var sc = await _db.HoSoSuaChuas.AsNoTracking().Select(h => h.TrangThai).ToListAsync(ct);
            var pc = await _db.PhanCongCongViecs.AsNoTracking().Select(p => p.TrangThai).ToListAsync(ct);
            var tb = await _db.ThietBis.AsNoTracking().Select(t => t.TinhTrangHienTai).ToListAsync(ct);
            var kh = await _db.ChiTietKeHoachBaoTris.AsNoTracking()
                .Select(c => new { c.MaHoSoBaoTri, c.NgayDuKienBaoTri }).ToListAsync(ct);

            var now = DateTime.Now;
            var khThang = kh.Count(c => c.MaHoSoBaoTri != null
                                        && c.NgayDuKienBaoTri.Year == now.Year
                                        && c.NgayDuKienBaoTri.Month == now.Month);

            static string Nhom(IEnumerable<string?> ds) =>
                string.Join(",", ds.GroupBy(x => x ?? "")
                    .OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}:{g.Count()}"));

            return $"bt={Nhom(bt)}|sc={Nhom(sc)}|pc={Nhom(pc)}|tb={Nhom(tb)}|kh={khThang}";
        }

        private async Task<int?> LaySoNhoAsync(string vt, string chuDe, bool boQua, CancellationToken ct)
        {
            if (boQua) return null;
            var fp = await TinhFingerprintAsync(ct);
            return ChatbotTriNhoStore.LaySoDaXacNhan(vt, chuDe, fp);
        }

        private async Task LuuSoDaXacNhanAsync(
            string vt, string chuDe, string cauHoiMau, int soLuong, CancellationToken ct)
        {
            var fp = await TinhFingerprintAsync(ct);
            ChatbotTriNhoStore.GhiNhan(new TriNhoChat
            {
                VaiTro = vt,
                ChuDe = chuDe,
                CauHoiMau = cauHoiMau,
                Tokens = KhoaTokens(ChuanHoa(cauHoiMau)),
                Loai = "so_lieu",
                SoLuong = soLuong,
                Fingerprint = fp,
                GhiChu = $"Đã đối chiếu lại: {MoTaChuDe(chuDe)} = {soLuong}."
            });
        }

        private static string GhiChuDaHoc(string vt)
        {
            var ds = ChatbotTriNhoStore.LayTheoVaiTro(vt)
                .Where(x => x.Loai == "hieu_ngu_canh" && !string.IsNullOrWhiteSpace(x.GhiChu))
                .Select(x => "• " + x.GhiChu.Trim().Replace('\n', ' '))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
            return string.Join("\n", ds);
        }

        /// <summary>Lấy con số đúng người dùng vừa nói (0 nếu nói «không có»).</summary>
        private static int? TrichSoTuNhien(string q)
        {
            if (Co(q, "khong co", "khong co cai nao", "lam gi co", "hien khong co", "chua co"))
                return 0;

            // Chỉ nhận câu khẳng định kiểu «số đúng là 4», «chỉ có 4», «thực tế là 4»…
            var m = Regex.Match(q,
                @"(?:so dung la|so dung|dung la|phai la|thuc te la|chi co|chi la|lam gi co|that ra la)\s*(\d{1,4})");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
                return n;

            // Câu chỉnh sửa ngắn kiểu «4 hồ sơ thôi», «trên app là 4».
            if (q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 10)
            {
                m = Regex.Match(q, @"\b(\d{1,4})\b");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var n2))
                    return n2;
            }

            return null;
        }

        /// <summary>
        /// Người dùng chỉnh lại bot sau một câu trả lời (vd «không có», «số đúng là 0»,
        /// «tôi hỏi hồ sơ chờ phân công chứ không phải lịch sử phân công»).
        /// Ghi nhớ cách hiểu rồi trả lời lại ngay, không lặp đáp án cũ.
        /// </summary>
        private async Task<string?> XuLyDayTuUserAsync(
            int maNd, string vt, string ten, string q,
            IReadOnlyList<(string role, string content)>? lichSu, CancellationToken ct)
        {
            if (lichSu == null || lichSu.Count == 0) return null;
            var coDapAnBot = lichSu.Any(x =>
                string.Equals(x.role, "assistant", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x.content));
            if (!coDapAnBot) return null;

            var cauTruoc = TrichCauHoiTruocTuLichSu(lichSu);
            if (string.IsNullOrWhiteSpace(cauTruoc)) return null;

            var qTruoc = ChuanHoa(cauTruoc);
            var chuDeTruoc = ChuDeCua(qTruoc);

            // 1) Người dùng nói rõ ý: đang hỏi hồ sơ, không phải lịch sử phân công.
            if (Co(q, "khong phai lich su", "khong phai bang phan cong", "khong phai phan cong",
                    "toi hoi ho so", "minh hoi ho so", "y minh la", "y toi la", "minh hoi ve ho so")
                && (chuDeTruoc == "cho_phan_cong_bt"
                    || Co(q, "ho so bao tri", "hs bao tri", "ho so cho phan cong", "cho phan cong")))
            {
                // Ưu tiên chủ đề của chính câu chỉnh sửa (nếu nó nói rõ «chờ phân công»).
                var chuDeGhi = ChuDeCua(q) == "cho_phan_cong_bt" ? "cho_phan_cong_bt" : chuDeTruoc;
                var qLai = chuDeGhi == "cho_phan_cong_bt" && chuDeTruoc != "cho_phan_cong_bt" ? q : qTruoc;

                ChatbotTriNhoStore.GhiNhan(new TriNhoChat
                {
                    VaiTro = vt,
                    ChuDe = chuDeGhi,
                    CauHoiMau = cauTruoc,
                    Tokens = KhoaTokens(qTruoc),
                    Loai = "hieu_ngu_canh",
                    GhiChu = "Người dùng hỏi HỒ SƠ BẢO TRÌ đang chờ phân công, không hỏi lịch sử phân công (PC#)."
                });

                var lai = await XuLyYDinhAsync(maNd, vt, ten, qLai, cauTruoc, ct, boQuaTriNho: true);
                if (lai.DaXuLy && !string.IsNullOrWhiteSpace(lai.TraLoi))
                    return "Dạ, mình hiểu rồi — bạn đang hỏi **hồ sơ bảo trì chờ phân công**, không phải lịch sử phân công. Mình đã ghi nhớ.\n"
                           + lai.TraLoi;
            }

            // 2) Người dùng đưa con số/trạng thái đúng.
            var coSua = Co(q, "khong co", "khong co cai nao", "lam gi co", "sai roi", "nham roi",
                "so dung", "dung la", "phai la", "thuc te la", "chi co", "khong dung", "chua dung",
                "doc lai", "sua lai", "bi lech", "dem sai", "hien khong co");
            if (!coSua) return null;

            // «… đúng là bao nhiêu?» là câu hỏi số lượng mới, không phải lời chỉnh sửa.
            if (Co(q, "bao nhieu", "may ho so", "may cai", "bao nhieu cai"))
                return null;

            var so = TrichSoTuNhien(q);
            if (so == null)
                return "Dạ, mình xin lỗi vì câu trả lời trước chưa đúng. Bạn cho mình biết con số đúng "
                       + "hoặc trạng thái đúng để mình ghi nhớ và trả lời lại nhé.";

            await LuuSoDaXacNhanAsync(vt, chuDeTruoc, cauTruoc, so.Value, ct);
            ChatbotTriNhoStore.GhiNhan(new TriNhoChat
            {
                VaiTro = vt,
                ChuDe = chuDeTruoc,
                CauHoiMau = cauTruoc,
                Tokens = KhoaTokens(qTruoc),
                Loai = "hieu_ngu_canh",
                GhiChu = $"Người dùng xác nhận {MoTaChuDe(chuDeTruoc)} là {so.Value}."
            });

            return $"Dạ, mình đã ghi nhớ chỉnh sửa của bạn: {MoTaChuDe(chuDeTruoc)} là **{so.Value}**. "
                   + "Lần sau hỏi lại mình sẽ trả lời theo con số này.";
        }

        /// <summary>
        /// «Báo sai — kiểm tra lại»: bỏ số đã lưu của chủ đề, đếm lại từ dữ liệu app,
        /// xin lỗi ngắn và trả con số đúng. Tuyệt đối không phát lại đáp án cũ.
        /// </summary>
        private async Task<string> XuLyBaoSaiAsync(
            int maNd, string vt, string ten,
            string? cauHoiGoc,
            IReadOnlyList<(string role, string content)>? lichSu,
            CancellationToken ct)
        {
            var cauGoc = !string.IsNullOrWhiteSpace(cauHoiGoc)
                ? cauHoiGoc.Trim()
                : TrichCauHoiTruocTuLichSu(lichSu);

            if (string.IsNullOrWhiteSpace(cauGoc))
                return "Dạ, mình xin lỗi vì câu trả lời trước chưa đúng. "
                       + "Bạn nhắc lại giúp mình câu hỏi muốn kiểm tra (hồ sơ/tháng/trạng thái) nhé.";

            var qGoc = ChuanHoa(cauGoc);
            var chuDe = ChuDeCua(qGoc);

            // Bỏ số đã lưu của chủ đề này để đếm lại từ đầu.
            ChatbotTriNhoStore.XoaTheoLoai(vt, chuDe, "so_lieu");
            ChatbotTriNhoStore.XoaTheoLoai(vt, chuDe, "hieu_ngu_canh");

            var kq = await XuLyYDinhAsync(maNd, vt, ten, qGoc, cauGoc, ct, boQuaTriNho: true);
            string body;

            if (kq.DaXuLy && !string.IsNullOrWhiteSpace(kq.TraLoi))
            {
                body = kq.TraLoi!;
                if (kq.SoLuong != null)
                    await LuuSoDaXacNhanAsync(vt, kq.ChuDe, cauGoc, kq.SoLuong.Value, ct);
            }
            else if (!string.IsNullOrEmpty(GroqKey))
            {
                var context = await NapContextVaiTroAsync(maNd, vt, ct);
                var (ok, text, err) = await GoiGroqAsync(vt, ten, cauGoc, context, lichSu, ct, suaSai: true);
                if (ok && !string.IsNullOrWhiteSpace(text))
                {
                    var s = LamSachTraLoi(text!);
                    body = LaKhongBiet(s)
                        ? "Mình đã đối chiếu lại nhưng chưa xác định được con số đúng cho câu này nên không dám đoán bừa. "
                          + "Bạn cho mình biết mốc cần xem (tháng/trạng thái/hồ sơ) nhé."
                        : s;
                }
                else
                {
                    _logger.LogWarning("Groq báo sai lỗi: {Err}", err);
                    body = "Mình đã đối chiếu lại nhưng chưa xác định được con số đúng cho câu này nên không dám đoán bừa. "
                           + "Bạn cho mình biết mốc cần xem (tháng/trạng thái/hồ sơ) nhé.";
                }
            }
            else
            {
                body = "Mình đã đối chiếu lại nhưng chưa xác định được con số đúng cho câu này nên không dám đoán bừa. "
                       + "Bạn cho mình biết mốc cần xem (tháng/trạng thái/hồ sơ) nhé.";
            }

            return "Dạ, mình xin lỗi vì câu trả lời trước chưa đúng. Mình đã đối chiếu lại ngay:\n" + body;
        }
    }
}
