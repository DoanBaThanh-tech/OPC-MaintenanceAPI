using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class ThemGioVaDonDuLieuHoSoBaoTri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Thêm cột GioBatDauDuKien nếu chưa tồn tại
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID('HoSoBaoTri')
                    AND name = 'GioBatDauDuKien'
                )
                BEGIN
                    ALTER TABLE HoSoBaoTri
                    ADD GioBatDauDuKien time NULL;
                END
            ");

            // Thêm cột GioKetThucDuKien nếu chưa tồn tại
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID('HoSoBaoTri')
                    AND name = 'GioKetThucDuKien'
                )
                BEGIN
                    ALTER TABLE HoSoBaoTri
                    ADD GioKetThucDuKien time NULL;
                END
            ");

            // ============================================
            // DỌN DỮ LIỆU HỒ SƠ BẢO TRÌ KHÔNG HỢP LỆ
            // ============================================

            // Hủy gắn ChiTiet với hồ sơ sẽ bị xóa
            migrationBuilder.Sql(@"
                UPDATE c
                SET c.MaHoSoBaoTri = NULL
                FROM ChiTietKeHoachBaoTri c
                INNER JOIN HoSoBaoTri h
                    ON h.MaHoSoBaoTri = c.MaHoSoBaoTri
                WHERE h.GioBatDauDuKien IS NULL
                   OR h.GioKetThucDuKien IS NULL
                   OR CAST(c.NgayDuKienBaoTri AS date) < CAST(h.NgayTao AS date);
            ");

            // Xóa lịch sử phê duyệt của hồ sơ không hợp lệ
            migrationBuilder.Sql(@"
                DELETE l
                FROM LichSuPheDuyet l
                INNER JOIN HoSoBaoTri h
                    ON h.MaHoSoBaoTri = l.MaHoSoBaoTri
                WHERE h.GioBatDauDuKien IS NULL
                   OR h.GioKetThucDuKien IS NULL;
            ");

            // Xóa lịch sử thiết bị của hồ sơ không hợp lệ
            migrationBuilder.Sql(@"
                DELETE l
                FROM LichSuThietBi l
                INNER JOIN HoSoBaoTri h
                    ON h.MaHoSoBaoTri = l.MaHoSoBaoTri
                WHERE h.GioBatDauDuKien IS NULL
                   OR h.GioKetThucDuKien IS NULL;
            ");

            // Xóa hồ sơ bảo trì không có giờ bắt đầu/kết thúc
            migrationBuilder.Sql(@"
                DELETE FROM HoSoBaoTri
                WHERE GioBatDauDuKien IS NULL
                   OR GioKetThucDuKien IS NULL;
            ");

            // Xóa hồ sơ có ngày dự kiến bảo trì trước ngày tạo hồ sơ
            migrationBuilder.Sql(@"
                DELETE h
                FROM HoSoBaoTri h
                INNER JOIN ChiTietKeHoachBaoTri c
                    ON c.MaHoSoBaoTri = h.MaHoSoBaoTri
                WHERE CAST(c.NgayDuKienBaoTri AS date) < CAST(h.NgayTao AS date);
            ");

            // Xóa ChiTiet không còn gắn với hồ sơ
            migrationBuilder.Sql(@"
                DELETE FROM ChiTietKeHoachBaoTri
                WHERE MaHoSoBaoTri IS NULL;
            ");

            // ============================================
            // CẬP NHẬT TÌNH TRẠNG THIẾT BỊ
            // ============================================

            migrationBuilder.Sql(@"
                UPDATE t
                SET t.TinhTrangHienTai = N'Sản xuất'
                FROM ThietBi t
                WHERE t.TinhTrangHienTai IN (N'Bảo trì', N'Sửa chữa')
                  AND NOT EXISTS (
                        SELECT 1
                        FROM HoSoBaoTri h
                        WHERE h.MaThieBi = t.MaThietBi
                          AND h.TrangThai = N'Đang thực hiện'
                  )
                  AND NOT EXISTS (
                        SELECT 1
                        FROM HoSoSuaChua h
                        WHERE h.MaThieBi = t.MaThietBi
                          AND h.TrangThai = N'Đang thực hiện'
                  );
            ");

            // Dọn ChiTiet còn treo (chưa gắn hồ sơ)
            // để tránh hiện "Chưa tạo hồ sơ"
            migrationBuilder.Sql(@"
                DELETE FROM ChiTietKeHoachBaoTri
                WHERE MaHoSoBaoTri IS NULL;
            ");
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