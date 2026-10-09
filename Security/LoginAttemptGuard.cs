using System.Collections.Concurrent;

namespace OPC.MaintenanceAPI.Security
{
    /// <summary>
    /// Chống brute-force đăng nhập (in-memory).
    /// Sau MaxFails lần sai → khóa tạm LockMinutes phút theo email (không phân biệt hoa thường).
    /// </summary>
    public sealed class LoginAttemptGuard
    {
        public const int MaxFails = 5;
        public const int LockMinutes = 15;

        private sealed class AttemptState
        {
            public int FailCount;
            public DateTime? LockedUntil;
        }

        private readonly ConcurrentDictionary<string, AttemptState> _map = new(StringComparer.OrdinalIgnoreCase);

        public bool IsLocked(string email, out TimeSpan conLai)
        {
            conLai = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(email)) return false;
            if (!_map.TryGetValue(email.Trim(), out var st) || st.LockedUntil == null)
                return false;
            if (st.LockedUntil > DateTime.UtcNow)
            {
                conLai = st.LockedUntil.Value - DateTime.UtcNow;
                return true;
            }
            // Hết hạn khóa → reset
            st.LockedUntil = null;
            st.FailCount = 0;
            return false;
        }

        public void RegisterFailure(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return;
            var key = email.Trim();
            var st = _map.GetOrAdd(key, _ => new AttemptState());
            lock (st)
            {
                st.FailCount++;
                if (st.FailCount >= MaxFails)
                    st.LockedUntil = DateTime.UtcNow.AddMinutes(LockMinutes);
            }
        }

        public void RegisterSuccess(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return;
            _map.TryRemove(email.Trim(), out _);
        }
    }
}
