using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class PhanCongNhieuNhanVien : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaHoSoBaoTri",
                table: "PhanCongCongViec",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhanCong_MaHoSoBaoTri",
                table: "PhanCongCongViec",
                column: "MaHoSoBaoTri");

            migrationBuilder.AddForeignKey(
                name: "FK_PhanCong_HoSoBaoTri",
                table: "PhanCongCongViec",
                column: "MaHoSoBaoTri",
                principalTable: "HoSoBaoTri",
                principalColumn: "MaHoSoBaoTri");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PhanCong_HoSoBaoTri",
                table: "PhanCongCongViec");

            migrationBuilder.DropIndex(
                name: "IX_PhanCong_MaHoSoBaoTri",
                table: "PhanCongCongViec");

            migrationBuilder.DropColumn(
                name: "MaHoSoBaoTri",
                table: "PhanCongCongViec");
        }
    }
}
