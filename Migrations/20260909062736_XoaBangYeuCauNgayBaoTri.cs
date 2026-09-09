using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class XoaBangYeuCauNgayBaoTri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YeuCauNgayBaoTri");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YeuCauNgayBaoTri",
                columns: table => new
                {
                    MaYeuCauNgayBaoTri = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaNguoiDungTao = table.Column<int>(type: "int", nullable: false),
                    MaThietBi = table.Column<int>(type: "int", nullable: false),
                    Nam = table.Column<int>(type: "int", nullable: false),
                    NgayBaoTri = table.Column<DateOnly>(type: "date", nullable: false),
                    NgayTao = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    TrangThai = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Chờ lập kế hoạch")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YeuCauNgayBaoTri", x => x.MaYeuCauNgayBaoTri);
                    table.ForeignKey(
                        name: "FK_YeuCauNgayBaoTri_NguoiDung",
                        column: x => x.MaNguoiDungTao,
                        principalTable: "QuanLyNguoiDung",
                        principalColumn: "MaNguoiDung");
                    table.ForeignKey(
                        name: "FK_YeuCauNgayBaoTri_ThietBi",
                        column: x => x.MaThietBi,
                        principalTable: "ThietBi",
                        principalColumn: "MaThietBi");
                });

            migrationBuilder.CreateIndex(
                name: "IX_YeuCauNgayBaoTri_MaNguoiDungTao",
                table: "YeuCauNgayBaoTri",
                column: "MaNguoiDungTao");

            migrationBuilder.CreateIndex(
                name: "UQ_YeuCauNgayBaoTri_ThietBi_Nam",
                table: "YeuCauNgayBaoTri",
                columns: new[] { "MaThietBi", "Nam" },
                unique: true);
        }
    }
}
