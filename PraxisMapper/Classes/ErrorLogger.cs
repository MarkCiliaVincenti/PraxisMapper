﻿using PraxisCore;
using System;

namespace PraxisMapper.Classes
{
    //Saves exceptions caught to the database for later reference.
    public static class ErrorLogger
    {
        public static void LogError(Exception ex)
        {
            var el = new DbTables.ErrorLog();
            el.Message = ex.Message;
            el.StackTrace = ex.StackTrace;
            el.LoggedAt = DateTime.UtcNow;
            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.ErrorLogs.Add(el);
            db.SaveChanges();
        }
    }
}
