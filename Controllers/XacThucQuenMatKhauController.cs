using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using OPC.MaintenanceAPI.Data;
using OPC.MaintenanceAPI.Models;
using OPC.MaintenanceAPI.DTOs.XacThucQuenMatKhau;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class XacThucQuenMatKhauController : ControllerBase
    {
        private readonly OPCDbContext _context;
        public XacThucQuenMatKhauController(OPCDbContext context) => _context = context;

        private static readonly Regex MatKhauHopLe =
            new(@"^(?=.*[A-Z])(?=.*[!@#$%^&*(),.?"":{}|<>_\-]).{8,}$");

        // POST: api/XacThucQuenMatKhau/yeu-cau
        [HttpPost("yeu-cau")]
        public async Task<IActionResult> YeuCauOtp([FromBody] QuenMatKhauRequestDto dto)
        {
            var user = await _context.QuanLyNguoiDungs.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null)
                return NotFound(new { Message = "Không tìm thấy tài khoản với email này." });

            var otp = new Random().Next(100000, 999999).ToString();

            var xacThuc = new XacThucQuenMatKhau
            {
                MaNguoiDung = user.MaNguoiDung,
                MaOTP = otp,
                ThoiGianHetHan = DateTime.Now.AddMinutes(5),
                TrangThaiXacThuc = "Chưa dùng"
            };
            _context.XacThucQuenMatKhaus.Add(xacThuc);
            await _context.SaveChangesAsync();

            // TODO: gửi OTP qua email thật khi có dịch vụ email
            return Ok(new { Message = "Đã gửi mã OTP tới email.", OtpTest = otp });
        }

        // POST: api/XacThucQuenMatKhau/xac-nhan
        [HttpPost("xac-nhan")]
        public async Task<IActionResult> XacNhanOtp([FromBody] XacNhanOtpDto dto)
        {
            var user = await _context.QuanLyNguoiDungs.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null)
                return NotFound(new { Message = "Không tìm thấy tài khoản." });

            var xacThuc = await _context.XacThucQuenMatKhaus
                .Where(x => x.MaNguoiDung == user.MaNguoiDung && x.MaOTP == dto.MaOTP
                         && x.TrangThaiXacThuc == "Chưa dùng")
                .OrderByDescending(x => x.MaXacThuc)
                .FirstOrDefaultAsync();

            if (xacThuc == null)
                return BadRequest(new { Message = "Mã OTP không đúng." });

            if (xacThuc.ThoiGianHetHan < DateTime.Now)
            {
                xacThuc.TrangThaiXacThuc = "Hết hạn";
                await _context.SaveChangesAsync();
                return BadRequest(new { Message = "Mã OTP đã hết hạn." });
            }

            xacThuc.TrangThaiXacThuc = "Đã xác nhận";
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Xác nhận thành công, mời nhập mật khẩu mới." });
        }

        // POST: api/XacThucQuenMatKhau/dat-lai-mat-khau
        [HttpPost("dat-lai-mat-khau")]
        public async Task<IActionResult> DatLaiMatKhau([FromBody] DatLaiMatKhauDto dto)
        {
            if (!MatKhauHopLe.IsMatch(dto.MatKhauMoi))
                return BadRequest(new { Message = "Mật khẩu phải có ít nhất 8 ký tự, gồm 1 chữ in hoa và 1 ký tự đặc biệt." });

            var user = await _context.QuanLyNguoiDungs.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null)
                return NotFound(new { Message = "Không tìm thấy tài khoản." });

            var xacThuc = await _context.XacThucQuenMatKhaus
                .Where(x => x.MaNguoiDung == user.MaNguoiDung && x.TrangThaiXacThuc == "Đã xác nhận")
                .OrderByDescending(x => x.MaXacThuc)
                .FirstOrDefaultAsync();

            if (xacThuc == null)
                return BadRequest(new { Message = "Chưa xác nhận OTP hoặc phiên xác nhận đã hết hạn." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                user.MatKhau = BCrypt.Net.BCrypt.HashPassword(dto.MatKhauMoi);
                xacThuc.TrangThaiXacThuc = "Đã dùng";
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { Message = "Đổi mật khẩu thành công." });
            }
            catch
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { Message = "Đổi mật khẩu thất bại." });
            }
        }
    }
}