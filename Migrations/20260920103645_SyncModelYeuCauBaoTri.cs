using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelYeuCauBaoTri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "LyDoTuChoi",
                table: "PhanCongCongViec",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaYeuCauBaoTri",
                table: "HoSoBaoTri",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "YeuCauBaoTriThietBi",
                columns: table => new
                {
                    MaYeuCauBaoTri = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaThietBi = table.Column<int>(type: "int", nullable: false),
                    MaNhanVienYeuCau = table.Column<int>(type: "int", nullable: false),
                    MaNhanVienXacNhan = table.Column<int>(type: "int", nullable: true),
                    ThangBaoTri = table.Column<int>(type: "int", nullable: false),
                    NamBaoTri = table.Column<int>(type: "int", nullable: false),
                    NgayBaoTri = table.Column<DateOnly>(type: "date", nullable: false),
                    ThoiGianDuKien = table.Column<decimal>(type: "decimal(5,1)", nullable: false),
                    GioBatDau = table.Column<TimeSpan>(type: "time", nullable: false),
                    GioKetThuc = table.Column<TimeSpan>(type: "time", nullable: false),
                    TrangThai = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Chờ xác nhận"),
                    LyDoTuChoi = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    GhiChu = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NgayTao = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    NgayXacNhan = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YeuCauBaoTriThietBi", x => x.MaYeuCauBaoTri);
                    table.ForeignKey(
                        name: "FK_YeuCauBaoTri_NVXacNhan",
                        column: x => x.MaNhanVienXacNhan,
                        principalTable: "NhanVien",
                        principalColumn: "MaNhanVien");
                    table.ForeignKey(
                        name: "FK_YeuCauBaoTri_NVYeuCau",
                        column: x => x.MaNhanVienYeuCau,
                        principalTable: "NhanVien",
                        principalColumn: "MaNhanVien");
                    table.ForeignKey(
                        name: "FK_YeuCauBaoTri_ThietBi",
                        column: x => x.MaThietBi,
                        principalTable: "ThietBi",
                        principalColumn: "MaThietBi");
                });

            migrationBuilder.CreateIndex(
                name: "IX_HoSoBaoTri_MaYeuCauBaoTri",
                table: "HoSoBaoTri",
                column: "MaYeuCauBaoTri",
                unique: true,
                filter: "[MaYeuCauBaoTri] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_YeuCauBaoTri_ThietBi_Nam_Thang",
                table: "YeuCauBaoTriThietBi",
                columns: new[] { "MaThietBi", "NamBaoTri", "ThangBaoTri" });

            migrationBuilder.CreateIndex(
                name: "IX_YeuCauBaoTri_TrangThai",
                table: "YeuCauBaoTriThietBi",
                column: "TrangThai");

            migrationBuilder.CreateIndex(
                name: "IX_YeuCauBaoTriThietBi_MaNhanVienXacNhan",
                table: "YeuCauBaoTriThietBi",
                column: "MaNhanVienXacNhan");

            migrationBuilder.CreateIndex(
                name: "IX_YeuCauBaoTriThietBi_MaNhanVienYeuCau",
                table: "YeuCauBaoTriThietBi",
                column: "MaNhanVienYeuCau");

            migrationBuilder.AddForeignKey(
                name: "FK_HoSoBaoTri_YeuCauBaoTri",
                table: "HoSoBaoTri",
                column: "MaYeuCauBaoTri",
                principalTable: "YeuCauBaoTriThietBi",
                principalColumn: "MaYeuCauBaoTri");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HoSoBaoTri_YeuCauBaoTri",
                table: "HoSoBaoTri");

            migrationBuilder.DropTable(
                name: "YeuCauBaoTriThietBi");

            migrationBuilder.DropIndex(
                name: "IX_HoSoBaoTri_MaYeuCauBaoTri",
                table: "HoSoBaoTri");

            migrationBuilder.DropColumn(
                name: "MaYeuCauBaoTri",
                table: "HoSoBaoTri");

            migrationBuilder.AlterColumn<string>(
                name: "LyDoTuChoi",
                table: "PhanCongCongViec",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldNullable: true);
        }
    }
}
