using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class ThemLyDoTuChoiPhanCong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
            name: "LyDoTuChoi",
            table: "PhanCongCongViec",
            type: "nvarchar(255)",
            maxLength: 255,
            nullable: true);    
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
            name: "LyDoTuChoi",
            table: "PhanCongCongViec");
        }
    }
}
