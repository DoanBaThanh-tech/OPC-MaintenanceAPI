using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class ThemBangXacThucQuenMatKhau : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "XacThucQuenMatKhau",
                columns: table => new
                {
                    MaXacThuc = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaNguoiDung = table.Column<int>(type: "int", nullable: false),
                    MaOTP = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    ThoiGianHetHan = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TrangThaiXacThuc = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XacThucQuenMatKhau", x => x.MaXacThuc);
                    table.ForeignKey(
                        name: "FK_XacThuc_NguoiDung",
                        column: x => x.MaNguoiDung,
                        principalTable: "QuanLyNguoiDung",
                        principalColumn: "MaNguoiDung");
                });

            migrationBuilder.CreateIndex(
                name: "IX_XacThucQuenMatKhau_MaNguoiDung",
                table: "XacThucQuenMatKhau",
                column: "MaNguoiDung");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XacThucQuenMatKhau");
        }
    }
}
