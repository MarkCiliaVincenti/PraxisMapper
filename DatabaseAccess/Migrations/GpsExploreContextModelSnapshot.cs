﻿// <auto-generated />
using System;
using DatabaseAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetTopologySuite.Geometries;

namespace DatabaseAccess.Migrations
{
    [DbContext(typeof(GpsExploreContext))]
    partial class GpsExploreContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "3.1.7")
                .HasAnnotation("Relational:MaxIdentifierLength", 128)
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("DatabaseAccess.DbTables+AreaType", b =>
                {
                    b.Property<int>("AreaTypeId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("AreaName")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("OsmTags")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("AreaTypeId");

                    b.ToTable("AreaTypes");
                });

            modelBuilder.Entity("DatabaseAccess.DbTables+MapData", b =>
                {
                    b.Property<long>("MapDataId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<long>("WayId")
                        .HasColumnType("bigint");

                    b.Property<string>("name")
                        .HasColumnType("nvarchar(max)");

                    b.Property<Geometry>("place")
                        .HasColumnType("geography");

                    b.Property<string>("type")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("MapDataId");

                    b.HasIndex("WayId");

                    b.ToTable("MapData");
                });

            modelBuilder.Entity("DatabaseAccess.DbTables+PerformanceInfo", b =>
                {
                    b.Property<int>("PerformanceInfoID")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<DateTime>("calledAt")
                        .HasColumnType("datetime2");

                    b.Property<string>("functionName")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("notes")
                        .HasColumnType("nvarchar(max)");

                    b.Property<long>("runTime")
                        .HasColumnType("bigint");

                    b.HasKey("PerformanceInfoID");

                    b.ToTable("PerformanceInfo");
                });

            modelBuilder.Entity("DatabaseAccess.DbTables+PlayerData", b =>
                {
                    b.Property<int>("PlayerDataID")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<int>("DateLastTrophyBought")
                        .HasColumnType("int");

                    b.Property<int>("altitudeSpread")
                        .HasColumnType("int");

                    b.Property<int>("cellVisits")
                        .HasColumnType("int");

                    b.Property<string>("deviceID")
                        .HasColumnType("nvarchar(450)");

                    b.Property<double>("distance")
                        .HasColumnType("float");

                    b.Property<DateTime>("lastSyncTime")
                        .HasColumnType("datetime2");

                    b.Property<double>("maxSpeed")
                        .HasColumnType("float");

                    b.Property<int>("score")
                        .HasColumnType("int");

                    b.Property<int>("t10Cells")
                        .HasColumnType("int");

                    b.Property<int>("t8Cells")
                        .HasColumnType("int");

                    b.Property<int>("timePlayed")
                        .HasColumnType("int");

                    b.Property<double>("totalSpeed")
                        .HasColumnType("float");

                    b.HasKey("PlayerDataID");

                    b.HasIndex("deviceID");

                    b.ToTable("PlayerData");
                });

            modelBuilder.Entity("DatabaseAccess.DbTables+ProcessedWay", b =>
                {
                    b.Property<long>("ProcessedWayId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("AreaType")
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("AreaTypeId")
                        .HasColumnType("int");

                    b.Property<long>("OsmWayId")
                        .HasColumnType("bigint");

                    b.Property<double>("distanceE")
                        .HasColumnType("float");

                    b.Property<double>("distanceN")
                        .HasColumnType("float");

                    b.Property<DateTime>("lastUpdated")
                        .HasColumnType("datetime2");

                    b.Property<double>("latitudeS")
                        .HasColumnType("float");

                    b.Property<double>("longitudeW")
                        .HasColumnType("float");

                    b.Property<string>("name")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("ProcessedWayId");

                    b.HasIndex("OsmWayId");

                    b.ToTable("ProcessedWays");
                });

            modelBuilder.Entity("DatabaseAccess.DbTables+SinglePointsOfInterest", b =>
                {
                    b.Property<long>("SinglePointsOfInterestId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<long>("NodeID")
                        .HasColumnType("bigint");

                    b.Property<string>("NodeType")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("PlusCode")
                        .HasColumnType("nvarchar(15)")
                        .HasMaxLength(15);

                    b.Property<string>("PlusCode6")
                        .HasColumnType("nvarchar(6)")
                        .HasMaxLength(6);

                    b.Property<string>("PlusCode8")
                        .HasColumnType("nvarchar(8)")
                        .HasMaxLength(8);

                    b.Property<double>("lat")
                        .HasColumnType("float");

                    b.Property<double>("lon")
                        .HasColumnType("float");

                    b.Property<string>("name")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("SinglePointsOfInterestId");

                    b.HasIndex("PlusCode");

                    b.HasIndex("PlusCode6");

                    b.HasIndex("PlusCode8");

                    b.ToTable("SinglePointsOfInterests");
                });
#pragma warning restore 612, 618
        }
    }
}
