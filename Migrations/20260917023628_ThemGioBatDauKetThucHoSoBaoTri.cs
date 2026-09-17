using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class ThemGioBatDauKetThucHoSoBaoTri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "GioBatDauDuKien",
                table: "HoSoBaoTri",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "GioKetThucDuKien",
                table: "HoSoBaoTri",
                type: "time",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GioBatDauDuKien",
                table: "HoSoBaoTri");

            migrationBuilder.DropColumn(
                name: "GioKetThucDuKien",
                table: "HoSoBaoTri");
        }
    }
}
