namespace OPC.MaintenanceAPI.Core.Exceptions
{
    /// Ném khi vi phạm quy tắc nghiệp vụ (VD: xoá vai trò đang có người dùng) - Middleware bắt và trả về 400
    public class BusinessRuleException : Exception
    {
        public BusinessRuleException(string message) : base(message) { }
    }
}