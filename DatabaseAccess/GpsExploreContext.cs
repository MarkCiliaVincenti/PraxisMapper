﻿using Microsoft.EntityFrameworkCore;
using System;
using static DatabaseAccess.DbTables;

namespace DatabaseAccess
{
    public class GpsExploreContext : DbContext
    {
        public DbSet<PlayerData> PlayerData { get; set; }
        public DbSet<PerformanceInfo> PerformanceInfo { get; set; }
        public DbSet<InterestingPoint> InterestingPoints { get; set; }
        public DbSet<ProcessedWay> ProcessedWays { get; set; }
        public DbSet<AreaType> AreaTypes { get; set; }
        public DbSet<MapData> MapData { get; set; }

        public DbSet<SinglePointsOfInterest> SinglePointsOfInterests { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            //TODO: figure out this connection string for local testing, and for AWS use.
            //LocalHost
            //optionsBuilder.UseSqlServer(@"Data Source=localhost\SQLEXPRESS;Integrated Security = true;Initial Catalog=GpsExplore;", x => x.UseNetTopologySuite()); //Home config, SQL Express. Free, RAM limits. I think this causes the 'appdomain unloaded' error when it hits its RAM limit
            optionsBuilder.UseSqlServer(@"Data Source=localhost\SQLDEV;Integrated Security = true;Initial Catalog=GpsExplore;", x => x.UseNetTopologySuite()); //Home config, SQL Developer, Free, no limits, cant use in production
            //NetTopologySuite is for future location stuff from OSM data.
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            //Create indexes here.
            model.Entity<PlayerData>().HasIndex(p => p.deviceID); //for updating data

            model.Entity<ProcessedWay>().HasIndex(p => p.OsmWayId); //for updating data
            model.Entity<InterestingPoint>().HasIndex(i => i.PlusCode8); //for reading data

            model.Entity<InterestingPoint>().Property(i => i.PlusCode8).HasMaxLength(8);
            model.Entity<InterestingPoint>().Property(i => i.PlusCode2).HasMaxLength(2);

            model.Entity<SinglePointsOfInterest>().HasIndex(i => i.PlusCode); //for reading data
            model.Entity<SinglePointsOfInterest>().HasIndex(i => i.PlusCode8); //for reading data, but actually used.
            model.Entity<SinglePointsOfInterest>().Property(i => i.PlusCode8).HasMaxLength(8);
            model.Entity<SinglePointsOfInterest>().Property(i => i.PlusCode).HasMaxLength(15);
        }
    }
}
