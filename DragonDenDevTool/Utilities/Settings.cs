using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace DragonDenDevTool.Utilities;
internal class Settings
{
    private static readonly List<ConfigEntryBase> ConfigEntries = new();

    private const string Debugging = "0. Debug";
    private const string UI    = "1. UI";
    private const string Keys  = "2. Keys";

    public static ConfigEntry<bool> DebugMode { get; set; }
    public static ConfigEntry<KeyboardShortcut> ToggleUI { get; set; }
    public static ConfigEntry<bool> GodModeOnByDefault  { get; set; }
    public static ConfigEntry<float> UpdateInterval { get; private set; }

    public static void Init(ConfigFile config)
    {
        ConfigEntries.Add(DebugMode = config.Bind(Debugging, "Enable Debug Mode", false, new ConfigDescription(
            "Enabling this will show more debug options and logging.", null, new ConfigurationManagerAttributes(), true)));
        
        ConfigEntries.Add(ToggleUI = config.Bind(Keys, "Toggle UI", new KeyboardShortcut(KeyCode.Insert),
            new ConfigDescription(
                "The key to open the Dev Tool UI.", null, new ConfigurationManagerAttributes(), true)));
        
        ConfigEntries.Add(GodModeOnByDefault = config.Bind(UI, "Enable Godmode on Raid Enter", true, new ConfigDescription(
            "Enabling this will give you God Mode on each raid you enter.",  null, new ConfigurationManagerAttributes(), true)));

        ConfigEntries.Add(UpdateInterval = config.Bind(UI, "Update Interval (sec)", 0.25f,
            new ConfigDescription("How often the UI refreshes dynamic data (player pos, bots list).", null, new ConfigurationManagerAttributes(), true)));

        
        ToggleUI.Subscribe(_ => {});
        UpdateInterval.Subscribe(_ => {});
        DebugMode.Subscribe(_ => {});
        GodModeOnByDefault.Subscribe(_ => {});
    
        RecalcOrder();
    }

    private static void RecalcOrder()
    {
        int settingOrder = ConfigEntries.Count;
        foreach (var entry in ConfigEntries)
        {
            if (entry.Description.Tags != null && entry.Description.Tags.Length > 0 && entry.Description.Tags[0] is ConfigurationManagerAttributes a)
                a.Order = settingOrder;
            settingOrder--;
        }
    }

    public static bool IsKeyPressed(KeyboardShortcut key, bool holdKey = false)
    {
        if (holdKey)
        {
            if (!UnityInput.Current.GetKey(key.MainKey)) return false;
            foreach (var m in key.Modifiers) if (!UnityInput.Current.GetKey(m)) return false;
        }
        else
        {
            if (!UnityInput.Current.GetKeyDown(key.MainKey)) return false;
            foreach (var m in key.Modifiers) if (!UnityInput.Current.GetKey(m)) return false;
        }
        return true;
    }

    public static bool IsKeyReleased(KeyboardShortcut key)
    {
        if (!UnityInput.Current.GetKey(key.MainKey))
        {
            foreach (var m in key.Modifiers) if (!UnityInput.Current.GetKey(m)) return false;
            return true;
        }
        return false;
    }
}

internal static class SettingExtensions
{
    public static void Subscribe<T>(this ConfigEntry<T> configEntry, Action<T> onChange, bool notification = false)
    {
        configEntry.SettingChanged += (_, _) =>
        {
            onChange(configEntry.Value);
            if (notification)
                EFT.Communications.NotificationManager.DisplayMessageNotification($"[DragonDenDevTool] Setting {configEntry.Value} changed to {configEntry.Value}");
        };
    }

    public static void Bind<T>(this ConfigEntry<T> configEntry, Action<T> onChange, bool notification = false)
    {
        configEntry.Subscribe(onChange, notification);
        onChange(configEntry.Value);
    }
}