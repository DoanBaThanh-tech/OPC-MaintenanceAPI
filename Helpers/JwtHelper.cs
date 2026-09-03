using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace OPC.MaintenanceAPI.Helpers
{
    public static class JwtHelper
    {
        public static string TaoToken(int maNguoiDung, string email, int maVaiTro, string tenVaiTro, IConfiguration config)
        {
            var claims = new[]
            {
                new Claim("MaNguoiDung", maNguoiDung.ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim("MaVaiTro", maVaiTro.ToString()),
                new Claim(ClaimTypes.Role, tenVaiTro)   // dùng cho [Authorize(Roles = "...")]
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expireHours = double.Parse(config["Jwt:ExpireHours"]!);

            var token = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddHours(expireHours),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}