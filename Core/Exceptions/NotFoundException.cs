namespace OPC.MaintenanceAPI.Core.Exceptions
{
    /// Ném khi không tìm thấy bản ghi - Middleware bắt và trả về 404
    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
    }
}