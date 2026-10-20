using System;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using DragonDenDevTool.Features.InRaid.Interfaces;
using DragonDenDevTool.Utilities;
using EFT;
using EFT.HealthSystem;
using UnityEngine;

namespace DragonDenDevTool.Features.InRaid.Services;

public class PlayerService : IPlayerService
{
    Player _player;
    ActiveHealthController _healthController;
    bool _godMode;
    bool _noFall;
    bool _instantSearch;
    bool _infiniteStamina;

    bool? _originalAttentionEliteExtraLootExp;
    float? _originalAttentionEliteLuckySearch;
    bool? _originalIntellectEliteContainerScope;

    public void TeleportAllTo(Player anchor, float forwardOffset)
    {
        try
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            if (!gameWorld || !anchor) return;
            var basePos = anchor.Transform.position + anchor.Transform.forward * forwardOffset;
            var offset = 0f;
            foreach (var p in gameWorld.RegisteredPlayers.OfType<Player>())
            {
                if (p.IsYourPlayer) continue;
                var pos = basePos + new Vector3(offset, 0f, 0f);
                p.Transform.position = pos;
                offset += 0.75f;
            }

            EFT.Communications.NotificationManager.DisplayMessageNotification("[DevTool] Teleported all to you.");
        }
        catch
        {
            /* good girl action */
        }
    }

    public bool TryKill(Player target)
    {
        try
        {
            var healthController = target?.HealthController;
            if (healthController == null) return false;

            var type = healthController.GetType();
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var applyDamage in methods)
            {
                if (!applyDamage.Name.Contains("ApplyDamage")) continue;
                var applyDamageParameter = applyDamage.GetParameters();
                if (applyDamageParameter.Length < 3 ||
                    !applyDamageParameter[0].ParameterType.IsEnum || applyDamageParameter[0].ParameterType.Name != nameof(EBodyPart) ||
                    applyDamageParameter[1].ParameterType != typeof(float) ||
                    !applyDamageParameter[2].ParameterType.IsEnum || applyDamageParameter[2].ParameterType.Name != nameof(EDamageType)) continue;
                applyDamage.Invoke(healthController, new object[] { EBodyPart.Head, 9999f, EDamageType.Bullet });
                return true;
            }

            foreach (var m in methods)
            {
                if (!m.Name.Contains("ApplyDamage")) continue;
                var ps = m.GetParameters();
                if (ps.Length < 2 ||
                    !ps[0].ParameterType.IsEnum || ps[0].ParameterType.Name != nameof(EBodyPart) ||
                    ps[1].ParameterType != typeof(float)) continue;
                m.Invoke(healthController, new object[] { EBodyPart.Head, 9999f });
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    void Resolve()
    {
        var gameWorld = Singleton<GameWorld>.Instance;
        _player = gameWorld?.MainPlayer;
        if (!_player && gameWorld?.RegisteredPlayers != null)
            _player = gameWorld.RegisteredPlayers.OfType<Player>().FirstOrDefault(x => x && x.IsYourPlayer)
                 ?? gameWorld.RegisteredPlayers.OfType<Player>().FirstOrDefault();
        _healthController = _player?.HealthController as ActiveHealthController;
    }

    public Structs.RaidInfo RaidInfo()
    {
        var gameWorld = Singleton<GameWorld>.Instance;
        if (!gameWorld) return default;

        var map = ReflectionHelper.SafeProp(gameWorld, "LocationId") ?? ReflectionHelper.SafeProp(gameWorld, "Location") ?? "Unknown";
        var total = gameWorld.RegisteredPlayers?.Count ?? 0;

        var pmc = 0;
        var scavs = 0;

        var list = gameWorld.RegisteredPlayers?.OfType<Player>();
        if (list != null)
        {
            foreach (var p in list)
            {
                var isAI = ReflectionHelper.TryReadBool(p, "IsAI") ?? false;
                if (isAI)
                {
                }

                var side = ReflectionHelper.SafeProp(p.Profile, "Side") ?? "";
                var isScav = side.IndexOf("scav", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isScav) scavs++;
                else pmc++;
            }
        }

        var secs = Time.timeSinceLevelLoad;
        return new Structs.RaidInfo
        {
            Map = map,
            Total = total,
            PMC = pmc,
            Scavs = scavs,
            Min = Mathf.FloorToInt(secs / 60f),
            Sec = Mathf.FloorToInt(secs % 60f)
        };
    }

    public Structs.PlayerInfo PlayerInfo()
    {
        Resolve();
        var player = _player;
        if (!player) return default;
        var pos = player.Transform.position;
        string name = player.Profile.Nickname;
        string side = player.Profile.Side.ToString();
        string lvl = EFT.ProfileInfo.GetLevel(player.Profile.Experience).ToString();

        return new Structs.PlayerInfo
        {
            Name = name,
            Side = side,
            Level = lvl,
            Pos = pos,
            Yaw = player.Transform.rotation.eulerAngles.y
        };
    }

    public Structs.VitalsInfo VitalsInfo()
    {
        Resolve();
        var healthController = _healthController;
        if (healthController == null) return default;
        try
        {
            var common = healthController.GetBodyPartHealth(EBodyPart.Common);
            var energy = healthController.Energy;
            var hydration = healthController.Hydration;
            var bodyTemp = healthController.Temperature.Current;

            return new Structs.VitalsInfo
            {
                HPCurrent = common.Current,
                HPMax = common.Maximum,
                EnergyCurrent = energy.Current,
                EnergyMax = energy.Maximum,
                HydrationCurrent = hydration.Current,
                HydrationMax = hydration.Maximum,
                BodyTemp = bodyTemp,
                DamageCoeff = healthController.DamageCoeff
            };
        }
        catch
        {
            return default;
        }
    }

    public void SetGodMode(bool on)
    {
        _godMode = on;
        ApplyToggles();
    }

    public void SetNoFall(bool on)
    {
        _noFall = on;
        ApplyToggles();
    }

    public void SetInstantSearch(bool on)
    {
        _instantSearch = on;
        ApplyToggles();
    }

    public void SetInfiniteStamina(bool on)
    {
        _infiniteStamina = on;
        ApplyToggles();
    }

    public void ApplyToggles()
    {
        Resolve();
        if (!_player || _healthController == null) return;

        try
        {
            _healthController.DamageCoeff = _godMode ? 0f : 1f;
            if (_noFall) _healthController.FallSafeHeight = 9999999f;
            ApplyInstantSearch(_player, _instantSearch);
            ApplyInfiniteStamina(_player, _infiniteStamina);
        }
        catch
        {
            /* good girl action */
        }
    }

    public void FullHealAll()
    {
        Resolve();
        if (_healthController == null) return;
        FullHeal(_healthController, EBodyPart.Head);
        FullHeal(_healthController, EBodyPart.Chest);
        FullHeal(_healthController, EBodyPart.Stomach);
        FullHeal(_healthController, EBodyPart.LeftArm);
        FullHeal(_healthController, EBodyPart.RightArm);
        FullHeal(_healthController, EBodyPart.LeftLeg);
        FullHeal(_healthController, EBodyPart.RightLeg);
        AddEnergy(999f);
        AddHydration(999f);
        ReflectionHelper.TryInvoke(_healthController, "RestoreFullHealth");
    }

    public void ClearNegativeEffects()
    {
        Resolve();
        _healthController?.RemoveNegativeEffects(EBodyPart.Common);
    }

    public void AddEnergy(float amount)
    {
        Resolve();
        if (_healthController == null || amount <= 0f) return;
        _healthController.ChangeEnergy(amount);
    }

    public void AddHydration(float amount)
    {
        Resolve();
        if (_healthController == null || amount <= 0f) return;
        _healthController.ChangeHydration(amount);
    }

    static void FullHeal(ActiveHealthController healthController, EBodyPart part)
    {
        if (healthController == null) return;
        if (healthController.IsBodyPartDestroyed(part)) healthController.RestoreBodyPart(part, 1f);
        healthController.FullRestoreBodyPart(part);
    }

    void ApplyInstantSearch(Player player, bool on)
    {
        try
        {
            var skills = player.Skills;
            if (skills == null) return;

            if (on)
            {
                _originalAttentionEliteExtraLootExp ??= skills.AttentionEliteExtraLootExp.Value;
                _originalAttentionEliteLuckySearch ??= skills.AttentionEliteLuckySearch.Value;
                _originalIntellectEliteContainerScope ??= skills.IntellectEliteContainerScope.Value;

                skills.AttentionEliteExtraLootExp.Value = true;
                skills.AttentionEliteLuckySearch.Value = 100f;
                skills.IntellectEliteContainerScope.Value = true;
            }
            else
            {
                if (_originalAttentionEliteExtraLootExp != null)
                    skills.AttentionEliteExtraLootExp.Value = _originalAttentionEliteExtraLootExp.Value;
                if (_originalAttentionEliteLuckySearch != null)
                    skills.AttentionEliteLuckySearch.Value = _originalAttentionEliteLuckySearch.Value;
                if (_originalIntellectEliteContainerScope != null)
                    skills.IntellectEliteContainerScope.Value = _originalIntellectEliteContainerScope.Value;
            }
        }
        catch
        {
            /* good girl action */
        }
    }

    static void ApplyInfiniteStamina(Player p, bool on)
    {
        try
        {
            var phys = p.Physical;
            if (phys == null) return;

            phys.Stamina.ForceMode = on;
            phys.Oxygen.ForceMode = on;
            phys.HandsStamina.ForceMode = on;

            if (!on) return;

            if (phys.Stamina.Current < 35f)
                phys.Stamina.Current = phys.Stamina.TotalCapacity.Value;
            if (phys.HandsStamina.Current < 35f)
                phys.HandsStamina.Current = phys.HandsStamina.TotalCapacity.Value;
            if (phys.Oxygen.Current < 75f)
                phys.Oxygen.Current = phys.Oxygen.TotalCapacity.Value;
        }
        catch
        {
            /* good girl action */
        }
    }
}