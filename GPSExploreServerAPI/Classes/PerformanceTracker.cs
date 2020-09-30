﻿using DatabaseAccess;
using static DatabaseAccess.DbTables;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics.Eventing.Reader;
using Microsoft.EntityFrameworkCore;

namespace GPSExploreServerAPI.Classes
{
    public class PerformanceTracker
    {
        //TODO: add toggle for this somewhere in the server config.
        public static bool EnableLogging = true;

        PerformanceInfo pi = new PerformanceInfo();
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        public PerformanceTracker(string name)
        {
            if (!EnableLogging) return;
            pi.functionName = name;
            pi.calledAt = DateTime.Now;
            sw.Start();
        }

        public void Stop()
        {
            Stop("");
        }

        public void Stop(string notes)
        {
            if (!EnableLogging) return;
            sw.Stop();
            GpsExploreContext db = new GpsExploreContext();
            db.Database.ExecuteSqlRaw("SavePerfInfo @p0, @p1, @p2 @p3", parameters: new object[] { pi.functionName, sw.ElapsedMilliseconds, pi.calledAt, notes });
            return;
        }
    }
}
