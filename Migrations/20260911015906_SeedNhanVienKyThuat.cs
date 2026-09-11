using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class SeedNhanVienKyThuat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cập nhật thông tin còn thiếu cho nhân viên cũ
            migrationBuilder.Sql(@"
                UPDATE NhanVien SET 
                    Email = N'nhanvien1@opc.com.vn',
                    ChucVu = N'Nhân viên kỹ thuật',
                    TrangThai = N'Đang làm việc'
                WHERE MaNhanVien = 4 AND (Email IS NULL OR ChucVu IS NULL OR TrangThai IS NULL);

                UPDATE NhanVien SET 
                    Email = N'test@opc.com.vn',
                    ChucVu = N'Nhân viên kỹ thuật',
                    TrangThai = N'Đang làm việc'
                WHERE MaNhanVien = 6 AND (Email IS NULL OR ChucVu IS NULL OR TrangThai IS NULL);

                UPDATE NhanVien SET 
                    Email = N'nguyenvanb@opc.com.vn',
                    ChucVu = N'Nhân viên kỹ thuật',
                    TrangThai = N'Đang làm việc'
                WHERE MaNhanVien = 7 AND (Email IS NULL OR ChucVu IS NULL OR TrangThai IS NULL);
            ");

            // Cập nhật 3 nhân viên kỹ thuật mới
            migrationBuilder.Sql(@"
                UPDATE NhanVien SET 
                    HoTen = N'Phạm Văn Kỹ 1',
                    Email = N'nvkt1@opc.com',
                    SoDienThoai = N'0901000004',
                    ChucVu = N'Nhân viên kỹ thuật',
                    TrangThai = N'Đang làm việc'
                WHERE MaNguoiDung = (SELECT TOP 1 MaNguoiDung FROM QuanLyNguoiDung WHERE Email = N'nvkt1@opc.com');

                UPDATE NhanVien SET 
                    HoTen = N'Hoàng Thị Kỹ 2',
                    Email = N'nvkt2@opc.com',
                    SoDienThoai = N'0901000005',
                    ChucVu = N'Nhân viên kỹ thuật',
                    TrangThai = N'Đang làm việc'
                WHERE MaNguoiDung = (SELECT TOP 1 MaNguoiDung FROM QuanLyNguoiDung WHERE Email = N'nvkt2@opc.com');

                UPDATE NhanVien SET 
                    HoTen = N'Võ Minh Kỹ 3',
                    Email = N'nvkt3@opc.com',
                    SoDienThoai = N'0901000006',
                    ChucVu = N'Nhân viên kỹ thuật',
                    TrangThai = N'Đang làm việc'
                WHERE MaNguoiDung = (SELECT TOP 1 MaNguoiDung FROM QuanLyNguoiDung WHERE Email = N'nvkt3@opc.com');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
