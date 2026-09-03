using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using OPC.MaintenanceAPI.Models;

namespace OPC.MaintenanceAPI.Data;

public partial class OPCDbContext : DbContext
{
    public OPCDbContext()
    {
    }

    public OPCDbContext(DbContextOptions<OPCDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ChiTietKeHoachBaoTri> ChiTietKeHoachBaoTris { get; set; }

    public virtual DbSet<ChiTietYeuCauVatTu> ChiTietYeuCauVatTus { get; set; }

    public virtual DbSet<ChuKyBaoTri> ChuKyBaoTris { get; set; }

    public virtual DbSet<DanhMucChucNang> DanhMucChucNangs { get; set; }

    public virtual DbSet<HoSoBaoTri> HoSoBaoTris { get; set; }

    public virtual DbSet<HoSoSuaChua> HoSoSuaChuas { get; set; }

    public virtual DbSet<HoSoYeuCauVatTu> HoSoYeuCauVatTus { get; set; }

    public virtual DbSet<KeHoachBaoTri> KeHoachBaoTris { get; set; }

    public virtual DbSet<KetQuaThucHien> KetQuaThucHiens { get; set; }

    public virtual DbSet<LichSuPheDuyet> LichSuPheDuyets { get; set; }

    public virtual DbSet<LichSuThietBi> LichSuThietBis { get; set; }

    public virtual DbSet<NhanVien> NhanViens { get; set; }

    public virtual DbSet<NhapXuatVatTu> NhapXuatVatTus { get; set; }

    public virtual DbSet<NhatKyHeThong> NhatKyHeThongs { get; set; }

    public virtual DbSet<PhanCongCongViec> PhanCongCongViecs { get; set; }

    public virtual DbSet<PhanQuyenVaiTro> PhanQuyenVaiTros { get; set; }

    public virtual DbSet<QuanLyNguoiDung> QuanLyNguoiDungs { get; set; }

    public virtual DbSet<ThietBi> ThietBis { get; set; }

    public virtual DbSet<VaiTro> VaiTros { get; set; }

    public virtual DbSet<VatTu> VatTus { get; set; }
    public virtual DbSet<XacThucQuenMatKhau> XacThucQuenMatKhaus { get; set; }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer("Name=ConnectionStrings:DefaultConnection");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChiTietKeHoachBaoTri>(entity =>
        {
            entity.HasKey(e => e.MaChiTietKeHoach).HasName("PK__ChiTietK__63E158BAABC27D93");

            entity.ToTable("ChiTietKeHoachBaoTri");

            entity.HasIndex(e => e.MaHoSoBaoTri, "UQ__ChiTietK__9AAC00375C21B2F0").IsUnique();

            entity.Property(e => e.GhiChu).HasMaxLength(255);
            entity.Property(e => e.TrangThai).HasMaxLength(30);

            entity.HasOne(d => d.MaHoSoBaoTriNavigation).WithOne(p => p.ChiTietKeHoachBaoTri)
                .HasForeignKey<ChiTietKeHoachBaoTri>(d => d.MaHoSoBaoTri)
                .HasConstraintName("FK_ChiTietKH_HoSoBT");

            entity.HasOne(d => d.MaKeHoachNavigation).WithMany(p => p.ChiTietKeHoachBaoTris)
                .HasForeignKey(d => d.MaKeHoach)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ChiTietKH_KeHoach");

            entity.HasOne(d => d.MaThietBiNavigation).WithMany(p => p.ChiTietKeHoachBaoTris)
                .HasForeignKey(d => d.MaThietBi)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ChiTietKH_ThietBi");
        });

        modelBuilder.Entity<ChiTietYeuCauVatTu>(entity =>
        {
            entity.HasKey(e => e.MaChiTietYeuCauVatTu).HasName("PK__ChiTietY__80836208A54A835B");

            entity.ToTable("ChiTietYeuCauVatTu");

            entity.HasOne(d => d.MaVatTuNavigation).WithMany(p => p.ChiTietYeuCauVatTus)
                .HasForeignKey(d => d.MaVatTu)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ChiTietYCVT_VatTu");

            entity.HasOne(d => d.MaYeuCauVatTuNavigation).WithMany(p => p.ChiTietYeuCauVatTus)
                .HasForeignKey(d => d.MaYeuCauVatTu)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ChiTietYCVT_YeuCau");
        });

        modelBuilder.Entity<ChuKyBaoTri>(entity =>
        {
            entity.HasKey(e => e.MaChuKy).HasName("PK__ChuKyBao__35853BEEBAC88F7E");

            entity.ToTable("ChuKyBaoTri");

            entity.Property(e => e.LoaiThietBi).HasMaxLength(100);
            entity.Property(e => e.MoTa).HasMaxLength(255);
        });

        modelBuilder.Entity<DanhMucChucNang>(entity =>
        {
            entity.HasKey(e => e.MaChucNang).HasName("PK__DanhMucC__B26DC257D64BF628");

            entity.ToTable("DanhMucChucNang");

            entity.Property(e => e.MoTa).HasMaxLength(255);
            entity.Property(e => e.NhomChucNang).HasMaxLength(100);
            entity.Property(e => e.TenChucNang).HasMaxLength(150);
        });

        modelBuilder.Entity<HoSoBaoTri>(entity =>
        {
            entity.HasKey(e => e.MaHoSoBaoTri).HasName("PK__HoSoBaoT__9AAC003672B25F3E");

            entity.ToTable("HoSoBaoTri");

            entity.HasIndex(e => e.TrangThai, "IX_HoSoBaoTri_TrangThai");

            entity.HasIndex(e => e.MaPhanCong, "UQ__HoSoBaoT__C279D917ECC83AEB").IsUnique();

            entity.Property(e => e.LyDoTuChoi).HasMaxLength(255);
            entity.Property(e => e.NgayDuyet).HasColumnType("datetime");
            entity.Property(e => e.NgayTao)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.NoiDungCongViec).HasMaxLength(500);
            entity.Property(e => e.ThoiGianDuKien).HasMaxLength(50);
            entity.Property(e => e.TrangThai)
                .HasMaxLength(30)
                .HasDefaultValue("Chờ duyệt");

            entity.HasOne(d => d.MaNhanVienDuyetNavigation).WithMany(p => p.HoSoBaoTriMaNhanVienDuyetNavigations)
                .HasForeignKey(d => d.MaNhanVienDuyet)
                .HasConstraintName("FK_HoSoBaoTri_NVDuyet");

            entity.HasOne(d => d.MaNhanVienTaoNavigation).WithMany(p => p.HoSoBaoTriMaNhanVienTaoNavigations)
                .HasForeignKey(d => d.MaNhanVienTao)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_HoSoBaoTri_NVTao");

            entity.HasOne(d => d.MaPhanCongNavigation).WithOne(p => p.HoSoBaoTri)
                .HasForeignKey<HoSoBaoTri>(d => d.MaPhanCong)
                .HasConstraintName("FK_HoSoBaoTri_PhanCong");

            entity.HasOne(d => d.MaThieBiNavigation).WithMany(p => p.HoSoBaoTris)
                .HasForeignKey(d => d.MaThieBi)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_HoSoBaoTri_ThietBi");
        });

        modelBuilder.Entity<HoSoSuaChua>(entity =>
        {
            entity.HasKey(e => e.MaHoSoSuaChua).HasName("PK__HoSoSuaC__B9B48E5C63379B32");

            entity.ToTable("HoSoSuaChua");

            entity.HasIndex(e => e.TrangThai, "IX_HoSoSuaChua_TrangThai");

            entity.HasIndex(e => e.MaPhanCong, "UQ__HoSoSuaC__C279D91736C9DBA8").IsUnique();

            entity.Property(e => e.LyDoTuChoi).HasMaxLength(255);
            entity.Property(e => e.MoTaHuHong).HasMaxLength(500);
            entity.Property(e => e.NgayDuyet).HasColumnType("datetime");
            entity.Property(e => e.NgayTao)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.PhuongAnSuaChua).HasMaxLength(500);
            entity.Property(e => e.TrangThai)
                .HasMaxLength(30)
                .HasDefaultValue("Chờ duyệt");

            entity.HasOne(d => d.MaNhanVienDuyetNavigation).WithMany(p => p.HoSoSuaChuaMaNhanVienDuyetNavigations)
                .HasForeignKey(d => d.MaNhanVienDuyet)
                .HasConstraintName("FK_HoSoSuaChua_NVDuyet");

            entity.HasOne(d => d.MaNhanVienTaoNavigation).WithMany(p => p.HoSoSuaChuaMaNhanVienTaoNavigations)
                .HasForeignKey(d => d.MaNhanVienTao)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_HoSoSuaChua_NVTao");

            entity.HasOne(d => d.MaPhanCongNavigation).WithOne(p => p.HoSoSuaChua)
                .HasForeignKey<HoSoSuaChua>(d => d.MaPhanCong)
                .HasConstraintName("FK_HoSoSuaChua_PhanCong");

            entity.HasOne(d => d.MaThieBiNavigation).WithMany(p => p.HoSoSuaChuas)
                .HasForeignKey(d => d.MaThieBi)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_HoSoSuaChua_ThietBi");
        });

        modelBuilder.Entity<HoSoYeuCauVatTu>(entity =>
        {
            entity.HasKey(e => e.MaYeuCauVatTu).HasName("PK__HoSoYeuC__2BB6DC13181B181A");

            entity.ToTable("HoSoYeuCauVatTu");

            entity.HasIndex(e => e.MaHoSoSuaChua, "UQ__HoSoYeuC__B9B48E5D755382F9").IsUnique();

            entity.Property(e => e.LyDoTuChoi).HasMaxLength(255);
            entity.Property(e => e.NgayDuyet).HasColumnType("datetime");
            entity.Property(e => e.NgayTao)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.TrangThai)
                .HasMaxLength(30)
                .HasDefaultValue("Chờ duyệt");

            entity.HasOne(d => d.MaHoSoSuaChuaNavigation).WithOne(p => p.HoSoYeuCauVatTu)
                .HasForeignKey<HoSoYeuCauVatTu>(d => d.MaHoSoSuaChua)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_YeuCauVT_HoSoSC");

            entity.HasOne(d => d.MaNhanVienDuyetNavigation).WithMany(p => p.HoSoYeuCauVatTuMaNhanVienDuyetNavigations)
                .HasForeignKey(d => d.MaNhanVienDuyet)
                .HasConstraintName("FK_YeuCauVT_NVDuyet");

            entity.HasOne(d => d.MaNhanVienTaoNavigation).WithMany(p => p.HoSoYeuCauVatTuMaNhanVienTaoNavigations)
                .HasForeignKey(d => d.MaNhanVienTao)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_YeuCauVT_NVTao");
        });

        modelBuilder.Entity<KeHoachBaoTri>(entity =>
        {
            entity.HasKey(e => e.MaKeHoach).HasName("PK__KeHoachB__88C5741F6562E6B7");

            entity.ToTable("KeHoachBaoTri");

            entity.Property(e => e.NgayTao)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.TrangThai).HasMaxLength(30);

            entity.HasOne(d => d.MaChuKyNavigation).WithMany(p => p.KeHoachBaoTris)
                .HasForeignKey(d => d.MaChuKy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_KeHoach_ChuKy");

            entity.HasOne(d => d.MaNhanVienLapNavigation).WithMany(p => p.KeHoachBaoTris)
                .HasForeignKey(d => d.MaNhanVienLap)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_KeHoach_NhanVien");
        });

        modelBuilder.Entity<KetQuaThucHien>(entity =>
        {
            entity.HasKey(e => e.MaKetQua).HasName("PK__KetQuaTh__D5B3102AF619BDD7");

            entity.ToTable("KetQuaThucHien");

            entity.HasIndex(e => e.MaPhanCong, "UQ__KetQuaTh__C279D917DDCCA813").IsUnique();

            entity.Property(e => e.GhiChu).HasMaxLength(500);
            entity.Property(e => e.HinhAnh).HasMaxLength(255);
            entity.Property(e => e.NgayGhiNhan)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.SoLieuGhiNhan).HasMaxLength(500);

            entity.HasOne(d => d.MaNhanVienGhiNhanNavigation).WithMany(p => p.KetQuaThucHiens)
                .HasForeignKey(d => d.MaNhanVienGhiNhan)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_KetQua_NhanVien");

            entity.HasOne(d => d.MaPhanCongNavigation).WithOne(p => p.KetQuaThucHien)
                .HasForeignKey<KetQuaThucHien>(d => d.MaPhanCong)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_KetQua_PhanCong");
        });

        modelBuilder.Entity<LichSuPheDuyet>(entity =>
        {
            entity.HasKey(e => e.MaPheDuyet).HasName("PK__LichSuPh__D14CE0E0A31E0715");

            entity.ToTable("LichSuPheDuyet");

            entity.HasIndex(e => e.MaNhanVienDuyet, "IX_LSPD_NhanVienDuyet");

            entity.Property(e => e.LyDo).HasMaxLength(255);
            entity.Property(e => e.NgayDuyet)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.QuyetDinh).HasMaxLength(20);

            entity.HasOne(d => d.MaHoSoBaoTriNavigation).WithMany(p => p.LichSuPheDuyets)
                .HasForeignKey(d => d.MaHoSoBaoTri)
                .HasConstraintName("FK_LSPD_HoSoBaoTri");

            entity.HasOne(d => d.MaHoSoSuaChuaNavigation).WithMany(p => p.LichSuPheDuyets)
                .HasForeignKey(d => d.MaHoSoSuaChua)
                .HasConstraintName("FK_LSPD_HoSoSuaChua");

            entity.HasOne(d => d.MaNhanVienDuyetNavigation).WithMany(p => p.LichSuPheDuyets)
                .HasForeignKey(d => d.MaNhanVienDuyet)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LSPD_NhanVien");

            entity.HasOne(d => d.MaYeuCauVatTuNavigation).WithMany(p => p.LichSuPheDuyets)
                .HasForeignKey(d => d.MaYeuCauVatTu)
                .HasConstraintName("FK_LSPD_YeuCauVT");
        });

        modelBuilder.Entity<LichSuThietBi>(entity =>
        {
            entity.HasKey(e => e.MaLichSu).HasName("PK__LichSuTh__C443222A120ED592");

            entity.ToTable("LichSuThietBi");

            entity.HasIndex(e => e.MaHoSoBaoTri, "UQ__LichSuTh__9AAC0037073AB41F").IsUnique();

            entity.HasIndex(e => e.MaHoSoSuaChua, "UQ__LichSuTh__B9B48E5DF745169F").IsUnique();

            entity.Property(e => e.GhiChu).HasMaxLength(255);
            entity.Property(e => e.KetQua).HasMaxLength(255);
            entity.Property(e => e.NgayHoanThanh).HasColumnType("datetime");

            entity.HasOne(d => d.MaHoSoBaoTriNavigation).WithOne(p => p.LichSuThietBi)
                .HasForeignKey<LichSuThietBi>(d => d.MaHoSoBaoTri)
                .HasConstraintName("FK_LSTB_HoSoBaoTri");

            entity.HasOne(d => d.MaHoSoSuaChuaNavigation).WithOne(p => p.LichSuThietBi)
                .HasForeignKey<LichSuThietBi>(d => d.MaHoSoSuaChua)
                .HasConstraintName("FK_LSTB_HoSoSuaChua");

            entity.HasOne(d => d.MaThietBiNavigation).WithMany(p => p.LichSuThietBis)
                .HasForeignKey(d => d.MaThietBi)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LSTB_ThietBi");
        });

        modelBuilder.Entity<NhanVien>(entity =>
        {
            entity.HasKey(e => e.MaNhanVien).HasName("PK__NhanVien__77B2CA476C4A22F7");

            entity.ToTable("NhanVien");

            entity.HasIndex(e => e.MaNguoiDung, "UQ__NhanVien__C539D763184E88B4").IsUnique();

            entity.Property(e => e.ChucVu).HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.HoTen).HasMaxLength(100);
            entity.Property(e => e.NgayTao)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.SoDienThoai)
                .HasMaxLength(15)
                .IsUnicode(false);
            entity.Property(e => e.TrangThai).HasMaxLength(30);

            entity.HasOne(d => d.MaNguoiDungNavigation).WithOne(p => p.NhanVien)
                .HasForeignKey<NhanVien>(d => d.MaNguoiDung)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NhanVien_NguoiDung");
        });

        modelBuilder.Entity<NhapXuatVatTu>(entity =>
        {
            entity.HasKey(e => e.MaGiaoDich).HasName("PK__NhapXuat__0A2A24EBBFDD84F5");

            entity.ToTable("NhapXuatVatTu");

            entity.Property(e => e.GhiChu).HasMaxLength(255);
            entity.Property(e => e.LoaiGiaoDich).HasMaxLength(10);
            entity.Property(e => e.NgayGiaoDich)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.MaNhanVienGiaoDichNavigation).WithMany(p => p.NhapXuatVatTus)
                .HasForeignKey(d => d.MaNhanVienGiaoDich)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NXVT_NhanVien");

            entity.HasOne(d => d.MaVatTuNavigation).WithMany(p => p.NhapXuatVatTus)
                .HasForeignKey(d => d.MaVatTu)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NXVT_VatTu");

            entity.HasOne(d => d.MaYeuCauVatTuNavigation).WithMany(p => p.NhapXuatVatTus)
                .HasForeignKey(d => d.MaYeuCauVatTu)
                .HasConstraintName("FK_NXVT_YeuCauVT");
        });

        modelBuilder.Entity<NhatKyHeThong>(entity =>
        {
            entity.HasKey(e => e.MaNhatKy).HasName("PK__NhatKyHe__E42EF42E3EF352B6");

            entity.ToTable("NhatKyHeThong");

            entity.HasIndex(e => new { e.MaNhanVien, e.ThoiGianTruyCap }, "IX_NhatKy_NhanVien_ThoiGian");

            entity.Property(e => e.DiaChiIp)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("DiaChiIP");
            entity.Property(e => e.PhuongThucHttp)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("PhuongThucHTTP");
            entity.Property(e => e.TenApi)
                .HasMaxLength(200)
                .HasColumnName("TenAPI");
            entity.Property(e => e.ThoiGianTruyCap)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.MaNhanVienNavigation).WithMany(p => p.NhatKyHeThongs)
                .HasForeignKey(d => d.MaNhanVien)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NhatKy_NhanVien");
        });

        modelBuilder.Entity<PhanCongCongViec>(entity =>
        {
            entity.HasKey(e => e.MaPhanCong).HasName("PK__PhanCong__C279D9162720337F");

            entity.ToTable("PhanCongCongViec");

            entity.Property(e => e.NgayPhanCong)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.TrangThai).HasMaxLength(30);

            entity.HasOne(d => d.MaNhanVienPhanCongNavigation).WithMany(p => p.PhanCongCongViecMaNhanVienPhanCongNavigations)
                .HasForeignKey(d => d.MaNhanVienPhanCong)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PhanCong_NhanVienPhanCong");

            entity.HasOne(d => d.MaNhanVienThucHienNavigation).WithMany(p => p.PhanCongCongViecMaNhanVienThucHienNavigations)
                .HasForeignKey(d => d.MaNhanVienThucHien)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PhanCong_NhanVienThucHien");
        });

        modelBuilder.Entity<PhanQuyenVaiTro>(entity =>
        {
            entity.HasKey(e => e.MaPhanQuyen).HasName("PK__PhanQuye__529AB12BD82D7C83");

            entity.ToTable("PhanQuyenVaiTro");

            entity.HasIndex(e => new { e.MaVaiTro, e.MaChucNang }, "UQ_PhanQuyen_VaiTro_ChucNang").IsUnique();

            entity.HasOne(d => d.MaChucNangNavigation).WithMany(p => p.PhanQuyenVaiTros)
                .HasForeignKey(d => d.MaChucNang)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PhanQuyen_ChucNang");

            entity.HasOne(d => d.MaVaiTroNavigation).WithMany(p => p.PhanQuyenVaiTros)
                .HasForeignKey(d => d.MaVaiTro)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PhanQuyen_VaiTro");
        });

        modelBuilder.Entity<QuanLyNguoiDung>(entity =>
        {
            entity.HasKey(e => e.MaNguoiDung).HasName("PK__QuanLyNg__C539D76297871A08");

            entity.ToTable("QuanLyNguoiDung");

            entity.HasIndex(e => e.Email, "UQ_QuanLyNguoiDung_Email").IsUnique();

            entity.Property(e => e.LanDangNhapCuoi).HasColumnType("datetime");
            entity.Property(e => e.MatKhau).HasMaxLength(255);
            entity.Property(e => e.NgayTao)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.TrangThai)
                    .HasMaxLength(30)
                    .HasDefaultValue("Chưa kích hoạt");

            entity.HasOne(d => d.MaVaiTroNavigation).WithMany(p => p.QuanLyNguoiDungs)
                .HasForeignKey(d => d.MaVaiTro)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_QuanLyNguoiDung_VaiTro");
        });

        modelBuilder.Entity<ThietBi>(entity =>
        {
            entity.HasKey(e => e.MaThietBi).HasName("PK__ThietBi__8AEC71F764099605");

            entity.ToTable("ThietBi");

            entity.HasIndex(e => e.NgayBaoTriTiepTheo, "IX_ThietBi_NgayBaoTriTiepTheo");

            entity.Property(e => e.GhiChu).HasMaxLength(255);
            entity.Property(e => e.LoaiThietBi).HasMaxLength(100);
            entity.Property(e => e.TenThietBi).HasMaxLength(150);
            entity.Property(e => e.TinhTrangHienTai).HasMaxLength(50);
            entity.Property(e => e.ViTriLapDat).HasMaxLength(150);

            entity.HasOne(d => d.MaChuKyNavigation).WithMany(p => p.ThietBis)
                .HasForeignKey(d => d.MaChuKy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ThietBi_ChuKy");
        });

        modelBuilder.Entity<VaiTro>(entity =>
        {
            entity.HasKey(e => e.MaVaiTro).HasName("PK__VaiTro__C24C41CFA1435993");

            entity.ToTable("VaiTro");

            entity.Property(e => e.MoTa).HasMaxLength(255);
            entity.Property(e => e.NgayTao)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.TenVaiTro).HasMaxLength(50);
            entity.Property(e => e.TrangThai).HasDefaultValue(true);
        });

        modelBuilder.Entity<VatTu>(entity =>
        {
            entity.HasKey(e => e.MaVatTu).HasName("PK__VatTu__0BD27B6A874DA6D3");

            entity.ToTable("VatTu");

            entity.HasIndex(e => new { e.SoLuongTonKho, e.MucTonKhoToiThieu }, "IX_VatTu_TonKho");

            entity.Property(e => e.DonViTinh).HasMaxLength(30);
            entity.Property(e => e.GhiChu).HasMaxLength(255);
            entity.Property(e => e.TenVatTu).HasMaxLength(150);
        });

        OnModelCreatingPartial(modelBuilder);

        modelBuilder.Entity<XacThucQuenMatKhau>(entity =>
        {
            entity.HasKey(e => e.MaXacThuc);
            entity.ToTable("XacThucQuenMatKhau");

            entity.Property(e => e.MaOTP).HasMaxLength(6);
            entity.Property(e => e.TrangThaiXacThuc).HasMaxLength(30);

            entity.HasOne(d => d.MaNguoiDungNavigation)
                .WithMany()
                .HasForeignKey(d => d.MaNguoiDung)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_XacThuc_NguoiDung");
        });
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
