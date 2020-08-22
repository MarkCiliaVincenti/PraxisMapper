﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;
using DatabaseAccess;
using GPSExploreServerAPI.Classes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static DatabaseAccess.DbTables;

namespace GPSExploreServerAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class GPSExploreController : ControllerBase
    {
        /* functions needed
         * -subboards TODO:  most coffees purchased (once store functions)
         * done: Most small cells, most big cells, highest score, most distance,  most time, fastest avg speed (distance/time),
         * Ties should be broken by date (so, who most recently did the thing that set the score), which means tracking more data client-side. 
         * --Tiebreaker calc: .Where(p => p.value > my.value && p.dateLastUpdated > my.dateLastUpdated? 
         * 
         * TODO:
         * Merge with the completed OsmServer DB? Or can I use 2 DbContexts in one project?
         * Set up API to load intersting points for tiles and return that data.
         * (may need a couple passes on this concept to find the best answer)
        */

        //Session is not enabled by default on API projects, which is correct.

        [HttpGet]
        [Route("/[controller]/test")]
        public string TestDummyEndpoint()
        {
            //For debug purposes to confirm the server is running and reachable.
            return "OK";
        }

        [HttpPost]
        [Route("/[controller]/UploadData")] //use this to tag endpoints correctly.
        public string UploadData() //this was't pulling allData out as a string parameter from the body request from Solar2D.
        {
            PerformanceTracker pt = new PerformanceTracker("UploadData");
            byte[] inputStream = new byte[(int)HttpContext.Request.ContentLength];
            HttpContext.Request.Body.ReadAsync(inputStream, 0, (int)HttpContext.Request.ContentLength - 1);
            string allData = System.Text.Encoding.Default.GetString(inputStream);

            if (allData == null)
                return "Error-Null";

            //TODO: take several int parameters instead of converting all these as strings.
            //Take data from a client, save it to the DB
            string[] components = allData.Split("|");

            if (components.Length != 11)
                return "Error-Length";

            GpsExploreContext db = new GpsExploreContext();
            var data = db.PlayerData.Where(p => p.deviceID == components[0]).FirstOrDefault();
            bool insert = false;
            if (data == null || data.deviceID == null)
            {
                data = new PlayerData();
                insert = true;
            }

            data.deviceID = components[0];
            data.cellVisits = components[1].ToInt();
            data.DateLastTrophyBought = components[2].ToInt();
            data.distance = components[3].ToInt();
            data.maxSpeed = components[4].ToInt();
            data.score = components[5].ToInt();
            data.t10Cells = components[6].ToInt();
            data.t8Cells = components[7].ToInt();
            data.timePlayed = components[8].ToInt();
            data.totalSpeed = components[9].ToInt();
            data.maxAltitude = components[10].ToInt();
            data.lastSyncTime = DateTime.Now;

            if (insert)
                db.PlayerData.Add(data);

            //TODO: add cheat detection. Mark any input that's blatantly impossible.

            db.SaveChanges();

            pt.Stop();
            return "OK";
        }

        [HttpGet]
        [Route("/[controller]/10CellLeaderboard/{deviceID}")]
        public string Get10CellLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            //Make into a template for other leaderboards.
            PerformanceTracker pt = new PerformanceTracker("Get10CellLeaderboard");
            GpsExploreContext db = new GpsExploreContext();
            
            List<int> results = db.PlayerData.OrderBy(p => p.t10Cells).Take(10).Select(p => p.t10Cells).ToList();
            int playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.t10Cells).FirstOrDefault();
            int playerRank = db.PlayerData.Where(p => p.t10Cells >= playerScore).Count();
            results.Add(playerRank);
            
            pt.Stop();
            return string.Join("|", results);
        }

        [HttpGet]
        [Route("/[controller]/8CellLeaderboard/{deviceID}")]
        public string Get8CellLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            PerformanceTracker pt = new PerformanceTracker("Get8CellLeaderboard");
            GpsExploreContext db = new GpsExploreContext();

            List<int> results = db.PlayerData.OrderBy(p => p.t8Cells).Take(10).Select(p => p.t8Cells).ToList();
            int playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.t8Cells).FirstOrDefault();
            int playerRank = db.PlayerData.Where(p => p.t8Cells >= playerScore).Count();
            results.Add(playerRank);

            pt.Stop();
            return string.Join("|", results);
        }

        [HttpGet]
        [Route("/[controller]/ScoreLeaderboard/{deviceID}")]
        public string GetScoreLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            //Make into a template for other leaderboards.
            PerformanceTracker pt = new PerformanceTracker("GetScoreLeaderboard");
            GpsExploreContext db = new GpsExploreContext();
            List<int> results = db.PlayerData.OrderBy(p => p.score).Take(10).Select(p => p.score).ToList();
            int playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.score).FirstOrDefault();
            int playerRank = db.PlayerData.Where(p => p.score >= playerScore).Count();
            results.Add(playerRank);

            pt.Stop();
            return string.Join("|", results);
        }

        [HttpGet]
        [Route("/[controller]/DistanceLeaderboard/{deviceID}")]
        public string GetDistanceLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            //Make into a template for other leaderboards.
            PerformanceTracker pt = new PerformanceTracker("GetDistanceLeaderboard");
            GpsExploreContext db = new GpsExploreContext();
            List<int> results = db.PlayerData.OrderBy(p => p.distance).Take(10).Select(p => p.distance).ToList();
            int playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.distance).FirstOrDefault();
            int playerRank = db.PlayerData.Where(p => p.distance >= playerScore).Count();
            results.Add(playerRank);

            pt.Stop();
            return string.Join("|", results);
        }

        [HttpGet]
        [Route("/[controller]/TimeLeaderboard/{deviceID}")]
        public string GetTimeLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            //Make into a template for other leaderboards.
            PerformanceTracker pt = new PerformanceTracker("GetTimeLeaderboard");
            GpsExploreContext db = new GpsExploreContext();
            List<int> results = db.PlayerData.OrderBy(p => p.timePlayed).Take(10).Select(p => p.timePlayed).ToList();
            int playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.timePlayed).FirstOrDefault();
            int playerRank = db.PlayerData.Where(p => p.timePlayed >= playerScore).Count();
            results.Add(playerRank);

            pt.Stop();
            return string.Join("|", results);
        }

        [HttpGet]
        [Route("/[controller]/AvgSpeedLeaderboard/{deviceID}")]
        public string GetAvgSpeedLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            //Make into a template for other leaderboards.
            PerformanceTracker pt = new PerformanceTracker("GetAvgSpeedLeaderboard");
            GpsExploreContext db = new GpsExploreContext();
            //This one does a calculation, will take a bit longer.
            List<int> results = db.PlayerData.Select(p => p.distance / p.timePlayed).OrderBy(p => p).ToList();
            int playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.distance / p.timePlayed).FirstOrDefault();
            int playerRank = results.Where(p => p >= playerScore).Count();
            results = results.Take(10).ToList();
            results.Add(playerRank);

            pt.Stop();
            return string.Join("|", results);
        }

        [HttpGet]
        [Route("/[controller]/TrophiesLeaderboard/{deviceID}")]
        public string GetTrophiesLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            PerformanceTracker pt = new PerformanceTracker("GetTrophiesLeaderboard");
            GpsExploreContext db = new GpsExploreContext();
            List<int> results = db.PlayerData.OrderByDescending(p => p.DateLastTrophyBought).Take(10).Select(p => p.DateLastTrophyBought).ToList();
            int playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.DateLastTrophyBought).FirstOrDefault();
            int playerRank = db.PlayerData.Where(p => p.DateLastTrophyBought <= playerScore).Count(); //This one is a time, so lower is better.
            results.Add(playerRank);

            pt.Stop();
            return string.Join("|", results);
        }
    }
}
