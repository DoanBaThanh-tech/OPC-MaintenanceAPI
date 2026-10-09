using System.Text.RegularExpressions;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.DTOs.Auth;
using OPC.MaintenanceAPI.Helpers;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Security;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public class AuthService : IAuthService
    {
        private const string VaiTroGiamDoc = "Giám đốc";
        private const string VaiTroPhoGiamDoc = "Phó giám đốc";
        private const int SoPhutChoOtp = 1; // chờ gửi lại OTP (phút)

        private readonly IQuanLyNguoiDungRepository _userRepo;
        private readonly INhanVienRepository _nhanVienRepo;
        private readonly IXacThucQuenMatKhauRepository _otpRepo;
        private readonly IConfiguration _config;
        private readonly LoginAttemptGuard _loginGuard;
        private readonly IEmailSender _emailSender;

        private static readonly Regex MatKhauHopLe =
            new(@"^(?=.*[0-9])(?=.*[!@#$%^&*(),.?"":{}|<>_\-]).{8,}$"); // ≥8, có số + ký tự đặc biệt

        public AuthService(IQuanLyNguoiDungRepository userRepo, INhanVienRepository nhanVienRepo,
                            IXacThucQuenMatKhauRepository otpRepo, IConfiguration config,
                            LoginAttemptGuard loginGuard, IEmailSender emailSender)
        {
            _userRepo = userRepo;
            _nhanVienRepo = nhanVienRepo;
            _otpRepo = otpRepo;
            _config = config;
            _loginGuard = loginGuard;
            _emailSender = emailSender;
        }

        public async Task<List<object>> GetAllAsync()
        {
            var list = await _userRepo.GetAllWithDetailsAsync();
            return list.Select(u => (object)new
            {
                u.MaNguoiDung,
                u.Email,
                u.MaVaiTro,
                TenVaiTro = u.MaVaiTroNavigation?.TenVaiTro,
                HoTen = u.NhanVien?.HoTen ?? u.Email,
                SoDienThoai = u.NhanVien?.SoDienThoai,
                ChucVu = u.NhanVien?.ChucVu,
                u.TrangThai,
                u.LanDangNhapCuoi,
                u.NgayTao
            }).ToList();
        }

        public async Task<AuthResult> GetByIdAsync(int id)
        {
            var u = await _userRepo.GetByIdAsync(id);
            if (u == null) return new AuthResult { ThanhCong = false, Message = "Không tìm thấy tài khoản." };
            return new AuthResult { ThanhCong = true, Data = new { u.MaNguoiDung, u.Email, u.MaVaiTro, u.TrangThai } };
        }

        public async Task<AuthResult> DangNhapAsync(DangNhapDto dto)
        {
            var email = (dto.Email ?? "").Trim();
            if (_loginGuard.IsLocked(email, out var conLai))
            {
                var phut = Math.Max(1, (int)Math.Ceiling(conLai.TotalMinutes));
                return new AuthResult
                {
                    ThanhCong = false,
                    Message = $"Tài khoản tạm khóa do đăng nhập sai nhiều lần. Thử lại sau khoảng {phut} phút."
                };
            }

            var user = await _userRepo.GetByEmailAsync(email);
            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.MatKhau, user.MatKhau))
            {
                _loginGuard.RegisterFailure(email);
                return new AuthResult { ThanhCong = false, Message = "Email hoặc mật khẩu không đúng." };
            }

            if (user.TrangThai == "Đã khóa")
                return new AuthResult { ThanhCong = false, Message = "Tài khoản đã bị khóa." };
            if (user.TrangThai == "Chưa kích hoạt")
                return new AuthResult { ThanhCong = false, Message = "Tài khoản chưa được kích hoạt." };

            _loginGuard.RegisterSuccess(email);

            user.LanDangNhapCuoi = DateTime.Now;
            _userRepo.Update(user);
            await _userRepo.SaveChangesAsync();

            var token = JwtHelper.TaoToken(user.MaNguoiDung, user.Email, user.MaVaiTro,
                user.MaVaiTroNavigation!.TenVaiTro, _config);

            // Lấy họ tên nhân viên gắn với tài khoản (nếu có) để hiện trên slide menu
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(user.MaNguoiDung);
            var hoTen = nhanVien?.HoTen ?? user.Email;

            return new AuthResult
            {
                ThanhCong = true,
                Data = new
                {
                    user.MaNguoiDung,
                    user.Email,
                    HoTen = hoTen,
                    VaiTro = user.MaVaiTroNavigation.TenVaiTro,
                    user.MaVaiTro,
                    Token = token
                }
            };
        }

        private static readonly HashSet<string> VaiTroChoPhep = new(StringComparer.OrdinalIgnoreCase)
        {
            "Tổ trưởng cơ điện", "Xưởng", "Giám đốc", "Nhân viên kỹ thuật", "Admin hệ thống"
        };

        private static readonly Regex HoTenHopLe =
            new(@"^[\p{L}\s]+$", RegexOptions.CultureInvariant);

        private static string? KiemTraEmailCongTy(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return "Vui lòng nhập email công ty.";
            var e = email.Trim();
            if (!e.EndsWith("@opc.com", StringComparison.OrdinalIgnoreCase))
                return "Email công ty phải có đuôi @opc.com.";
            if (!e.Contains('@') || e.StartsWith("@"))
                return "Email không hợp lệ.";
            return null;
        }

        private static string? KiemTraHoTen(string? hoTen, bool batBuoc)
        {
            var t = (hoTen ?? "").Trim();
            if (t.Length == 0)
                return batBuoc ? "Họ tên không được để trống." : null;
            if (t.Contains('@') || t.Contains("opc.com", StringComparison.OrdinalIgnoreCase))
                return "Họ tên không được điền email công ty.";
            if (!HoTenHopLe.IsMatch(t))
                return "Họ tên chỉ gồm chữ cái và khoảng trắng (không số, không ký tự đặc biệt).";
            return null;
        }

        // Luồng 4 — nhánh "Tạo mới" (không bắt chức vụ; họ tên có thể trống)
        public async Task<AuthResult> TaoTaiKhoanAsync(TaoTaiKhoanDto dto)
        {
            if (!MatKhauHopLe.IsMatch(dto.MatKhau))
                return new AuthResult { ThanhCong = false, Message = "Mật khẩu phải có ít nhất 8 ký tự, gồm 1 chữ số và 1 ký tự đặc biệt." };

            var loiEmail = KiemTraEmailCongTy(dto.Email);
            if (loiEmail != null)
                return new AuthResult { ThanhCong = false, Message = loiEmail };

            // Họ tên được để trống khi chưa biết — user tự cập nhật sau
            var loiHoTen = KiemTraHoTen(dto.HoTen, batBuoc: false);
            if (loiHoTen != null)
                return new AuthResult { ThanhCong = false, Message = loiHoTen };

            if (await _userRepo.EmailExistsAsync(dto.Email.Trim()))
                return new AuthResult { ThanhCong = false, Message = "Email đã tồn tại." };

            var vaiTro = await _userRepo.GetVaiTroByIdAsync(dto.MaVaiTro);
            if (vaiTro == null)
                return new AuthResult { ThanhCong = false, Message = "Vai trò không hợp lệ." };
            if (!VaiTroChoPhep.Contains(vaiTro.TenVaiTro) ||
                string.Equals(vaiTro.TenVaiTro, "Admin hệ thống", StringComparison.OrdinalIgnoreCase))
                return new AuthResult { ThanhCong = false, Message = "Chỉ được gán: Tổ trưởng cơ điện, Xưởng, Giám đốc, Nhân viên kỹ thuật." };

            var loiGioiHan = await KiemTraGioiHanVaiTroAsync(dto.MaVaiTro);
            if (loiGioiHan != null)
                return new AuthResult { ThanhCong = false, Message = loiGioiHan };

            var taiKhoan = new QuanLyNguoiDung
            {
                Email = dto.Email.Trim().ToLowerInvariant(),
                MatKhau = BCrypt.Net.BCrypt.HashPassword(dto.MatKhau),
                MaVaiTro = dto.MaVaiTro,
                TrangThai = "Chưa kích hoạt"
            };
            await _userRepo.AddAsync(taiKhoan);
            await _userRepo.SaveChangesAsync();

            var hoTen = string.IsNullOrWhiteSpace(dto.HoTen) ? "Chưa cập nhật" : dto.HoTen.Trim();
            var nhanVien = new NhanVien
            {
                MaNguoiDung = taiKhoan.MaNguoiDung,
                HoTen = hoTen,
                SoDienThoai = dto.SoDienThoai,
                ChucVu = null,
                NgayVaoLam = DateOnly.FromDateTime(dto.NgayVaoLam ?? DateTime.Now),
                TrangThai = "Đang làm việc"
            };
            await _nhanVienRepo.AddAsync(nhanVien);
            await _nhanVienRepo.SaveChangesAsync();

            return new AuthResult { ThanhCong = true, Data = new { taiKhoan.MaNguoiDung, taiKhoan.Email, taiKhoan.TrangThai } };
        }

        // Admin chỉ được đổi vai trò — không sửa họ tên / email / chức vụ
        public async Task<AuthResult> CapNhatTaiKhoanAsync(int id, CapNhatTaiKhoanDto dto)
        {
            var user = await _userRepo.GetByIdAsync(id);
            if (user == null) return new AuthResult { ThanhCong = false, Message = "Không tìm thấy tài khoản." };

            var vaiTro = await _userRepo.GetVaiTroByIdAsync(dto.MaVaiTro);
            if (vaiTro == null)
                return new AuthResult { ThanhCong = false, Message = "Vai trò không hợp lệ." };
            if (!VaiTroChoPhep.Contains(vaiTro.TenVaiTro) ||
                string.Equals(vaiTro.TenVaiTro, "Admin hệ thống", StringComparison.OrdinalIgnoreCase))
                return new AuthResult { ThanhCong = false, Message = "Chỉ được gán: Tổ trưởng cơ điện, Xưởng, Giám đốc, Nhân viên kỹ thuật." };

            var loiGioiHan = await KiemTraGioiHanVaiTroAsync(dto.MaVaiTro, loaiTruMaNguoiDung: id);
            if (loiGioiHan != null)
                return new AuthResult { ThanhCong = false, Message = loiGioiHan };

            user.MaVaiTro = dto.MaVaiTro;
            _userRepo.Update(user);
            await _userRepo.SaveChangesAsync();
            return new AuthResult { ThanhCong = true, Message = "Đã cập nhật vai trò." };
        }

        public async Task<AuthResult> KichHoatAsync(int id)
        {
            var user = await _userRepo.GetByIdAsync(id);
            if (user == null) return new AuthResult { ThanhCong = false, Message = "Không tìm thấy tài khoản." };
            var loiGioiHan = await KiemTraGioiHanVaiTroAsync(user.MaVaiTro, loaiTruMaNguoiDung: id);
            if (loiGioiHan != null)
                return new AuthResult { ThanhCong = false, Message = loiGioiHan };
            user.TrangThai = "Đang hoạt động";
            _userRepo.Update(user);
            await _userRepo.SaveChangesAsync();
            return new AuthResult { ThanhCong = true, Message = "Đã kích hoạt tài khoản." };
        }

        public async Task<AuthResult> KhoaAsync(int id)
        {
            var user = await _userRepo.GetByIdAsync(id);
            if (user == null) return new AuthResult { ThanhCong = false, Message = "Không tìm thấy tài khoản." };
            user.TrangThai = "Đã khóa";
            _userRepo.Update(user);
            await _userRepo.SaveChangesAsync();
            return new AuthResult { ThanhCong = true, Message = "Đã khóa tài khoản." };
        }

        // Luồng 2 — Quên mật khẩu, bước Yêu cầu OTP
        public async Task<AuthResult> YeuCauOtpAsync(QuenMatKhauRequestDto dto)
        {
            var user = await _userRepo.GetByEmailAsync(dto.Email);
            if (user == null) return new AuthResult { ThanhCong = false, Message = "Không tìm thấy tài khoản." };

            // BẢO MẬT: OTP CHỈ gửi tới email cá nhân đã gắn trên hồ sơ NV.
            // Không bao giờ tin EmailNhanOtp từ client làm địa chỉ gửi (tránh chiếm tài khoản).
            var nhanVien = await _nhanVienRepo.GetByMaNguoiDungAsync(user.MaNguoiDung);
            var emailTrenHoSo = nhanVien?.Email?.Trim();
            if (!LaEmailCoTheNhanThu(emailTrenHoSo))
            {
                return new AuthResult
                {
                    ThanhCong = false,
                    Message = "Tài khoản chưa đăng ký email cá nhân nhận OTP trên hồ sơ. "
                              + "Đăng nhập (nếu còn) vào Hồ sơ cá nhân để cập nhật Gmail/Outlook, "
                              + "hoặc liên hệ Admin cập nhật email liên hệ — mỗi người một email riêng."
                };
            }

            // Nếu client gửi kèm email cá nhân: phải khớp 100% với hồ sơ (xác nhận thêm)
            if (!string.IsNullOrWhiteSpace(dto.EmailNhanOtp))
            {
                var nhap = dto.EmailNhanOtp.Trim();
                if (!string.Equals(nhap, emailTrenHoSo, StringComparison.OrdinalIgnoreCase))
                {
                    return new AuthResult
                    {
                        ThanhCong = false,
                        Message = "Email cá nhân không khớp với email đã đăng ký trên hồ sơ tài khoản này. "
                                  + "OTP chỉ gửi tới email đã gắn với đúng người dùng."
                    };
                }
            }

            var emailNhan = emailTrenHoSo!;

            // Chống spam — cách lần gửi gần nhất chưa đủ 1 phút thì chặn
            var otpGanNhat = await _otpRepo.GetMoiNhatAsync(user.MaNguoiDung);
            if (otpGanNhat != null && (DateTime.Now - otpGanNhat.NgayTao).TotalSeconds < SoPhutChoOtp * 60)
            {
                var conGiay = SoPhutChoOtp * 60 - (int)(DateTime.Now - otpGanNhat.NgayTao).TotalSeconds;
                if (conGiay < 1) conGiay = 1;
                return new AuthResult
                {
                    ThanhCong = false,
                    Message = $"Vui lòng chờ thêm {conGiay} giây trước khi gửi lại mã OTP."
                };
            }

            var otp = Random.Shared.Next(100000, 999999).ToString();
            await _otpRepo.AddAsync(new XacThucQuenMatKhau
            {
                MaNguoiDung = user.MaNguoiDung,
                MaOTP = otp,
                ThoiGianHetHan = DateTime.Now.AddMinutes(2), // OTP hiệu lực 2 phút
                TrangThaiXacThuc = "Chưa dùng",
                NgayTao = DateTime.Now
            });
            await _otpRepo.SaveChangesAsync();

            var subject = "OPC Maintenance — Mã OTP đặt lại mật khẩu";
            var bodyHtml =
                $"""
                <p>Xin chào,</p>
                <p>Mã OTP đặt lại mật khẩu của bạn là:</p>
                <p style="font-size:24px;font-weight:bold;letter-spacing:4px;">{otp}</p>
                <p>Mã có hiệu lực <b>5 phút</b>. Không chia sẻ mã này cho任何人.</p>
                <p>Nếu bạn không yêu cầu, hãy bỏ qua email này.</p>
                <p>— Hệ thống OPC Maintenance</p>
                """;
            var bodyText = $"Mã OTP đặt lại mật khẩu: {otp}. Hiệu lực 5 phút.";

            var loiGui = await _emailSender.SendAsync(emailNhan, subject, bodyHtml, bodyText);
            if (loiGui != null)
            {
                // Dev: nếu chưa cấu hình SMTP vẫn cho test bằng OtpTest
                var choPhepDev = string.Equals(_config["Email:DevShowOtp"], "true", StringComparison.OrdinalIgnoreCase)
                                 || !_emailSender.IsConfigured;
                if (choPhepDev)
                {
                    return new AuthResult
                    {
                        ThanhCong = true,
                        Message = "Chưa gửi được email (SMTP). Mã OTP hiển thị tạm để dev/test.",
                        Data = new { OtpTest = otp, EmailNhan = emailNhan, CanhBao = loiGui }
                    };
                }
                return new AuthResult { ThanhCong = false, Message = loiGui };
            }

            var emailAn = AnEmail(emailNhan);
            return new AuthResult
            {
                ThanhCong = true,
                Message = $"Đã gửi mã OTP tới {emailAn}. Mã có hiệu lực 2 phút. Vui lòng kiểm tra hộp thư (và Spam).",
                Data = new
                {
                    EmailNhanAn = emailAn,
                    HetHanSauGiay = 120,
                    GuiLaiSauGiay = 60
                }
            };
        }

        /// <summary>Chọn email nhận OTP: ưu tiên email NV thật, không nhận @opc.com ảo nếu không phải inbox thật.</summary>
        private static string? ChonEmailNhanOtp(string emailDangNhap, string? emailNhanVien)
        {
            if (LaEmailCoTheNhanThu(emailNhanVien))
                return emailNhanVien!.Trim();
            if (LaEmailCoTheNhanThu(emailDangNhap))
                return emailDangNhap.Trim();
            return null;
        }

        private static bool LaEmailCoTheNhanThu(string? email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return false;
            var e = email.Trim().ToLowerInvariant();
            // Chỉ Gmail nhận OTP (theo ràng buộc hồ sơ)
            return e.EndsWith("@gmail.com")
                   && System.Text.RegularExpressions.Regex.IsMatch(
                       e, @"^[\w.+\-]+@gmail\.com$");
        }

        private static string AnEmail(string email)
        {
            var at = email.IndexOf('@');
            if (at <= 1) return "***";
            return email[0] + "***" + email[(at - 1)..];
        }

        public async Task<AuthResult> XacNhanOtpAsync(XacNhanOtpDto dto)
        {
            var user = await _userRepo.GetByEmailAsync(dto.Email);
            if (user == null) return new AuthResult { ThanhCong = false, Message = "Không tìm thấy tài khoản." };

            var xacThuc = await _otpRepo.GetHopLeAsync(user.MaNguoiDung, dto.MaOTP);
            if (xacThuc == null) return new AuthResult { ThanhCong = false, Message = "Mã OTP không đúng." };

            if (xacThuc.ThoiGianHetHan < DateTime.Now)
            {
                xacThuc.TrangThaiXacThuc = "Hết hạn";
                _otpRepo.Update(xacThuc);
                await _otpRepo.SaveChangesAsync();
                return new AuthResult { ThanhCong = false, Message = "Mã OTP đã hết hạn." };
            }

            xacThuc.TrangThaiXacThuc = "Đã xác nhận";
            _otpRepo.Update(xacThuc);
            await _otpRepo.SaveChangesAsync();

            return new AuthResult { ThanhCong = true, Message = "Xác nhận thành công." };
        }

        public async Task<AuthResult> DatLaiMatKhauAsync(DatLaiMatKhauDto dto)
        {
            if (!MatKhauHopLe.IsMatch(dto.MatKhauMoi))
                return new AuthResult { ThanhCong = false, Message = "Mật khẩu phải có ít nhất 8 ký tự, gồm 1 chữ số và 1 ký tự đặc biệt." };

            var user = await _userRepo.GetByEmailAsync(dto.Email);
            if (user == null) return new AuthResult { ThanhCong = false, Message = "Không tìm thấy tài khoản." };

            var xacThuc = await _otpRepo.GetDaXacNhanAsync(user.MaNguoiDung);
            if (xacThuc == null) return new AuthResult { ThanhCong = false, Message = "Chưa xác nhận OTP." };

            user.MatKhau = BCrypt.Net.BCrypt.HashPassword(dto.MatKhauMoi);
            _userRepo.Update(user);
            await _userRepo.SaveChangesAsync();

            xacThuc.TrangThaiXacThuc = "Đã dùng";
            _otpRepo.Update(xacThuc);
            await _otpRepo.SaveChangesAsync();

            return new AuthResult { ThanhCong = true, Message = "Đổi mật khẩu thành công." };
        }


        public async Task<AuthResult> DoiMatKhauAsync(int maNguoiDung, DoiMatKhauDto dto)
        {
            var user = await _userRepo.GetByIdAsync(maNguoiDung);
            if (user == null)
                return new AuthResult { ThanhCong = false, Message = "Không tìm thấy tài khoản." };

            if (string.IsNullOrEmpty(dto.MatKhauCu) || !BCrypt.Net.BCrypt.Verify(dto.MatKhauCu, user.MatKhau))
                return new AuthResult { ThanhCong = false, Message = "Mật khẩu hiện tại không đúng." };

            if (!MatKhauHopLe.IsMatch(dto.MatKhauMoi ?? ""))
                return new AuthResult { ThanhCong = false, Message = "Mật khẩu mới phải có ít nhất 8 ký tự, gồm 1 chữ số và 1 ký tự đặc biệt." };

            if (dto.MatKhauCu == dto.MatKhauMoi)
                return new AuthResult { ThanhCong = false, Message = "Mật khẩu mới phải khác mật khẩu hiện tại." };

            user.MatKhau = BCrypt.Net.BCrypt.HashPassword(dto.MatKhauMoi);
            _userRepo.Update(user);
            await _userRepo.SaveChangesAsync();
            return new AuthResult { ThanhCong = true, Message = "Đổi mật khẩu thành công." };
        }

        public async Task<AuthResult> GetHoSoCaNhanAsync(int maNguoiDung)
        {
            var u = await _userRepo.GetByIdAsync(maNguoiDung);
            if (u == null)
                return new AuthResult { ThanhCong = false, Message = "Không tìm thấy tài khoản." };

            var nv = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            var vaiTro = await _userRepo.GetVaiTroByIdAsync(u.MaVaiTro);
            return new AuthResult
            {
                ThanhCong = true,
                Data = new
                {
                    u.MaNguoiDung,
                    u.Email,
                    TenVaiTro = vaiTro?.TenVaiTro,
                    u.MaVaiTro,
                    HoTen = nv?.HoTen ?? u.Email,
                    SoDienThoai = nv?.SoDienThoai,
                    EmailLienHe = nv?.Email,
                    ChucVu = nv?.ChucVu,
                    NgayVaoLam = nv?.NgayVaoLam,
                    TrangThaiNv = nv?.TrangThai,
                    TrangThaiTk = u.TrangThai
                }
            };
        }

        public async Task<AuthResult> CapNhatHoSoCaNhanAsync(int maNguoiDung, NhanVienUpdateDto dto)
        {
            var u = await _userRepo.GetByIdAsync(maNguoiDung);
            if (u == null)
                return new AuthResult { ThanhCong = false, Message = "Không tìm thấy tài khoản." };

            var loiHoTen = KiemTraHoTen(dto.HoTen, batBuoc: true);
            if (loiHoTen != null)
                return new AuthResult { ThanhCong = false, Message = loiHoTen };

            var sdt = (dto.SoDienThoai ?? "").Trim();
            // Chỉ chữ số, tối đa 10 số — không chữ / ký tự đặc biệt / thập phân / số âm
            if (sdt.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(sdt, @"^\d{1,10}$"))
                return new AuthResult { ThanhCong = false, Message = "Số điện thoại chỉ gồm số, tối đa 10 chữ số." };

            string? emailLienHe = null;
            if (!string.IsNullOrWhiteSpace(dto.EmailLienHe))
            {
                emailLienHe = dto.EmailLienHe.Trim();
                // Chỉ chấp nhận @gmail.com (khớp app Flutter)
                if (!System.Text.RegularExpressions.Regex.IsMatch(
                        emailLienHe,
                        @"^[\w.+\-]+@gmail\.com$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return new AuthResult
                    {
                        ThanhCong = false,
                        Message = "Email nhận OTP phải đúng định dạng …@gmail.com."
                    };
            }

            var nv = await _nhanVienRepo.GetByMaNguoiDungAsync(maNguoiDung);
            if (nv == null)
            {
                nv = new NhanVien
                {
                    MaNguoiDung = maNguoiDung,
                    HoTen = dto.HoTen.Trim(),
                    SoDienThoai = string.IsNullOrWhiteSpace(sdt) ? null : sdt,
                    Email = emailLienHe,
                    NgayVaoLam = dto.NgayVaoLam,
                    TrangThai = "Đang làm việc"
                };
                await _nhanVienRepo.AddAsync(nv);
            }
            else
            {
                nv.HoTen = dto.HoTen.Trim();
                nv.SoDienThoai = string.IsNullOrWhiteSpace(sdt) ? null : sdt;
                if (dto.EmailLienHe != null)
                    nv.Email = emailLienHe; // null hoặc email thật
                if (dto.NgayVaoLam.HasValue)
                    nv.NgayVaoLam = dto.NgayVaoLam;
                _nhanVienRepo.Update(nv);
            }

            await _nhanVienRepo.SaveChangesAsync();
            var vaiTro = await _userRepo.GetVaiTroByIdAsync(u.MaVaiTro);
            return new AuthResult
            {
                ThanhCong = true,
                Message = "Đã cập nhật thông tin cá nhân.",
                Data = new
                {
                    u.MaNguoiDung,
                    u.Email,
                    TenVaiTro = vaiTro?.TenVaiTro,
                    HoTen = nv.HoTen,
                    SoDienThoai = nv.SoDienThoai,
                    EmailLienHe = nv.Email,
                    NgayVaoLam = nv.NgayVaoLam
                }
            };
        }

        // Điều kiện: MaVaiTro là Giám đốc hoặc Phó giám đốc thì chỉ được tối đa 1 tài khoản
        // (không tính tài khoản đã "Đã khóa" — xem DemTaiKhoanTheoVaiTroAsync)
        private async Task<string?> KiemTraGioiHanVaiTroAsync(int maVaiTro, int? loaiTruMaNguoiDung = null)
        {
            var vaiTro = await _userRepo.GetVaiTroByIdAsync(maVaiTro);
            if (vaiTro == null) return "Vai trò không tồn tại.";

            if (vaiTro.TenVaiTro == VaiTroGiamDoc || vaiTro.TenVaiTro == VaiTroPhoGiamDoc)
            {
                var soLuong = await _userRepo.DemTaiKhoanTheoVaiTroAsync(maVaiTro, loaiTruMaNguoiDung);
                if (soLuong >= 1)
                    return $"Đã có tài khoản giữ vai trò {vaiTro.TenVaiTro}, không thể tạo thêm.";
            }
            return null;
        }
    }
}