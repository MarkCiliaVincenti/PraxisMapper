﻿using PraxisCore;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries.Prepared;
using PraxisMapper.Classes;
using System;
using System.Text;
using static PraxisCore.DbTables;
using static PraxisCore.Place;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace PraxisMapper.Controllers
{
    //DataController: For handling generic get/set commands for data by location.
    //The part that actually allows for games to be made using PraxisMapper without editing code. (give or take styles until a style editor exists on an admin interface)

    [Route("[controller]")]
    [ApiController]
    public class DataController : Controller
    {
        //TODO: check this logic in a busy test state. These might need to be some kind of SlimReadWriteLock or equivalent that counts requests, and get removed at 0 instead of after every call
        static ConcurrentDictionary<string, string> incrementLock = new ConcurrentDictionary<string, string>();
        static string dictValue = "1";
        static DateTime lastExpiryPass = DateTime.Now.AddSeconds(-1);

        private readonly IConfiguration Configuration;
        private static IMemoryCache cache;

        public DataController(IConfiguration configuration, IMemoryCache memoryCacheSingleton)
        {
            Configuration = configuration;
            cache = memoryCacheSingleton;

            if (lastExpiryPass < DateTime.Now)
            {
                var db = new PraxisContext();
                db.Database.ExecuteSqlRaw("DELETE FROM CustomDataOsmElements WHERE expiration IS NOT NULL AND expiration < NOW()");
                db.Database.ExecuteSqlRaw("DELETE FROM CustomDataPlusCodes WHERE expiration IS NOT NULL AND expiration < NOW()");
                db.Database.ExecuteSqlRaw("DELETE FROM PlayerData WHERE expiration IS NOT NULL AND expiration < NOW()");
                lastExpiryPass = DateTime.Now.AddMinutes(30);
            }
        }

        //TODO: make all Set* values a Put instead of a Get
        [HttpGet]
        [Route("/[controller]/SetPlusCodeData/{plusCode}/{key}/{value}")]
        [Route("/[controller]/SetPlusCodeData/{plusCode}/{key}/{value}/{expiresIn}")]
        [Route("/[controller]/SetPlusCode/{plusCode}/{key}/{value}")]
        [Route("/[controller]/SetPlusCode/{plusCode}/{key}/{value}/{expiresIn}")]
        public bool SetPlusCodeData(string plusCode, string key, string value, double? expiresIn = null)
        {
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(plusCode)))
                return false;
            return GenericData.SetPlusCodeData(plusCode, key, value, expiresIn);
        }

        [HttpGet]
        [Route("/[controller]/GetPlusCodeData/{plusCode}/{key}")]
        [Route("/[controller]/GetPlusCode/{plusCode}/{key}")]
        public string GetPlusCodeData(string plusCode, string key)
        {
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(plusCode)))
                return "";
            return GenericData.GetPlusCodeData(plusCode, key);
        }

        //TODO: make all Set* values a Put instead of a Get
        [HttpGet]
        [Route("/[controller]/SetPlayerData/{deviceId}/{key}/{value}")]
        [Route("/[controller]/SetPlayerData/{deviceId}/{key}/{value}/{expiresIn}")]
        [Route("/[controller]/SetPlayer/{deviceId}/{key}/{value}")]
        [Route("/[controller]/SetPlayer/{deviceId}/{key}/{value}/{expiresIn}")]
        public bool SetPlayerData(string deviceId, string key, string value, double? expiresIn = null)
        {
            return GenericData.SetPlayerData(deviceId, key, value, expiresIn);
        }

        [HttpGet]
        [Route("/[controller]/GetPlayerData/{deviceId}/{key}")]
        [Route("/[controller]/GetPlayer/{deviceId}/{key}")]
        public string GetPlayerData(string deviceId, string key)
        {
            return GenericData.GetPlayerData(deviceId, key);
        }

        [HttpGet]
        [Route("/[controller]/SetElementData/{elementId}/{key}/{value}")]
        [Route("/[controller]/SetElementData/{elementId}/{key}/{value}/{expiresIn}")]
        [Route("/[controller]/SetElement/{elementId}/{key}/{value}")]
        [Route("/[controller]/SetElement/{elementId}/{key}/{value}/{expiresIn}")]
        public bool SetStoredElementData(Guid elementId, string key, string value, double? expiresIn = null)
        {
            return GenericData.SetStoredElementData(elementId, key, value, expiresIn);
        }

        [HttpGet]
        [Route("/[controller]/GetElementData/{elementId}/{key}")]
        [Route("/[controller]/GetElement/{elementId}/{key}")]
        public string GetElementData(Guid elementId, string key)
        {
            return GenericData.GetElementData(elementId, key);
        }

        [HttpGet]
        [Route("/[controller]/GetAllPlayerData/{deviceId}")]
        public string GetAllPlayerData(string deviceId)
        {
            var data = GenericData.GetAllPlayerData(deviceId);
            StringBuilder sb = new StringBuilder();
            foreach (var d in data)
                sb.Append(d.deviceId).Append("|").Append(d.key).Append("|").AppendLine(d.value);

            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/GetAllDataInPlusCode/{plusCode}")]
        public string GetAllDataInPlusCode(string plusCode)
        {
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(plusCode)))
                return "";
            var data = GenericData.GetAllDataInPlusCode(plusCode);
            StringBuilder sb = new StringBuilder();
            foreach (var d in data)
                sb.Append(d.plusCode).Append("|").Append(d.key).Append("|").AppendLine(d.value);

            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/GetAllDataInOsmElement/{elementId}/")]
        public string GetAllDataInOsmElement(Guid elementId)

        {
            var data = GenericData.GetAllDataInPlace(elementId);
            StringBuilder sb = new StringBuilder();
            foreach (var d in data)
                sb.Append(d.elementId).Append("|").Append(d.key).Append("|").AppendLine(d.value);

            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/SetGlobalData/{key}/{value}")]
        public bool SetGlobalData(string key, string value)
        {
            return GenericData.SetGlobalData(key, value);
        }

        [HttpGet]
        [Route("/[controller]/GetGlobalData/{key}")]
        public string GetGlobalData(string key)
        {
            return GenericData.GetGlobalData(key);
        }

        [HttpGet]
        [Route("/[controller]/GetServerBounds")]
        public string GetServerBounds()
        {
            var bounds = cache.Get<ServerSetting>("settings");
            return bounds.SouthBound + "|" + bounds.WestBound + "|" + bounds.NorthBound + "|" + bounds.EastBound;
        }

        [HttpGet]
        [Route("/[controller]/IncrementPlayerData/{deviceId}/{key}/{changeAmount}")]
        public void IncrementPlayerData(string deviceId, string key, double changeAmount, double? expirationTimer = null)
        {
            string lockKey = deviceId + key;
            incrementLock.TryAdd(lockKey, dictValue);
            lock (incrementLock[lockKey])
            {
                var data = GenericData.GetPlayerData(deviceId, key);
                double val = 0;
                Double.TryParse(data, out val);
                val += changeAmount;
                GenericData.SetPlayerData(deviceId, key, val.ToString(), expirationTimer);
            }
            incrementLock.TryRemove(lockKey,out dictValue);
        }

        [HttpGet]
        [Route("/[controller]/IncrementGlobalData/{key}/{changeAmount}")]
        public void IncrementGlobalData(string key, double changeAmount)
        {
            incrementLock.TryAdd(key, dictValue);
            lock (incrementLock[key])
            {
                var data = GenericData.GetGlobalData(key);
                double val = 0;
                Double.TryParse(data, out val);
                val += changeAmount;
                GenericData.SetGlobalData(key, val.ToString());
            }
            incrementLock.TryRemove(key, out dictValue);
        }

        [HttpGet]
        [Route("/[controller]/IncrementPlusCodeData/{plusCode}/{key}/{changeAmount}")]
        public void IncrementPlusCodeData(string plusCode, string key, double changeAmount, double? expirationTimer = null)
        {
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(plusCode)))
                return;
            string lockKey = plusCode + key;
            incrementLock.TryAdd(lockKey, dictValue);
            lock (incrementLock[lockKey])
            {
                var data = GenericData.GetPlusCodeData(plusCode, key);
                double val = 0;
                Double.TryParse(data, out val);
                val += changeAmount;
                GenericData.SetPlusCodeData(plusCode, key, val.ToString(), expirationTimer);
            }
            
            incrementLock.TryRemove(lockKey, out dictValue);
        }

        [HttpGet]
        [Route("/[controller]/IncrementElementData/{elementId}/{key}/{changeAmount}")]
        public void IncrementElementData(Guid elementId, string key, double changeAmount, double? expirationTimer = null)
        {
            string lockKey = elementId.ToString() + key;
            incrementLock.TryAdd(lockKey, dictValue);
            lock (incrementLock[lockKey])
            {
                var data = GenericData.GetElementData(elementId,key);
                double val = 0;
                Double.TryParse(data, out val);
                val += changeAmount;
                GenericData.SetStoredElementData(elementId, key, val.ToString(), expirationTimer);
            }
            incrementLock.TryRemove(lockKey, out dictValue);
        }

        [HttpGet]
        [Route("/[controller]/GetPlusCodeTerrainData/{plusCode}")]
        [Route("/[controller]/Terrain/{plusCode}")]
        public string GetPlusCodeTerrainData(string plusCode)
        {
            //This function returns 1 line per Cell10, the smallest (and therefore highest priority) item intersecting that cell10.
            PerformanceTracker pt = new PerformanceTracker("GetPlusCodeTerrainData");
            GeoArea box = OpenLocationCode.DecodeValid(plusCode);
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), box))
                return "";            
            var places = GetPlaces(box);
            places = places.Where(p => p.GameElementName != TagParser.defaultStyle.name).ToList();

            StringBuilder sb = new StringBuilder();
            //pluscode|name|type|PrivacyID 

            var data = AreaTypeInfo.SearchArea(ref box, ref places);
            foreach (var d in data)
                sb.Append(d.Key).Append("|").Append(d.Value.Name).Append("|").Append(d.Value.areaType).Append("|").Append(d.Value.StoredOsmElementId).Append("\r\n");
            var results = sb.ToString();
            pt.Stop(plusCode);
            return results;
        }

        [HttpGet]
        [Route("/[controller]/GetPlusCodeTerrainDataFull/{plusCode}")]
        [Route("/[controller]/TerrainFull/{plusCode}")]
        public string GetPlusCodeTerrainDataFull(string plusCode)
        {
            //This function returns 1 line per Cell10 per intersecting element. For an app that needs to know all things in all points.
            PerformanceTracker pt = new PerformanceTracker("GetPlusCodeTerrainDataFull");
            GeoArea box = OpenLocationCode.DecodeValid(plusCode);
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), box))
                return "";
            var places = GetPlaces(box); //, includeGenerated: Configuration.GetValue<bool>("generateAreas")  //All the places in this Cell8
            places = places.Where(p => p.GameElementName != TagParser.defaultStyle.name).ToList();

            StringBuilder sb = new StringBuilder();
            //pluscode|name|type|StoredOsmElementID

            var data = AreaTypeInfo.SearchAreaFull(ref box, ref places);
            foreach (var d in data)
                foreach(var v in d.Value)
                    sb.Append(d.Key).Append("|").Append(v.Name).Append("|").Append(v.areaType).Append("|").Append(v.StoredOsmElementId).Append("\r\n");
            var results = sb.ToString();
            pt.Stop(plusCode);
            return results;
        }

        [HttpGet]
        [Route("/[controller]/GetScoreForPlace/{elementId}")]
        public long GetScoreForPlace(Guid elementId)
        {
            return ScoreData.GetScoreForSinglePlace(elementId);
        }

        [HttpGet]
        [Route("/[controller]/GetDistanceToPlace/{elementId}/{lat}/{lon}")]
        public double GetDistanceToPlace(Guid elementId, double lat, double lon)
        {
            var db = new PraxisContext();
            var place = db.StoredOsmElements.FirstOrDefault(e => e.privacyId == elementId);
            if (place == null) return 0;
            return place.elementGeometry.Distance(new NetTopologySuite.Geometries.Point(lon, lat));
        }

        [HttpGet]
        [Route("/[controller]/GetCenterOfPlace/{elementId}")]
        public string GetCenterOfPlace(Guid elementId)
        {
            var db = new PraxisContext();
            var place = db.StoredOsmElements.FirstOrDefault(e => e.privacyId == elementId);
            if (place == null) return "0|0";
            var center = place.elementGeometry.Centroid;
            return center.Y.ToString() + "|" + center.X.ToString();        
        }

        [HttpGet]
        [Route("/[controller]/DeleteUser/{deviceId}")]
        public int DeleteUser(string deviceId)
        {
            //GDPR compliance requires this to exist and be available to the user. 
            //Custom games that attach players to locations may need additional logic to fully meet legal requirements.
            var db = new PraxisContext();
            var removing = db.PlayerData.Where(p => p.deviceID == deviceId).ToArray();
            db.PlayerData.RemoveRange(removing);
            return db.SaveChanges();
        }
    }
}