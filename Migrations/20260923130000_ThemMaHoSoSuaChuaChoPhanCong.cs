using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class ThemMaHoSoSuaChuaChoPhanCong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaHoSoSuaChua",
                table: "PhanCongCongViec",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhanCongCongViec_MaHoSoSuaChua",
                table: "PhanCongCongViec",
                column: "MaHoSoSuaChua");

            migrationBuilder.AddForeignKey(
                name: "FK_PhanCong_HoSoSuaChua_Multi",
                table: "PhanCongCongViec",
                column: "MaHoSoSuaChua",
                principalTable: "HoSoSuaChua",
                principalColumn: "MaHoSoSuaChua");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PhanCong_HoSoSuaChua_Multi",
                table: "PhanCongCongViec");

            migrationBuilder.DropIndex(
                name: "IX_PhanCongCongViec_MaHoSoSuaChua",
                table: "PhanCongCongViec");

            migrationBuilder.DropColumn(
                name: "MaHoSoSuaChua",
                table: "PhanCongCongViec");
        }
    }
}
