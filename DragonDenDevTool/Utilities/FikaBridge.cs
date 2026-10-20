using System;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;

namespace DragonDenDevTool.Utilities;

/// <summary>
/// Optional Fika (co-op) support, found by reflection so the tool needs no Fika reference and behaves exactly as
/// before when Fika isn't installed. Written against Fika 2.4.6.
///
/// In a Fika raid the host owns the world: bots, their health, and every item's existence. A client only puppets
/// bots (ObservedPlayer), so damaging one locally does nothing, and an item created locally is unknown to the host,
/// which then rejects picking it up. This class routes those two actions through Fika's own packets:
///  - damage: ObservedPlayer.HandleExplosive sends a DamagePacket to the bot's owner (as Fika's minefields do);
///  - items: SpawnItemInInventoryPacket, broadcast so the host and every other client create the same item
///    (same id) in their copy of our inventory.
/// </summary>
public static class FikaBridge
{
    private static bool _resolved;
    private static Type _observedPlayer;
    private static MethodInfo _handleExplosive;
    private static FieldInfo _netId;
    private static FieldInfo _isObservedAI;
    private static PropertyInfo _isServer;
    private static PropertyInfo _isClient;
    private static PropertyInfo _managerInstance;
    private static PropertyInfo _managerInstantiated;
    private static PropertyInfo _temporaryStash;
    private static MethodInfo _sendData;
    private static object _reliableOrdered;
    private static Type _spawnPacket;

    /// <summary>Fika is installed and its types were found.</summary>
    public static bool Present
    {
        get
        {
            Resolve();
            return _observedPlayer != null;
        }
    }

    /// <summary>This game is a Fika client: someone else (another player or a headless) hosts the raid.</summary>
    public static bool IsClient => Present && _isClient != null && (bool)_isClient.GetValue(null);

    /// <summary>This game hosts the Fika raid (or Fika isn't installed, so it's single-player).</summary>
    public static bool IsHostOrSinglePlayer => !Present || _isServer == null || (bool)_isServer.GetValue(null);

    private static void Resolve()
    {
        if (_resolved) return;
        _resolved = true;
        try
        {
            if (!Chainloader.PluginInfos.ContainsKey("com.fika.core")) return;

            var fikaPlayer = AccessTools.TypeByName("Fika.Core.Main.Players.FikaPlayer");
            var observed = AccessTools.TypeByName("Fika.Core.Main.Players.ObservedPlayer");
            var backend = AccessTools.TypeByName("Fika.Core.Main.Utils.FikaBackendUtils");
            var manager = AccessTools.TypeByName("Fika.Core.Networking.IFikaNetworkManager");
            _spawnPacket = AccessTools.TypeByName("Fika.Core.Networking.Packets.Player.SpawnItemInInventoryPacket");
            if (fikaPlayer == null || observed == null || backend == null || manager == null || _spawnPacket == null)
            {
                Plugin.Logger.LogWarning("[DevTool] Fika is installed but its types weren't found (unsupported Fika version?). Bot kills and item spawns will only work when you host.");
                return;
            }

            _handleExplosive = AccessTools.Method(observed, "HandleExplosive", new[] { typeof(DamageInfo), typeof(EBodyPart), typeof(EBodyPartColliderType) });
            _netId = AccessTools.Field(fikaPlayer, "NetId");
            _isObservedAI = AccessTools.Field(fikaPlayer, "IsObservedAI");
            _isServer = AccessTools.Property(backend, "IsServer");
            _isClient = AccessTools.Property(backend, "IsClient");

            var singleton = typeof(Comfort.Common.Singleton<>).MakeGenericType(manager);
            _managerInstance = singleton.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            _managerInstantiated = singleton.GetProperty("Instantiated", BindingFlags.Public | BindingFlags.Static);
            _temporaryStash = manager.GetProperty("TemporaryStash");

            // void SendData<T>(ref T packet, DeliveryMethod deliveryMethod, bool broadcast = false)
            var send = manager.GetMethods().FirstOrDefault(m => m.Name == "SendData" && m.IsGenericMethodDefinition && m.GetParameters().Length == 3);
            if (send != null)
            {
                _sendData = send.MakeGenericMethod(_spawnPacket);
                _reliableOrdered = Enum.Parse(send.GetParameters()[1].ParameterType, "ReliableOrdered");
            }

            _observedPlayer = observed;
            Plugin.Logger.LogInfo($"[DevTool] Fika detected: kill via DamagePacket {(_handleExplosive != null ? "ok" : "MISSING")}, " +
                                  $"item spawn via SpawnItemInInventoryPacket {(_sendData != null && _temporaryStash != null ? "ok" : "MISSING")}");
        }
        catch (Exception e)
        {
            _observedPlayer = null;
            Plugin.Logger.LogError($"[DevTool] Fika support failed to initialise: {e}");
        }
    }

    /// <summary>
    /// A bot rather than a human. Plain EFT and the Fika host mark bots through AIData; on a Fika client every
    /// bot is an ObservedPlayer flagged IsObservedAI (Player.IsAI is false there).
    /// </summary>
    public static bool IsBot(Player player)
    {
        if (!player || player.IsYourPlayer) return false;
        if (player.IsAI) return true;
        Resolve();
        return _isObservedAI != null && _isObservedAI.DeclaringType.IsInstanceOfType(player) && (bool)_isObservedAI.GetValue(player);
    }

    /// <summary>Kills <paramref name="target"/>, routing the damage to its owner when this machine only observes it.</summary>
    public static void Kill(Player target)
    {
        var damage = new DamageInfo { Damage = 9999f, DamageType = EDamageType.Bullet, StaminaBurnRate = 0f, BleedBlock = true };
        Resolve();
        if (_observedPlayer != null && _observedPlayer.IsInstanceOfType(target))
        {
            if (_handleExplosive == null) throw new InvalidOperationException("Fika's ObservedPlayer.HandleExplosive wasn't found (unsupported Fika version).");
            // Sends a DamagePacket to the player's owner (the host for bots); the owner applies it and syncs the death.
            _handleExplosive.Invoke(target, new object[] { damage, EBodyPart.Head, EBodyPartColliderType.HeadCommon });
            return;
        }
        target.ApplyDamageInfo(damage, EBodyPart.Head, EBodyPartColliderType.HeadCommon, 0f);
    }

    /// <summary>
    /// Fika only: puts a new item (with <paramref name="count"/> in its stack) into <paramref name="player"/>'s own
    /// inventory and tells the host and every other client to do the same. Returns false (and why) when it doesn't fit
    /// or Fika's API is missing.
    /// </summary>
    public static bool SpawnInInventory(Player player, string templateId, int count, out string error)
    {
        error = null;
        Resolve();
        if (_sendData == null || _temporaryStash == null || _netId == null)
        {
            error = "Fika's item spawn API wasn't found (unsupported Fika version)";
            return false;
        }
        if (_managerInstantiated == null || !(bool)_managerInstantiated.GetValue(null))
        {
            error = "Fika's network manager isn't running";
            return false;
        }

        var manager = _managerInstance.GetValue(null);
        var controller = player.InventoryController;
        var item = Comfort.Common.Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), templateId, null);
        if (item == null)
        {
            error = "CreateItem returned null";
            return false;
        }
        item.StackObjectsCount = item.StackMaxSize > 1 ? UnityEngine.Mathf.Clamp(count, 1, item.StackMaxSize) : 1;

        // Same steps Fika runs when it receives this packet, plus the UI events so an open inventory shows the item.
        // The item needs a location (Fika's temporary stash) before EFT can search for space: the pickup search
        // validates the move from item.Parent, which throws for an item that isn't anywhere yet.
        var stash = (Stash)_temporaryStash.GetValue(manager);
        var add = stash.Grid.AddAnywhere(item, EErrorHandlingType.Ignore);
        if (add.Failed)
        {
            error = "temporary stash: " + add.Error;
            return false;
        }
        _ = new ItemController(stash, controller.ID, "temp item stash", false, EOwnerType.Profile);

        ItemAddress address = controller.FindGridToPickUp(item) ?? controller.FindSlotToPickUp(item);
        if (address == null)
        {
            stash.Grid.Remove(item, false);
            error = "no free space in your inventory";
            return false;
        }

        var move = ItemManipulator.Move(item, address, controller, false);
        if (move.Failed)
        {
            stash.Grid.Remove(item, false);
            error = "move into inventory: " + move.Error;
            return false;
        }
        move.Value.RaiseEvents(controller, CommandStatus.Begin);
        move.Value.RaiseEvents(controller, CommandStatus.Succeed);

        object packet = Activator.CreateInstance(_spawnPacket);
        _spawnPacket.GetField("NetId").SetValue(packet, (int)_netId.GetValue(player));
        _spawnPacket.GetField("ItemId").SetValue(packet, (MongoID)item.Id);
        _spawnPacket.GetField("TemplateId").SetValue(packet, (MongoID)templateId);
        _spawnPacket.GetField("Amount").SetValue(packet, item.StackObjectsCount);
        _spawnPacket.GetField("ItemAddress").SetValue(packet, address);
        // broadcast: a client's packet is processed by the host and relayed to every other client.
        _sendData.Invoke(manager, new[] { packet, _reliableOrdered, true });
        return true;
    }
}
