using System;
using System.Collections.Generic;
using DragonDenDevTool.Features.InRaid.Interfaces;
using DragonDenDevTool.Features.InRaid.Services;
using DragonDenDevTool.UI;
using DragonDenDevTool.Utilities;
using EFT;
using UnityEngine;

namespace DragonDenDevTool.Features.InRaid.Tabs;

public sealed class HotspotsTab : IDevTab
{
    readonly DDStyle _s;
    readonly IHotspotService _svc;

    int _subTab;
    Vector2 _scroll;
    string _filter = "";
    string _favName = "";
    float _yLift = 0.20f;

    bool _queueReloadJsons;
    bool _queueAddFav;
    Hotspot _queuedFavToAdd;
    int _queueRemoveFavIndex = -1;

    public HotspotsTab(DDStyle s, IHotspotService svc)
    {
        _s = s;
        _svc = svc ?? new HotspotService();
        _svc.LoadFavorites();
    }

    public string Title => "Hotspots";

    public void Tick()
    {
    }

    public void OnGUI()
    {
        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Toggle(_subTab == 0, "Global Hotspots", _subTab == 0 ? _s.TabOn : _s.TabOff, GUILayout.Height(27)))
                _subTab = 0;
            if (GUILayout.Toggle(_subTab == 1, "Favourite Hotspots", _subTab == 1 ? _s.TabOn : _s.TabOff, GUILayout.Height(27)))
                _subTab = 1;
            GUILayout.FlexibleSpace();
        }

        GUILayout.Space(6);

        using (new GUILayout.HorizontalScope())
        {
            _filter = GUILayout.TextField(_filter ?? "", _s.SearchBox, GUILayout.Width(260), GUILayout.Height(27));

            GUILayout.Label("Y Lift", _s.SmallLabel, GUILayout.Width(44));
            var yStr = GUILayout.TextField(_yLift.ToString("0.00"), _s.TextBox, GUILayout.Width(60), GUILayout.Height(27));
            if (float.TryParse(yStr, out var yParsed)) _yLift = Mathf.Clamp(yParsed, 0f, 3f);

            if (GUILayout.Button("Reload Hotspots", _s.Button, GUILayout.Width(150), GUILayout.Height(27)))
                _queueReloadJsons = true;

            GUILayout.FlexibleSpace();
        }

        GUILayout.Space(6);

        _scroll = GUILayout.BeginScrollView(_scroll);

        if (_subTab == 0)
        {
            DrawList(_svc.GlobalSpots, allowFavourite: true);
            if (_svc.GlobalSpots == null || _svc.GlobalSpots.Count == 0)
                DrawEmptyState($"No global hotspots for map: {MapIdUtil.GetCurrentMapId()}", showGotoGlobal: false);
        }
        else
        {
            DrawFavorites();
            if (_svc.FavoriteSpots == null || _svc.FavoriteSpots.Count == 0)
                DrawEmptyState($"No favourites for: {MapIdUtil.GetCurrentMapId()}", showGotoGlobal: true);
        }

        GUILayout.EndScrollView();

        if (_subTab == 1) DrawAddFavoriteToolbar();

        if (_queueReloadJsons)
        {
            _queueReloadJsons = false;
            _svc.LoadFavorites();
            if (_svc is HotspotService hs) hs.ReloadGlobalsForCurrentMap();
            EFT.Communications.NotificationManager.DisplayMessageNotification("[DevTool] Reloaded favourites and globals");
        }

        if (_queueAddFav)
        {
            _queueAddFav = false;
            _svc.AddFavorite(_queuedFavToAdd);
        }

        if (_queueRemoveFavIndex >= 0)
        {
            var idx = _queueRemoveFavIndex;
            _queueRemoveFavIndex = -1;
            _svc.RemoveFavoriteAt(idx);
        }
    }

    void DrawList(IReadOnlyList<Hotspot> list, bool allowFavourite)
    {
        if (list == null) return;

        foreach (var hs in list)
        {
            if (!PassFilter(hs)) continue;

            using (new GUILayout.VerticalScope(_s.Pill))
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(hs.Name ?? "?", _s.Label, GUILayout.Width(260));
                GUILayout.Label(hs.Category ?? "", _s.SmallLabel, GUILayout.Width(100));
                GUILayout.Label($"{hs.Position.x:0.00}, {hs.Position.y:0.00}, {hs.Position.z:0.00}", _s.SmallLabel, GUILayout.Width(140));

                if (GUILayout.Button("Teleport", _s.Button, GUILayout.Width(110), GUILayout.Height(27)))
                    Teleport(hs);

                if (allowFavourite)
                {
                    var favIndex = FindFavoriteIndex(hs);
                    if (favIndex >= 0)
                    {
                        if (GUILayout.Button("Remove", _s.BtnDanger, GUILayout.Width(120), GUILayout.Height(27)))
                            _queueRemoveFavIndex = favIndex;
                    }
                    else
                    {
                        if (GUILayout.Button("★ Favourite", _s.Button, GUILayout.Width(120), GUILayout.Height(27)))
                            QueueAddFavourite(hs);
                    }
                }

                GUILayout.FlexibleSpace();
            }
        }
    }

    void DrawFavorites()
    {
        var favs = _svc.FavoriteSpots;
        if (favs == null) return;

        for (var i = 0; i < favs.Count; i++)
        {
            var hotspot = favs[i];
            if (!PassFilter(hotspot)) continue;

            using (new GUILayout.VerticalScope(_s.Pill))
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(hotspot.Name ?? "?", _s.Label, GUILayout.Width(260));
                GUILayout.Label(hotspot.Category ?? "Favourite", _s.SmallLabel, GUILayout.Width(100));
                GUILayout.Label($"{hotspot.Position.x:0.00}, {hotspot.Position.y:0.00}, {hotspot.Position.z:0.00}", _s.SmallLabel, GUILayout.Width(140));

                if (GUILayout.Button("Teleport", _s.Button, GUILayout.Width(90), GUILayout.Height(27)))
                    Teleport(hotspot);

                if (GUILayout.Button("Remove", _s.BtnDanger, GUILayout.Width(90), GUILayout.Height(27)))
                    _queueRemoveFavIndex = i;

                GUILayout.FlexibleSpace();
            }
        }
    }

    void DrawAddFavoriteToolbar()
    {
        GUILayout.Space(6);
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label("Name", _s.Label, GUILayout.Width(44));
            _favName = GUILayout.TextField(_favName ?? "", _s.TextBox, GUILayout.Width(240), GUILayout.Height(27));

            if (GUILayout.Button("Add Current Location", _s.Button, GUILayout.Width(200), GUILayout.Height(27)))
                QueueAddCurrentAsFavourite();

            GUILayout.FlexibleSpace();
        }
    }

    void DrawEmptyState(string message, bool showGotoGlobal, Action extraAction = null)
    {
        GUILayout.Space(8);
        using (new GUILayout.VerticalScope(_s.Pill))
        {
            GUILayout.Label(message, _s.SmallLabel);
            GUILayout.Space(4);
            using (new GUILayout.HorizontalScope())
            {
                if (showGotoGlobal && GUILayout.Button("Go to Global", _s.Button, GUILayout.Width(140), GUILayout.Height(27)))
                    _subTab = 0;
                GUILayout.FlexibleSpace();
            }

            extraAction?.Invoke();
        }
    }

    bool PassFilter(Hotspot hs)
    {
        var f = _filter?.Trim();
        if (string.IsNullOrEmpty(f)) return true;
        if (!string.IsNullOrEmpty(hs.Name) && hs.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return !string.IsNullOrEmpty(hs.Category) && hs.Category.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    void QueueAddFavourite(Hotspot hs)
    {
        _queueAddFav = true;
        _queuedFavToAdd = new Hotspot
        {
            Id = null,
            MapId = MapIdUtil.GetCurrentMapId(),
            Name = hs.Name,
            Category = "Favourite",
            Position = hs.Position,
            IsGlobal = false
        };
    }

    void QueueAddCurrentAsFavourite()
    {
        var me = GamePlayerOwner.MyPlayer;
        if (!me) return;
        var pos = me.Transform.position;
        var name = string.IsNullOrWhiteSpace(_favName) ? $"Fav {DateTime.Now:HHmmss}" : _favName.Trim();

        _queueAddFav = true;
        _queuedFavToAdd = new Hotspot
        {
            Id = null,
            MapId = MapIdUtil.GetCurrentMapId(),
            Name = name,
            Category = "Favourite",
            Position = pos,
            IsGlobal = false
        };
        _favName = "";
    }

    int FindFavoriteIndex(Hotspot hs)
    {
        var favs = _svc.FavoriteSpots;
        if (favs == null) return -1;

        for (var i = 0; i < favs.Count; i++)
        {
            var f = favs[i];
            if (!string.Equals(f.MapId, hs.MapId, StringComparison.OrdinalIgnoreCase)) continue;

            if (!string.Equals(f.Name ?? "", hs.Name ?? "", StringComparison.OrdinalIgnoreCase)) continue;

            if ((f.Position - hs.Position).sqrMagnitude > 0.0001f) continue;

            return i;
        }

        return -1;
    }

    void Teleport(Hotspot hs)
    {
        var me = GamePlayerOwner.MyPlayer;
        if (!me) return;

        var up = Mathf.Max(0.05f, _yLift);
        TeleportUtils.SafeTeleport(me, hs.Position, up);
        EFT.Communications.NotificationManager.DisplayMessageNotification($"[DevTool] Teleported to {hs.Name}");
    }
}