using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DragonDenDevTool.Features.InRaid.Interfaces;
using DragonDenDevTool.UI;
using DragonDenDevTool.Utilities;
using EFT;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DragonDenDevTool.Features.InRaid.Tabs;

public sealed class HotspotMakerTab : IDevTab
{
    readonly DDStyle _style;

    string _mapId = "";
    string _name = "";
    string _category = "Misc";

    string _x = "";
    string _y = "";
    string _z = "";

    float _lift = 0.20f;
    Vector2 _scroll;

    public HotspotMakerTab(DDStyle style)
    {
        _style = style;
        try
        {
            _mapId = MapIdUtil.GetCurrentMapId() ?? "";
        }
        catch
        {
            /* good girl action */
        }
    }

    public string Title => "Hotspot Maker";

    public void Tick()
    {
    }

    public void OnGUI()
    {
        var path = DevPaths.HotspotsGlobalFile;

        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label("File", _style.SmallLabel, GUILayout.Width(36));
            GUILayout.Label(path, _style.Muted);
            GUILayout.FlexibleSpace();
        }

        GUILayout.Space(4);
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label("Map", _style.Label, GUILayout.Width(40));
            GUILayout.Label(_mapId ?? "", _style.Label, GUILayout.Width(180));

            GUILayout.Label("Name", _style.Label, GUILayout.Width(44));
            _name = GUILayout.TextField(_name ?? "", _style.TextBox, GUILayout.Width(220), GUILayout.Height(27));

            GUILayout.Label("Category", _style.Label, GUILayout.Width(64));
            _category = GUILayout.TextField(_category ?? "", _style.TextBox, GUILayout.Width(140), GUILayout.Height(27));

            GUILayout.FlexibleSpace();
        }

        GUILayout.Space(4);
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label("X", _style.SmallLabel, GUILayout.Width(14));
            _x = GUILayout.TextField(_x ?? "", _style.TextBox, GUILayout.Width(75), GUILayout.Height(27));
            GUILayout.Label("Y", _style.SmallLabel, GUILayout.Width(14));
            _y = GUILayout.TextField(_y ?? "", _style.TextBox, GUILayout.Width(75), GUILayout.Height(27));
            GUILayout.Label("Z", _style.SmallLabel, GUILayout.Width(14));
            _z = GUILayout.TextField(_z ?? "", _style.TextBox, GUILayout.Width(75), GUILayout.Height(27));

            if (GUILayout.Button("Fetch Position", _style.Button, GUILayout.Width(180), GUILayout.Height(27)))
                FetchPosition();

            GUILayout.Label("Y Offset", _style.SmallLabel, GUILayout.Width(50));
            var liftStr = GUILayout.TextField(_lift.ToString("0.00", CultureInfo.InvariantCulture), _style.TextBox, GUILayout.Width(60), GUILayout.Height(27));
            if (float.TryParse(liftStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var liftParsed))
                _lift = Mathf.Clamp(liftParsed, 0f, 3f);

            if (GUILayout.Button("Teleport Test", _style.Button, GUILayout.Width(120), GUILayout.Height(27)))
                TryTeleportPreview();

            GUILayout.FlexibleSpace();
        }

        GUILayout.Space(6);
        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add To File", _style.Button, GUILayout.Width(140), GUILayout.Height(27)))
                AddToFile(path);
            if (GUILayout.Button("Fetch & Add To File", _style.Button, GUILayout.Width(140), GUILayout.Height(27)))
            {
                FetchPosition();
                AddToFile(path);
            }

            if (GUILayout.Button("Open Folder", _style.Button, GUILayout.Width(120), GUILayout.Height(27)))
            {
                try
                {
                    DevPaths.EnsureFolders();
                    Process.Start(DevPaths.RootFolder);
                }
                catch
                {
                    /* good girl action */
                }
            }

            GUILayout.FlexibleSpace();
        }

        GUILayout.Space(6);
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(80));
        GUILayout.EndScrollView();
    }

    void FetchPosition()
    {
        var me = GamePlayerOwner.MyPlayer;
        if (!me)
        {
            EFT.Communications.NotificationManager.DisplayMessageNotification("[DevTool] No Player Found... This is weird!?");
            Plugin.Logger.LogError("[DevTool] No Player Found... This is weird!?");
            return;
        }

        try
        {
            _mapId = MapIdUtil.GetCurrentMapId() ?? _mapId;
        }
        catch
        {
            /* good girl action */
        }

        var pos = me.Transform.position;
        _x = pos.x.ToString("0.###", CultureInfo.InvariantCulture);
        _y = pos.y.ToString("0.###", CultureInfo.InvariantCulture);
        _z = pos.z.ToString("0.###", CultureInfo.InvariantCulture);
        EFT.Communications.NotificationManager.DisplayMessageNotification($"Fetched Position {_x},{_y},{_z}");
    }

    void TryTeleportPreview()
    {
        var player = GamePlayerOwner.MyPlayer;
        if (!player)
        {
            Plugin.Logger.LogError("[DevTool] No Player Found... This is weird!?");
            EFT.Communications.NotificationManager.DisplayMessageNotification("[DevTool] No Player Found... This is weird!?");
            return;
        }

        if (!TryParseFields(out var pos))
        {
            Plugin.Logger.LogError("[DevTool] Invalid  Fields in Position");
            EFT.Communications.NotificationManager.DisplayMessageNotification("[DevTool] Invalid Fields in Position");
            return;
        }

        var up = Mathf.Max(0.05f, _lift);
        var tp = new Vector3(pos.x, pos.y + up, pos.z);
        TeleportUtils.SafeTeleport(player, tp);

        var rot = player.Transform.rotation.eulerAngles;
        player.Transform.rotation = Quaternion.Euler(rot);

        EFT.Communications.NotificationManager.DisplayMessageNotification($"[DevTool] Teleported to {tp}");
    }

    void AddToFile(string path)
    {
        //FetchPosition();
        
        if (!TryParseFields(out var pos))
        {
            Plugin.Logger.LogError("[DevTool] Invalid  Fields in Position");
            EFT.Communications.NotificationManager.DisplayMessageNotification("[DevTool] Invalid Fields in Position");
            return;
        }

        if (string.IsNullOrWhiteSpace(_mapId))
        {
            Plugin.Logger.LogError("[DevTool] No Map ID found");
            EFT.Communications.NotificationManager.DisplayMessageNotification("[DevTool] No Map ID found");
            return;
        }

        var id = BuildId(_mapId, _name, _category);
        var entry = new Hotspot
        {
            Id = id,
            MapId = _mapId.Trim(),
            Name = string.IsNullOrWhiteSpace(_name) ? $"HS_{DateTime.Now:HHmmss}" : _name.Trim(),
            Category = string.IsNullOrWhiteSpace(_category) ? "Misc" : _category.Trim(),
            Position = pos
        };

        try
        {
            DevPaths.EnsureFolders();
            if (!File.Exists(path)) WriteNewFileWith(entry, path);
            else InsertEntry(path, entry);
            EFT.Communications.NotificationManager.DisplayMessageNotification($"Added {_name} for {_mapId}.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError("[DevTool] Failed to add to file: " + ex.Message);
            EFT.Communications.NotificationManager.DisplayMessageNotification("[DevTool] Failed to add to file.. See Console.");
        }
    }

    bool TryParseFields(out Vector3 pos)
    {
        pos = Vector3.zero;
        var fx = float.TryParse(_x, NumberStyles.Float, CultureInfo.InvariantCulture, out var px);
        var fy = float.TryParse(_y, NumberStyles.Float, CultureInfo.InvariantCulture, out var py);
        var fz = float.TryParse(_z, NumberStyles.Float, CultureInfo.InvariantCulture, out var pz);
        if (!(fx && fy && fz)) return false;
        pos = new Vector3(px, py, pz);
        return true;
    }

    static string BuildId(string mapId, string name, string cat)
    {
        var m = (mapId ?? "").Trim().ToLowerInvariant();
        var n = (name ?? "").Trim().ToLowerInvariant();
        var c = (cat ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(n)) n = "spot";
        n = n.Replace(" ", "_");
        c = string.IsNullOrEmpty(c) ? "misc" : c.Replace(" ", "_");
        return $"{m}_{c}_{n}";
    }

    static void WriteNewFileWith(Hotspot first, string path, bool includeFirst = true)
    {
        var mapKey = first.MapId ?? "";
        var root = new JObject();
        var arr = new JArray();
        if (includeFirst) arr.Add(FormatEntry(first));
        root[mapKey] = arr;
        var json = root.ToString(Formatting.Indented);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    void InsertEntry(string path, Hotspot entry)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        JObject root;
        try
        {
            root = JObject.Parse(text);
        }
        catch
        {
            WriteNewFileWith(entry, path);
            return;
        }

        var key = entry.MapId ?? "";
        if (!root.TryGetValue(key, StringComparison.Ordinal, out var token) || token.Type != JTokenType.Array)
            root[key] = new JArray();

        var arr = (JArray)root[key];

        var newId = entry.Id ?? "";
        var exists = arr != null && arr.Select(t => (string)t["Id"]).Any(tid => string.Equals(tid, newId, StringComparison.OrdinalIgnoreCase));
        if (!exists)
            if (arr != null)
                arr.Add(FormatEntry(entry));

        var json = root.ToString(Formatting.Indented);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    static JObject FormatEntry(Hotspot e)
    {
        return new JObject
        {
            ["Id"] = e.Id ?? "",
            ["MapId"] = e.MapId ?? "",
            ["Name"] = e.Name ?? "",
            ["Category"] = string.IsNullOrEmpty(e.Category) ? "Misc" : e.Category,
            ["X"] = e.Position.x,
            ["Y"] = e.Position.y,
            ["Z"] = e.Position.z
        };
    }
}