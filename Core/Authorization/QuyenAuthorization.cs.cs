using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using OPC.MaintenanceAPI.Repositories.Specific;

namespace OPC.MaintenanceAPI.Core.Authorization
{
    // ===== 1. Requirement: mô tả "cần quyền gì" =====
    public class QuyenRequirement : IAuthorizationRequirement
    {
        public string TenChucNang { get; }
        public string LoaiQuyen { get; }   // "Xem" / "Tao" / "Sua" / "Duyet"

        public QuyenRequirement(string tenChucNang, string loaiQuyen)
        {
            TenChucNang = tenChucNang;
            LoaiQuyen = loaiQuyen;
        }
    }

    // ===== 2. Attribute: gắn lên Controller, ví dụ [CoQuyen("Quản lý hồ sơ bảo trì", "Duyet")] =====
    public class CoQuyenAttribute : AuthorizeAttribute
    {
        public CoQuyenAttribute(string tenChucNang, string loaiQuyen)
        {
            // Policy name dạng "CoQuyen:<TenChucNang>:<LoaiQuyen>",
            // QuyenPolicyProvider bên dưới sẽ tách chuỗi này ra để tạo Requirement lúc runtime
            Policy = $"CoQuyen:{tenChucNang}:{loaiQuyen}";
        }
    }

    // ===== 3. Policy Provider: sinh Policy động, không cần khai tay từng cái trong Program.cs =====
    public class QuyenPolicyProvider : IAuthorizationPolicyProvider
    {
        private readonly DefaultAuthorizationPolicyProvider _fallback;
        public QuyenPolicyProvider(IOptions<AuthorizationOptions> options)
        {
            _fallback = new DefaultAuthorizationPolicyProvider(options);
        }

        public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (policyName.StartsWith("CoQuyen:"))
            {
                var parts = policyName.Split(':');
                if (parts.Length == 3)
                {
                    var tenChucNang = parts[1];
                    var loaiQuyen = parts[2];

                    var policy = new AuthorizationPolicyBuilder();
                    policy.AddRequirements(new QuyenRequirement(tenChucNang, loaiQuyen));
                    return policy.Build();
                }
            }
            return await _fallback.GetPolicyAsync(policyName);
        }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
    }

    // ===== 4. Handler: nơi thật sự tra bảng PhanQuyenVaiTro để quyết định cho phép hay không =====
    public class QuyenAuthorizationHandler : AuthorizationHandler<QuyenRequirement>
    {
        private readonly IQuanLyNguoiDungRepository _userRepo;
        public QuyenAuthorizationHandler(IQuanLyNguoiDungRepository userRepo) => _userRepo = userRepo;

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context, QuyenRequirement requirement)
        {
            var claim = context.User.FindFirst("MaNguoiDung")?.Value;
            if (claim == null || !int.TryParse(claim, out var maNguoiDung))
            {
                context.Fail();
                return;
            }

            var coQuyen = await _userRepo.CoQuyenAsync(maNguoiDung, requirement.TenChucNang, requirement.LoaiQuyen);
            if (coQuyen)
                context.Succeed(requirement);
            else
                context.Fail();
        }
    }
}