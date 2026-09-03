using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class DamBaoTrangThaiLaChuoi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TrangThai",
                table: "QuanLyNguoiDung",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Chưa kích hoạt",
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "TrangThai",
                table: "QuanLyNguoiDung",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30,
                oldDefaultValue: "Chưa kích hoạt");
        }
    }
}
