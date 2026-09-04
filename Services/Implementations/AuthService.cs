using System.Text.RegularExpressions;
using OPC.MaintenanceAPI.Core.Entities;
using OPC.MaintenanceAPI.DTOs.Auth;
using OPC.MaintenanceAPI.Helpers;
using OPC.MaintenanceAPI.Repositories.Specific;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Services.Implementations
{
    public class AuthService : IAuthService 
    {
        private readonly IQuanLyNguoiDungRepository _userRepo;
        private readonly INhanVienRepository _nhanVienRepo;
        private readonly IXacThucQuenMatKhauRepository _otpRepo;
        private readonly IConfiguration _config;

        private static readonly Regex MatKhauHopLe =
            new(@"^(?=.*[A-Z])(?=.*[!@#$%^&*(),.?"":{}|<>_\-]).{8,}$");

        public AuthService(IQuanLyNguoiDungRepository userRepo, INhanVienRepository nhanVienRepo,
                            IXacThucQuenMatKhauRepository otpRepo, IConfiguration config)
        {
            _userRepo = userRepo;
            _nhanVienRepo = nhanVienRepo;
            _otpRepo = otpRepo;
            _config = config;
        }

        public async Task<List<object>> GetAllAsync()
        {
            var list = await _userRepo.GetAllAsync();
            return list.Select(u => (object)new
            {
                u.MaNguoiDung, u.Email, u.MaVaiTro,
                TenVaiTro = u.MaVaiTroNavigation?.TenVaiTro,
                u.TrangThai, u.LanDangNhapCuoi, u.NgayTao
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
            var user = await _userRepo.GetByEmailAsync(dto.Email);
            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.MatKhau, user.MatKhau))
                return new AuthResult { ThanhCong = false, Message = "Email hoặc mật khẩu không đúng." };

            if (user.TrangThai == "Đã khóa")
                return new AuthResult { ThanhCong = false, Message = "Tài khoản đã bị khóa." };
            if (user.TrangThai == "Chưa kích hoạt")
                return new AuthResult { ThanhCong = false, Message = "Tài khoản chưa được kích hoạt." };

            user.LanDangNhapCuoi = DateTime.Now;
            _userRepo.Update(user);
            await _userRepo.SaveChangesAsync();

            var token = JwtHelper.TaoToken(user.MaNguoiDung, user.Email, user.MaVaiTro,
                user.MaVaiTroNavigation!.TenVaiTro, _config);

            return new AuthResult
            {
                ThanhCong = true,
                Data = new { user.MaNguoiDung, user.Email, VaiTro = user.MaVaiTroNavigation.TenVaiTro, user.MaVaiTro, Token = token }
            };
        }

        public async Task<AuthResult> TaoTaiKhoanAsync(TaoTaiKhoanDto dto)
        {
            if (!MatKhauHopLe.IsMatch(dto.MatKhau))
                return new AuthResult { ThanhCong = false, Message = "Mật khẩu phải có ít nhất 8 ký tự, gồm 1 chữ hoa và 1 ký tự đặc biệt." };

            if (await _userRepo.EmailExistsAsync(dto.Email))
                return new AuthResult { ThanhCong = false, Message = "Email đã tồn tại." };

            var taiKhoan = new QuanLyNguoiDung
            {
                Email = dto.Email,
                MatKhau = BCrypt.Net.BCrypt.HashPassword(dto.MatKhau),
                MaVaiTro = dto.MaVaiTro,
                TrangThai = "Chưa kích hoạt"
            };
            await _userRepo.AddAsync(taiKhoan);
            await _userRepo.SaveChangesAsync();

            var nhanVien = new NhanVien
            {
                MaNguoiDung = taiKhoan.MaNguoiDung,
                HoTen = dto.HoTen,
                SoDienThoai = dto.SoDienThoai,
                ChucVu = dto.ChucVu,
                NgayVaoLam = DateOnly.FromDateTime(dto.NgayVaoLam ?? DateTime.Now),
                TrangThai = "Đang làm việc"
            };
            await _nhanVienRepo.AddAsync(nhanVien);
            await _nhanVienRepo.SaveChangesAsync();

            return new AuthResult { ThanhCong = true, Data = new { taiKhoan.MaNguoiDung, taiKhoan.Email, taiKhoan.TrangThai } };
        }

        public async Task<AuthResult> KichHoatAsync(int id)
        {
            var user = await _userRepo.GetByIdAsync(id);
            if (user == null) return new AuthResult { ThanhCong = false, Message = "Không tìm thấy tài khoản." };
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

        public async Task<AuthResult> YeuCauOtpAsync(QuenMatKhauRequestDto dto)
        {
            var user = await _userRepo.GetByEmailAsync(dto.Email);
            if (user == null) return new AuthResult { ThanhCong = false, Message = "Không tìm thấy tài khoản." };

            var otp = new Random().Next(100000, 999999).ToString();
            await _otpRepo.AddAsync(new XacThucQuenMatKhau
            {
                MaNguoiDung = user.MaNguoiDung,
                MaOTP = otp,
                ThoiGianHetHan = DateTime.Now.AddMinutes(5),
                TrangThaiXacThuc = "Chưa dùng"
            });
            await _otpRepo.SaveChangesAsync();

            return new AuthResult { ThanhCong = true, Message = "Đã gửi mã OTP.", Data = new { OtpTest = otp } };
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
                return new AuthResult { ThanhCong = false, Message = "Mật khẩu phải có ít nhất 8 ký tự, gồm 1 chữ hoa và 1 ký tự đặc biệt." };

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
    }
}