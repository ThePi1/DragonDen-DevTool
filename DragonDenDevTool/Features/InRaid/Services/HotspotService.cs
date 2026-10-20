using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DragonDenDevTool.Features.InRaid.Interfaces;
using DragonDenDevTool.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DragonDenDevTool.Features.InRaid.Services;

public sealed class HotspotService : IHotspotService
{
    readonly List<Hotspot> _favorites = new List<Hotspot>();
    List<Hotspot> _globalCache = new List<Hotspot>();
    string _cachedMapId;

    class FavRow
    {
        public string MapId;
        public string Name;
        public string Category;
        public float X;
        public float Y;
        public float Z;
    }

    class FavFile
    {
        public List<FavRow> Items = new List<FavRow>();
    }

    static string CurrentMapId()
    {
        var mapId = MapIdUtil.GetCurrentMapId();
        return string.IsNullOrEmpty(mapId) ? "" : mapId;
    }
    
    public void ReloadGlobalsForCurrentMap()
    {
        _cachedMapId = null;
        EnsureGlobalForCurrentMap();
    }

    void EnsureGlobalForCurrentMap()
    {
        var map = CurrentMapId();
        if (_cachedMapId == map) return;
        _cachedMapId = map;
        _globalCache = LoadGlobalForMap(map);
    }
    
    List<Hotspot> LoadGlobalForMap(string map)
    {
        var list = new List<Hotspot>();
        try
        {
            DevPaths.EnsureFolders();
            if (string.IsNullOrEmpty(map) || !File.Exists(DevPaths.HotspotsGlobalFile)) return list;

            var json = File.ReadAllText(DevPaths.HotspotsGlobalFile);
            if (string.IsNullOrWhiteSpace(json)) return list;

            var root = JToken.Parse(json);
            IEnumerable<JToken> rows = null;

            var itemsToken = root["Items"];
            if (itemsToken != null && itemsToken.Type == JTokenType.Array)
                rows = itemsToken;
            else
            {
                var mapToken = root[map];
                if (mapToken != null && mapToken.Type == JTokenType.Array)
                    rows = mapToken;
            }

            if (rows == null) return list;

            foreach (var it in rows)
            {
                var id = (string)it["Id"];
                var mapId = (string)it["MapId"];
                var name = (string)it["Name"];
                var category = (string)it["Category"];
                var x = (float?)it["X"] ?? 0f;
                var y = (float?)it["Y"] ?? 0f;
                var z = (float?)it["Z"] ?? 0f;

                list.Add(new Hotspot
                {
                    Id = id,
                    MapId = string.IsNullOrEmpty(mapId) ? map : mapId,
                    Name = name ?? "",
                    Category = string.IsNullOrEmpty(category) ? "Misc" : category,
                    Position = new Vector3(x, y, z)
                });
            }
        }
        catch
        {
            /* good girl action */
        }
        return list
            .OrderBy(h => h.Name ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Category ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Id ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<Hotspot> GlobalSpots
    {
        get
        {
            EnsureGlobalForCurrentMap();
            return _globalCache;
        }
    }

    public IReadOnlyList<Hotspot> FavoriteSpots
    {
        get
        {
            var map = CurrentMapId();
            return _favorites.Where(f => string.Equals(f.MapId, map, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    public void LoadFavorites()
    {
        try
        {
            _favorites.Clear();
            DevPaths.EnsureFolders();
            if (!File.Exists(DevPaths.HotspotsFavoritesFile)) return;

            var json = File.ReadAllText(DevPaths.HotspotsFavoritesFile);
            if (string.IsNullOrWhiteSpace(json)) return;

            var data = JsonConvert.DeserializeObject<FavFile>(json);
            if (data?.Items == null) return;

            foreach (var it in data.Items)
            {
                _favorites.Add(new Hotspot
                {
                    Id = null,
                    MapId = it.MapId ?? "",
                    Name = it.Name ?? "",
                    Category = it.Category ?? "Favourite",
                    Position = new Vector3(it.X, it.Y, it.Z)
                });
            }
        }
        catch
        {
            /* good girl action */
        }
    }

    public void SaveFavorites()
    {
        try
        {
            DevPaths.EnsureFolders();
            var data = new FavFile
            {
                Items = _favorites.Select(f => new FavRow
                {
                    MapId = f.MapId ?? "",
                    Name = f.Name ?? "",
                    Category = string.IsNullOrEmpty(f.Category) ? "Favourite" : f.Category,
                    X = f.Position.x,
                    Y = f.Position.y,
                    Z = f.Position.z
                }).ToList()
            };
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(DevPaths.HotspotsFavoritesFile, json);
        }
        catch
        {
            /* good girl action */
        }
    }

    public void AddFavorite(Hotspot spot)
    {
        if (spot == null) return;
        if (string.IsNullOrEmpty(spot.MapId)) spot.MapId = CurrentMapId();
        if (string.IsNullOrEmpty(spot.Category)) spot.Category = "Favourite";
        _favorites.Add(spot);
        EFT.Communications.NotificationManager.DisplayMessageNotification($"[DevTool] Added {spot.Name} to Favorites");
        SaveFavorites();
    }

    public void RemoveFavoriteAt(int index)
    {
        var favs = FavoriteSpots;
        if (index < 0 || index >= favs.Count) return;

        var hotspotToRemove = favs[index];
        var indexInAll = _favorites.FindIndex(hotspot =>
            hotspot.Name == hotspotToRemove.Name &&
            hotspot.MapId == hotspotToRemove.MapId &&
            (hotspot.Position - hotspotToRemove.Position).sqrMagnitude < 0.0001f);

        if (indexInAll >= 0) _favorites.RemoveAt(indexInAll);
        EFT.Communications.NotificationManager.DisplayMessageNotification($"[DevTool] Removed  {hotspotToRemove.Name} from Favorites");
        SaveFavorites();
    }
}