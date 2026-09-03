using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class DoiTenDangNhapThanhEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TenDangNhap",
                table: "QuanLyNguoiDung",
                newName: "Email");

            migrationBuilder.RenameIndex(
                name: "UQ__QuanLyNg__55F68FC03C332257",
                table: "QuanLyNguoiDung",
                newName: "UQ_QuanLyNguoiDung_Email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Email",
                table: "QuanLyNguoiDung",
                newName: "TenDangNhap");

            migrationBuilder.RenameIndex(
                name: "UQ_QuanLyNguoiDung_Email",
                table: "QuanLyNguoiDung",
                newName: "UQ__QuanLyNg__55F68FC03C332257");
        }
    }
}
