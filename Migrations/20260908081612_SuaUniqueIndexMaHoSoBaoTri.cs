using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OPC.MaintenanceAPI.Migrations
{
    /// <inheritdoc />
    public partial class SuaUniqueIndexMaHoSoBaoTri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1
                    FROM sys.key_constraints
                    WHERE name = 'UQ__ChiTietK__9AAC00375B2F0'
                      AND parent_object_id = OBJECT_ID('dbo.ChiTietKeHoachBaoTri')
                )
                    ALTER TABLE [dbo].[ChiTietKeHoachBaoTri]
                    DROP CONSTRAINT [UQ__ChiTietK__9AAC00375B2F0];

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'UQ__ChiTietK__9AAC00375B2F0'
                      AND object_id = OBJECT_ID('dbo.ChiTietKeHoachBaoTri')
                )
                    CREATE UNIQUE INDEX [UQ__ChiTietK__9AAC00375B2F0]
                    ON [dbo].[ChiTietKeHoachBaoTri] ([MaHoSoBaoTri])
                    WHERE [MaHoSoBaoTri] IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'UQ__ChiTietK__9AAC00375B2F0'
                      AND object_id = OBJECT_ID('dbo.ChiTietKeHoachBaoTri')
                )
                    DROP INDEX [UQ__ChiTietK__9AAC00375B2F0]
                    ON [dbo].[ChiTietKeHoachBaoTri];

                ALTER TABLE [dbo].[ChiTietKeHoachBaoTri]
                ADD CONSTRAINT [UQ__ChiTietK__9AAC00375B2F0]
                UNIQUE ([MaHoSoBaoTri]);
            ");
        }
    }
}
