using System;
using Comfort.Common;
using DragonDenDevTool.Features.InRaid.Interfaces;
using DragonDenDevTool.Features.InRaid.Services;
using DragonDenDevTool.UI;
using DragonDenDevTool.Utilities;
using EFT;
using UnityEngine;

namespace DragonDenDevTool.Features.InRaid.Tabs;

public sealed class BotsTab : IDevTab
{
    readonly DDStyle _s;
    readonly IBotService _bots;

    string _filter = "";
    string[] _roles;
    int _roleIndex;
    string _spawnAmtStr = "1";
    (string id, string nick, string side)[] _list = Array.Empty<(string, string, string)>();

    bool _rolePopupOpen;
    Vector2 _roleScroll;

    const float InteractionSuppress = 0.25f;
    float _nextRefreshAt;
    float _suppressRefreshUntil;

    public BotsTab(DDStyle s, IBotService bots)
    {
        _s = s;
        _bots = bots ?? new BotService();
        _roles = _bots.GetAllRoleNames() ?? Array.Empty<string>();
        _roleIndex = Mathf.Clamp(_roleIndex, 0, Mathf.Max(0, _roles.Length - 1));
        RefreshList();
        _nextRefreshAt = Time.realtimeSinceStartup + Settings.UpdateInterval.Value;
    }

    public string Title => "Bots";

    public void Tick()
    {
        var now = Time.realtimeSinceStartup;
        if (!(now >= _nextRefreshAt) || !(now >= _suppressRefreshUntil)) return;
        _nextRefreshAt = now + Settings.UpdateInterval.Value;
        RefreshList();
    }

    public void OnGUI()
    {
        if (Event.current != null)
        {
            if (Event.current.isMouse || Event.current.isKey)
                _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
            if (GUIUtility.hotControl != 0)
                _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
        }

        using (new GUILayout.HorizontalScope())
        {
            GUI.SetNextControlName("BotsFilterInput");
            _filter = GUILayout.TextField(_filter ?? "", _s.SearchBox, GUILayout.Width(320), GUILayout.Height(27));

            if (GUILayout.Button("Filter", _s.Button, GUILayout.Width(90), GUILayout.Height(27)))
            {
                RefreshList();
                _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
            }

            if (GUILayout.Button("Clear", _s.Button, GUILayout.Width(80), GUILayout.Height(27)))
            {
                _filter = "";
                RefreshList();
                _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
            }

            if (GUILayout.Button("Refresh", _s.Button, GUILayout.Width(90), GUILayout.Height(27)))
            {
                RefreshList();
                _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
            }

            if (GUILayout.Button("Teleport All", _s.Button, GUILayout.Width(120), GUILayout.Height(27)))
            {
                TeleportAll();
                _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
            }

            if (GUILayout.Button("Kill All", _s.Button, GUILayout.Width(100), GUILayout.Height(27)))
            {
                _bots.KillAllBots();
                _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
            }
            
            GUILayout.FlexibleSpace();
        }
        
        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Kill PMCs", _s.Button, GUILayout.Width(120), GUILayout.Height(27)))
            {
                _bots.KillAllSpecificBots(EBotType.Pmcs);
            }

            if (GUILayout.Button("Kill Scavs", _s.Button, GUILayout.Width(120), GUILayout.Height(27)))
            {
                _bots.KillAllSpecificBots(EBotType.Scavs);
            }

            if (GUILayout.Button("Kill Bosses", _s.Button, GUILayout.Width(120), GUILayout.Height(27)))
            {
                _bots.KillAllSpecificBots(EBotType.Bosses);
            }

            if (GUILayout.Button("Spawn Usec", _s.Button, GUILayout.Width(120), GUILayout.Height(27)))
            {
                _bots.SpawnBotAsync(WildSpawnType.pmcUSEC, 1);
            }

            if (GUILayout.Button("Spawn Bear", _s.Button, GUILayout.Width(120), GUILayout.Height(27)))
            {
                _bots.SpawnBotAsync(WildSpawnType.pmcBEAR, 1);
            }

            if (GUILayout.Button("Spawn Scav", _s.Button, GUILayout.Width(120), GUILayout.Height(27)))
            {
                _bots.SpawnBotAsync(WildSpawnType.assault, 1);
            }
            
            GUILayout.FlexibleSpace();
        }

        GUILayout.Space(6);
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label("Role:", _s.Label, GUILayout.Width(36));
            var cur = (_roles != null && _roles.Length > 0) ? _roles[Mathf.Clamp(_roleIndex, 0, _roles.Length - 1)] : "assault";
            if (GUILayout.Button(cur + "  ▾", _s.Button, GUILayout.Width(260), GUILayout.Height(27)))
            {
                _rolePopupOpen = !_rolePopupOpen;
                _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
            }

            GUILayout.Space(10);
            GUI.SetNextControlName("AmountInput");
            GUILayout.Label("Amount:", _s.Label, GUILayout.Width(56));
            _spawnAmtStr = GUILayout.TextField(_spawnAmtStr ?? "1", _s.TextBox, GUILayout.Width(50), GUILayout.Height(27));
            if (GUILayout.Button("Spawn", _s.Button, GUILayout.Width(80), GUILayout.Height(27)))
            {
                var roleName = _roles is { Length: > 0 } ? _roles[_roleIndex] : "assault";
                if (Enum.TryParse(roleName, true, out WildSpawnType wst))
                {
                    if (!int.TryParse(_spawnAmtStr, out var amt)) amt = 1;
                    _ = _bots.SpawnBotAsync(wst, Mathf.Clamp(amt, 1, 50));
                }
                else EFT.Communications.NotificationManager.DisplayMessageNotification("[DevTool] Invalid role.");

                _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
            }

            GUILayout.FlexibleSpace();
        }

        var e = Event.current;
        if (e != null && (e.type == EventType.KeyDown || e.type == EventType.KeyUp) &&
            (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
        {
            if (GUI.GetNameOfFocusedControl() == "BotsFilterInput")
            {
                GUI.FocusControl(string.Empty);
                GUIUtility.keyboardControl = 0;
                RefreshList();
                _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
                e.Use();
            }
            else if (GUI.GetNameOfFocusedControl() == "AmountInput")
            {
                GUI.FocusControl(String.Empty);
                GUIUtility.keyboardControl = 0;
                e.Use();
            }
        }

        if (_rolePopupOpen)
        {
            GUILayout.Space(4);
            using (new GUILayout.VerticalScope(_s.PopupBg))
            {
                _roleScroll = GUILayout.BeginScrollView(_roleScroll, GUILayout.Height(220));
                for (int i = 0; i < (_roles?.Length ?? 0); i++)
                {
                    var sel = (i == _roleIndex);
                    var label = sel ? $"• {_roles[i]}" : $"   {_roles[i]}";
                    if (!GUILayout.Button(label, _s.Button, GUILayout.Height(27))) continue;
                    _roleIndex = i;
                    _rolePopupOpen = false;
                    _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
                }

                GUILayout.EndScrollView();
            }
        }

        GUILayout.Space(6);
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label($"Total: {_bots.RegisterBotCount()}    Filtered: {_list.Length}", _s.SmallLabel);
            GUILayout.FlexibleSpace();
        }

        GUILayout.Space(6);
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label("Id", _s.ColumnHeader, GUILayout.Width(220));
            GUILayout.Label("Nick", _s.ColumnHeader, GUILayout.Width(200));
            GUILayout.Label("Side", _s.ColumnHeader, GUILayout.Width(90));
            GUILayout.Label("Actions", _s.ColumnHeader, GUILayout.Width(160));
            GUILayout.FlexibleSpace();
        }

        foreach (var b in _list)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(b.id, _s.Label, GUILayout.Width(220));
                GUILayout.Label(b.nick, _s.Label, GUILayout.Width(200));
                GUILayout.Label(b.side, _s.Label, GUILayout.Width(90));
                if (GUILayout.Button("To Me", _s.Button, GUILayout.Width(70), GUILayout.Height(27)))
                {
                    _bots.TeleportBotToMeById(b.id);
                    _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
                }

                if (GUILayout.Button("Me To Them", _s.Button, GUILayout.Width(100), GUILayout.Height(27)))
                {
                    _bots.TeleportMeToBot(b.id);
                    _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
                }

                if (GUILayout.Button("Kill", _s.Button, GUILayout.Width(70), GUILayout.Height(27)))
                {
                    _bots.KillBotById(b.id);
                    _suppressRefreshUntil = Time.realtimeSinceStartup + InteractionSuppress;
                }

                GUILayout.FlexibleSpace();
            }
        }

        if (_list.Length != 0) return;
        GUILayout.Space(8);
        GUILayout.Label("", _s.Label);
    }

    void RefreshList()
    {
        var lst = _bots.ListBots(_filter);
        _list = lst.ToArray();
    }

    void TeleportAll()
    {
        try
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            var me = gameWorld?.MainPlayer;
            if (!gameWorld || !me) return;
            _bots.TeleportAllBotsToMe(me, 2f);
        }
        catch
        {
            /* good girl action */
        }
    }
}