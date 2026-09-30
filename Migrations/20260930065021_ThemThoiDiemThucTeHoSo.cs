using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class ThemThoiDiemThucTeHoSo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DonGia",
                table: "VatTu",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "LaNguoiGhiChep",
                table: "PhanCongCongViec",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "GioBatDauDuKien",
                table: "HoSoSuaChua",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "GioKetThucDuKien",
                table: "HoSoSuaChua",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ThoiDiemBatDauThucTe",
                table: "HoSoSuaChua",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ThoiDiemKetThucThucTe",
                table: "HoSoSuaChua",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThoiGianDuKien",
                table: "HoSoSuaChua",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ThoiDiemBatDauThucTe",
                table: "HoSoBaoTri",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ThoiDiemKetThucThucTe",
                table: "HoSoBaoTri",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HoSoSuDungVatTu",
                columns: table => new
                {
                    MaHoSoVatTu = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaHoSoBaoTri = table.Column<int>(type: "int", nullable: true),
                    MaHoSoSuaChua = table.Column<int>(type: "int", nullable: true),
                    MaThietBi = table.Column<int>(type: "int", nullable: true),
                    TenThietBi = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    LoaiCongViec = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NgayThucHien = table.Column<DateTime>(type: "datetime", nullable: false),
                    MaNhanVienTH = table.Column<int>(type: "int", nullable: false),
                    TongTien = table.Column<decimal>(type: "decimal(18,0)", nullable: false),
                    TrangThai = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NgayGuiGiamDoc = table.Column<DateTime>(type: "datetime", nullable: true),
                    GhiChu = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NgayTao = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HoSoSuDungVatTu", x => x.MaHoSoVatTu);
                    table.ForeignKey(
                        name: "FK_HSVT_HoSoBT",
                        column: x => x.MaHoSoBaoTri,
                        principalTable: "HoSoBaoTri",
                        principalColumn: "MaHoSoBaoTri");
                    table.ForeignKey(
                        name: "FK_HSVT_HoSoSC",
                        column: x => x.MaHoSoSuaChua,
                        principalTable: "HoSoSuaChua",
                        principalColumn: "MaHoSoSuaChua");
                    table.ForeignKey(
                        name: "FK_HSVT_NV",
                        column: x => x.MaNhanVienTH,
                        principalTable: "NhanVien",
                        principalColumn: "MaNhanVien");
                    table.ForeignKey(
                        name: "FK_HSVT_ThietBi",
                        column: x => x.MaThietBi,
                        principalTable: "ThietBi",
                        principalColumn: "MaThietBi");
                });

            migrationBuilder.CreateTable(
                name: "QuyTrinhThietBi",
                columns: table => new
                {
                    MaQuyTrinh = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaThietBi = table.Column<int>(type: "int", nullable: false),
                    LoaiCongViec = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SoBuoc = table.Column<int>(type: "int", nullable: false),
                    MoTaBuoc = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ThuTu = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuyTrinhThietBi", x => x.MaQuyTrinh);
                    table.ForeignKey(
                        name: "FK_QuyTrinhThietBi_ThietBi_MaThietBi",
                        column: x => x.MaThietBi,
                        principalTable: "ThietBi",
                        principalColumn: "MaThietBi");
                });

            migrationBuilder.CreateTable(
                name: "ChiTietSuDungVatTu",
                columns: table => new
                {
                    MaChiTiet = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaHoSoVatTu = table.Column<int>(type: "int", nullable: false),
                    SoBuoc = table.Column<int>(type: "int", nullable: false),
                    MoTaBuoc = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MaVatTu = table.Column<int>(type: "int", nullable: true),
                    TenVatTu = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SoLuong = table.Column<int>(type: "int", nullable: false),
                    DonGia = table.Column<decimal>(type: "decimal(18,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChiTietSuDungVatTu", x => x.MaChiTiet);
                    table.ForeignKey(
                        name: "FK_CTSDVT_HoSo",
                        column: x => x.MaHoSoVatTu,
                        principalTable: "HoSoSuDungVatTu",
                        principalColumn: "MaHoSoVatTu",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CTSDVT_VT",
                        column: x => x.MaVatTu,
                        principalTable: "VatTu",
                        principalColumn: "MaVatTu");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChiTietSuDungVatTu_MaHoSoVatTu",
                table: "ChiTietSuDungVatTu",
                column: "MaHoSoVatTu");

            migrationBuilder.CreateIndex(
                name: "IX_ChiTietSuDungVatTu_MaVatTu",
                table: "ChiTietSuDungVatTu",
                column: "MaVatTu");

            migrationBuilder.CreateIndex(
                name: "IX_HoSoSuDungVatTu_MaHoSoBaoTri",
                table: "HoSoSuDungVatTu",
                column: "MaHoSoBaoTri");

            migrationBuilder.CreateIndex(
                name: "IX_HoSoSuDungVatTu_MaHoSoSuaChua",
                table: "HoSoSuDungVatTu",
                column: "MaHoSoSuaChua");

            migrationBuilder.CreateIndex(
                name: "IX_HoSoSuDungVatTu_MaNhanVienTH",
                table: "HoSoSuDungVatTu",
                column: "MaNhanVienTH");

            migrationBuilder.CreateIndex(
                name: "IX_HoSoSuDungVatTu_MaThietBi",
                table: "HoSoSuDungVatTu",
                column: "MaThietBi");

            migrationBuilder.CreateIndex(
                name: "IX_QuyTrinhThietBi_MaThietBi",
                table: "QuyTrinhThietBi",
                column: "MaThietBi");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChiTietSuDungVatTu");

            migrationBuilder.DropTable(
                name: "QuyTrinhThietBi");

            migrationBuilder.DropTable(
                name: "HoSoSuDungVatTu");

            migrationBuilder.DropColumn(
                name: "DonGia",
                table: "VatTu");

            migrationBuilder.DropColumn(
                name: "LaNguoiGhiChep",
                table: "PhanCongCongViec");

            migrationBuilder.DropColumn(
                name: "GioBatDauDuKien",
                table: "HoSoSuaChua");

            migrationBuilder.DropColumn(
                name: "GioKetThucDuKien",
                table: "HoSoSuaChua");

            migrationBuilder.DropColumn(
                name: "ThoiDiemBatDauThucTe",
                table: "HoSoSuaChua");

            migrationBuilder.DropColumn(
                name: "ThoiDiemKetThucThucTe",
                table: "HoSoSuaChua");

            migrationBuilder.DropColumn(
                name: "ThoiGianDuKien",
                table: "HoSoSuaChua");

            migrationBuilder.DropColumn(
                name: "ThoiDiemBatDauThucTe",
                table: "HoSoBaoTri");

            migrationBuilder.DropColumn(
                name: "ThoiDiemKetThucThucTe",
                table: "HoSoBaoTri");
        }
    }
}
