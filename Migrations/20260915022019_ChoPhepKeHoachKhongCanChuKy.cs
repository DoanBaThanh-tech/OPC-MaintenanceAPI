using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class ChoPhepKeHoachKhongCanChuKy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KeHoach_ChuKy",
                table: "KeHoachBaoTri");

            migrationBuilder.AlterColumn<int>(
                name: "MaChuKy",
                table: "KeHoachBaoTri",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_KeHoachBaoTri_ChuKyBaoTri_MaChuKy",
                table: "KeHoachBaoTri",
                column: "MaChuKy",
                principalTable: "ChuKyBaoTri",
                principalColumn: "MaChuKy",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KeHoachBaoTri_ChuKyBaoTri_MaChuKy",
                table: "KeHoachBaoTri");

            migrationBuilder.AlterColumn<int>(
                name: "MaChuKy",
                table: "KeHoachBaoTri",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_KeHoach_ChuKy",
                table: "KeHoachBaoTri",
                column: "MaChuKy",
                principalTable: "ChuKyBaoTri",
                principalColumn: "MaChuKy");
        }
    }
}
