# PraxisMapper
An open-source server for location based games. Follow my on <a rel="me" href="https://mastodon.gamedev.place/@Praxismapper">Mastodon</a>

Powered by OpenStreetMap data.

To focus on the player, and to let them play without tracking them.

Source code for a test application, Hypothesis, is available at https://github.com/PraxisMapper/Hypothesis


# Requirements
* Visual Studio 2022 17.0+ Community (or the mac/linux equivalent)
* .NET 7.0 Preview
* MariaDB 10.6+ (Recommended) OR SQL Server 2016+ OR PostgreSQL
* System resources may vary with content.
* * Running a county-sized game (500 square miles/1200 square kilometers)? A server can run with as little as 1GB RAM and storage space, with the webserver and DB on the same box.
* * Running a server for a continent? You'll want at least 64GB of RAM on the DB server, and 200GB+ of storage space to handle map data and drawn tiles.

# Features
* Simple API handles all the baseline needs for a location based game. It handles locations, map tiles, and interactions, you can focus on the gameplay.
* Load data in from OpenStreetMap exports.
* Create map tiles on demand or ahead of time, from the source map or from gameplay data.
* Draw map tiles in multiple styles, and draw gameplay data to overlay tiles.
* Supports any sized area for gameplay. 
* Server stores as little data as possible about users. 
* 3 backend database options: MariaDB is free and simple, PostgreSQL is popular among OpenStreetMap projects, and Microsoft SQL Server for enterprise sized games.

# Performance Examples
* Setting up a county-sized game server (1,200 square miles) takes about 20 minutes (half of which will be drawing baseline maptiles for the game) and needs 500MB of disk space.
* Setting up a state-sized game server (53,000 square miles) takes about 6 hours (Processing files and data will be about 1 hour, the rest is maptile drawing) and uses up 14GB of space.
* Most continents can be converted from source data to working server in under 48 hours of processing time and a little extra planning. North America takes up about 100GB of space for map data, tags, and indexes, without any map tiles drawn. Europe requires significantly more space than NA. 

# How to Use PraxisMapper's APIs
* /MapTile handles all the drawing logic for creating baseline map tiles, or overlays to layer multiple tiles together on your client.
* * Call YourServer/MapTile/Area/{PlusCode} to get a maptile for a gameplay area, or MapTile/AreaPlaceData/{PlusCode} to draw an overlay with gameplay data from the elements.
* /Data handles storing and reading data for players, Places, Areas, and global information or settings.
* * Use GET Data/Area/{PlusCode} to read info from a grid cell, or PUT Data/Area/{PlusCode} to save data to the server
* * Use GET Data/Place/{ID} and PUT Data/Place/{ID} to read and write data based on the items drawn on the map.
* /SecureData allows for entries to be encrypted, blocking them from being viewed by unauthorized users.
* * GET and PUT calls both add a password entry
* * SecureData endpoints are appropriate if you want to attach users to places or store location data, so as to not expose it to other players (or the server owner, if the password is provided by the player)
* See the APIDocs.txt file or the wiki tab on GitHub for a full set of API endpoints and expected values.
* More examples are available in Hypothesis, the example mobile client.
* By default, your game should keeps any player location history stored client-side. The server is for interactions, not tracking. 

At this time, you are expected to have some programming experience to use PraxisMapper for making games. Building a location-based game with PraxisMapper is not currently suitable as a first coding project.
# Minimal Setup Instructions (Windows)
* Unzip all files from PraxisMapper.zip to C:\Praxis.
* Download the smallest usable PBF map extract file you can find for the area you want to cover for gameplay from Geofabrik.de to C:\Praxis
* On OpenStreetMap.org, search for the area you want your game to cover, and write down its Relation ID. (Optional, for smaller area games)
* Install MariaDB and create a service account for PraxisMapper
* Update the config files Larry.config.json and appsettings.json. with your connection string for the database, and the specific relation you want to map out (if you did the optional step for a Relation ID) for Larry. Also, remove all of the lines in the "Kestrel:{}" block of appsettings.json (everything above the "Logging" line) to skip over some SSL setup (these lines may be necessary later, as iOS apps and Javascript web pages that use GPS data require HTTPS)
* Run "Larry -makeServerDb" from the command line. This will create your database, parse your map files, and load them into your database. (This will NOT pre-draw map tiles, to save setup time and disk space)
* Run PraxisMapper.exe. It should fire up, and you should get the words "OK" if you go to http://localhost:5000/Server/Test. If so, congratulations! You have a minimum functional PraxisMapper installation.

# Scale Changes
It is entirely feasible for small games to run the entire server on a single PC, with surprisingly low resources. A US county is often an entirely viable space for a local game, particularly in a testing phase of development.
Huge games will require some manual work to set up. Continent-sized servers, or countries that take up most of a continent, will require deleting indexes after creating the database schema, processing and importing data, then re-creating indexes after import in order to remove a few days from that initial load time. This index juggling process is handled with the -makeServerDb command.
