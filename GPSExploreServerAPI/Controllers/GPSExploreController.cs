﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;
using GPSExploreServerAPI.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GPSExploreServerAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class GPSExploreController : ControllerBase
    {
        /* functions needed
         * update player stats (on login, client attempts to send playerData row, plus a couple of Count() calls, and their device ID (or google games id or something)
         * get leaderboards
         * -subboards: Most small cells, most big cells, highest score, most distance, most time, fastest avg speed (distance/time), most coffees purchased, time to all trophies, 
         * Ties should be broken by date (so, who most recently did the thing that set the score), which means tracking more data client-side.
        */

        //Session is not enabled by default on API projects, which is correct.

        [HttpPost]
        [Route("/[controller]/UploadData")] //use this to tag endpoints correctly.
        public string UploadData() //this was't pulling allData out as a string parameter from the body request from Solar2D.
        {
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

            if (insert)
                db.PlayerData.Add(data);

            //TODO: add cheat detection. Mark any input that's blatantly impossible.

            db.SaveChanges();

            return "OK";
        }

        [HttpGet]
        [Route("/[controller]/test")]
        public string TestDummyEndpoint()
        {
            //For debug purposes to confirm the server is running and reachable.
            return "OK";
        }

        [HttpGet]
        [Route("/[controller]/10CellLeaderboard/{deviceID}")]
        public string Get10CellLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            //Make into a template for other leaderboards.
            GpsExploreContext db = new GpsExploreContext();
            List<int> results = db.PlayerData.OrderBy(p => p.t10Cells).Take(10).Select(p => p.t10Cells).ToList();
            results.Add(db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.t10Cells).FirstOrDefault()); //TODO: fix this to get the rownumber of the current player to indicate their place. Already know their value.

            return string.Join("|", results);
        }


    }
}
