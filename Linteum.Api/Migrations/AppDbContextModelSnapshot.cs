﻿// <auto-generated />
using System;
using Linteum.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Linteum.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    partial class AppDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.6")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Linteum.Domain.BalanceChangedEvent", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("CanvasId")
                        .HasColumnType("uuid");

                    b.Property<DateTime>("ChangedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<long>("NewBalance")
                        .HasColumnType("bigint");

                    b.Property<long>("OldBalance")
                        .HasColumnType("bigint");

                    b.Property<int>("Reason")
                        .HasColumnType("integer");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("CanvasId");

                    b.HasIndex("UserId", "CanvasId");

                    b.ToTable("BalanceChangedEvents");
                });

            modelBuilder.Entity("Linteum.Domain.Canvas", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("Height")
                        .HasColumnType("integer");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(128)
                        .HasColumnType("character varying(128)");

                    b.Property<string>("PasswordHash")
                        .HasMaxLength(128)
                        .HasColumnType("character varying(128)");

                    b.Property<DateTime>("UpdatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("Width")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("Canvases");
                });

            modelBuilder.Entity("Linteum.Domain.Color", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("HexValue")
                        .IsRequired()
                        .HasMaxLength(7)
                        .HasColumnType("character varying(7)");

                    b.Property<string>("Name")
                        .HasMaxLength(64)
                        .HasColumnType("character varying(64)");

                    b.HasKey("Id");

                    b.ToTable("Colors");
                });

            modelBuilder.Entity("Linteum.Domain.LoginEvent", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("IpAddress")
                        .HasMaxLength(128)
                        .HasColumnType("character varying(128)");

                    b.Property<DateTime>("LoggedInAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("Provider")
                        .HasColumnType("integer");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("LoginEvents");
                });

            modelBuilder.Entity("Linteum.Domain.Pixel", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("CanvasId")
                        .HasColumnType("uuid");

                    b.Property<int>("ColorId")
                        .HasColumnType("integer");

                    b.Property<Guid?>("OwnerId")
                        .HasColumnType("uuid");

                    b.Property<long>("Price")
                        .HasColumnType("bigint");

                    b.Property<int>("X")
                        .HasColumnType("integer");

                    b.Property<int>("Y")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.HasIndex("OwnerId");

                    b.HasIndex("CanvasId", "X", "Y")
                        .IsUnique();

                    b.ToTable("Pixels");
                });

            modelBuilder.Entity("Linteum.Domain.PixelChangedEvent", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<DateTime>("ChangedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("NewColorId")
                        .HasColumnType("integer");

                    b.Property<long>("NewPrice")
                        .HasColumnType("bigint");

                    b.Property<int>("OldColorId")
                        .HasColumnType("integer");

                    b.Property<Guid?>("OldOwnerUserId")
                        .HasColumnType("uuid");

                    b.Property<Guid>("OwnerUserId")
                        .HasColumnType("uuid");

                    b.Property<Guid>("PixelId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("ChangedAt");

                    b.HasIndex("OldOwnerUserId");

                    b.HasIndex("OwnerUserId");

                    b.HasIndex("PixelId");

                    b.ToTable("PixelChangedEvents");
                });

            modelBuilder.Entity("Linteum.Domain.Subscription", b =>
                {
                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.Property<Guid>("CanvasId")
                        .HasColumnType("uuid");

                    b.HasKey("UserId", "CanvasId");

                    b.HasIndex("CanvasId");

                    b.HasIndex("UserId");

                    b.ToTable("Subscriptions");
                });

            modelBuilder.Entity("Linteum.Domain.User", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("character varying(64)");

                    b.Property<int>("LoginMethod")
                        .HasColumnType("integer");

                    b.Property<string>("PasswordHashOrKey")
                        .IsRequired()
                        .HasMaxLength(128)
                        .HasColumnType("character varying(128)");

                    b.Property<string>("UserName")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("character varying(64)");

                    b.HasKey("Id");

                    b.HasIndex("Email")
                        .IsUnique();

                    b.HasIndex("UserName")
                        .IsUnique();

                    b.ToTable("Users");
                });

            modelBuilder.Entity("Linteum.Domain.BalanceChangedEvent", b =>
                {
                    b.HasOne("Linteum.Domain.Canvas", "Canvas")
                        .WithMany()
                        .HasForeignKey("CanvasId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Linteum.Domain.User", "User")
                        .WithMany("BalanceChangedEvents")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Canvas");

                    b.Navigation("User");
                });

            modelBuilder.Entity("Linteum.Domain.LoginEvent", b =>
                {
                    b.HasOne("Linteum.Domain.User", "User")
                        .WithMany("LoginEvents")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("User");
                });

            modelBuilder.Entity("Linteum.Domain.Pixel", b =>
                {
                    b.HasOne("Linteum.Domain.Canvas", "Canvas")
                        .WithMany("Pixels")
                        .HasForeignKey("CanvasId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Linteum.Domain.User", "Owner")
                        .WithMany()
                        .HasForeignKey("OwnerId");

                    b.Navigation("Canvas");

                    b.Navigation("Owner");
                });

            modelBuilder.Entity("Linteum.Domain.PixelChangedEvent", b =>
                {
                    b.HasOne("Linteum.Domain.User", "OldOwnerUser")
                        .WithMany()
                        .HasForeignKey("OldOwnerUserId");

                    b.HasOne("Linteum.Domain.User", "User")
                        .WithMany("PixelChangedEvents")
                        .HasForeignKey("OwnerUserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Linteum.Domain.Pixel", "Pixel")
                        .WithMany()
                        .HasForeignKey("PixelId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("OldOwnerUser");

                    b.Navigation("Pixel");

                    b.Navigation("User");
                });

            modelBuilder.Entity("Linteum.Domain.Subscription", b =>
                {
                    b.HasOne("Linteum.Domain.Canvas", "Canvas")
                        .WithMany("Subscriptions")
                        .HasForeignKey("CanvasId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Linteum.Domain.User", "User")
                        .WithMany("Subscriptions")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Canvas");

                    b.Navigation("User");
                });

            modelBuilder.Entity("Linteum.Domain.Canvas", b =>
                {
                    b.Navigation("Pixels");

                    b.Navigation("Subscriptions");
                });

            modelBuilder.Entity("Linteum.Domain.User", b =>
                {
                    b.Navigation("BalanceChangedEvents");

                    b.Navigation("LoginEvents");

                    b.Navigation("PixelChangedEvents");

                    b.Navigation("Subscriptions");
                });
#pragma warning restore 612, 618
        }
    }
}
