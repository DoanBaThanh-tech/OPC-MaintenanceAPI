using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class ThemMaHoSoBaoTriChoPhanCongVaLuongXuong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PhanCong_HoSoBaoTri",
                table: "PhanCongCongViec");

            migrationBuilder.RenameIndex(
                name: "IX_PhanCong_MaHoSoBaoTri",
                table: "PhanCongCongViec",
                newName: "IX_PhanCongCongViec_MaHoSoBaoTri");

            migrationBuilder.AddForeignKey(
                name: "FK_PhanCong_HoSoBaoTri_Multi",
                table: "PhanCongCongViec",
                column: "MaHoSoBaoTri",
                principalTable: "HoSoBaoTri",
                principalColumn: "MaHoSoBaoTri");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PhanCong_HoSoBaoTri_Multi",
                table: "PhanCongCongViec");

            migrationBuilder.RenameIndex(
                name: "IX_PhanCongCongViec_MaHoSoBaoTri",
                table: "PhanCongCongViec",
                newName: "IX_PhanCong_MaHoSoBaoTri");

            migrationBuilder.AddForeignKey(
                name: "FK_PhanCong_HoSoBaoTri",
                table: "PhanCongCongViec",
                column: "MaHoSoBaoTri",
                principalTable: "HoSoBaoTri",
                principalColumn: "MaHoSoBaoTri");
        }
    }
}
