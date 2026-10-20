using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using DragonDenDevTool.Features.InRaid.Interfaces;
using DragonDenDevTool.Utilities;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DragonDenDevTool.Features.InRaid.Services;

public class ItemService : IItemService
{
    public ItemTemplate[] SearchItems(string query, int max)
    {
        try
        {
            var itemFactory = Singleton<EFT.ItemFactory>.Instance;
            if (itemFactory?.ItemTemplates == null)
                return Array.Empty<ItemTemplate>();

            bool IsRealItem(ItemTemplate t) => t != null && t._type == NodeType.Item;

            var all = itemFactory.ItemTemplates
                .Select(kv => kv.Value)
                .Where(IsRealItem)
                .ToArray();

            var q = (query ?? "").Trim();
            if (q.Length == 0)
                return all.Take(Mathf.Max(1, max)).ToArray();

            var low = q.ToLowerInvariant();

            return all.Where(Match).Take(Mathf.Max(1, max)).ToArray();

            bool Match(ItemTemplate t)
            {
                var id = t._id.ToString();
                var n1 = t._name ?? string.Empty;
                var n2 = t.ShortName ?? string.Empty;
                var n3 = t.Name ?? string.Empty;
                var n4 = t.NameLocalizationKey ?? string.Empty;

                return id.IndexOf(low, StringComparison.OrdinalIgnoreCase) >= 0
                       || n1.IndexOf(low, StringComparison.OrdinalIgnoreCase) >= 0
                       || n2.IndexOf(low, StringComparison.OrdinalIgnoreCase) >= 0
                       || n3.IndexOf(low, StringComparison.OrdinalIgnoreCase) >= 0
                       || n4.IndexOf(low, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
        catch
        {
            return Array.Empty<ItemTemplate>();
        }
    }

    public int GetStackMax(string templateId)
    {
        try
        {
            var itemFactory = Singleton<EFT.ItemFactory>.Instance;

            var itemTpl = itemFactory?.ItemTemplates?
                .Select(kv => kv.Value)
                .FirstOrDefault(v => v != null && v._id.ToString() == templateId);
            if (itemTpl == null) return 1;

            var itemType = itemTpl.GetType();
            var itemProperty = itemType.GetProperty("StackMaxSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (itemProperty != null && itemProperty.PropertyType == typeof(int))
                return Math.Max(1, (int)itemProperty.GetValue(itemTpl));

            var itemField = itemType.GetField("StackMaxSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (itemField != null && itemField.FieldType == typeof(int))
                return Math.Max(1, (int)itemField.GetValue(itemTpl));

            var item = itemFactory.CreateItem(Guid.NewGuid().ToString("N"), templateId, null);
            if (item != null)
            {
                var itemType2 = item.GetType();
                var itemProperty2 = itemType2.GetProperty("StackMaxSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (itemProperty2 != null && itemProperty2.PropertyType == typeof(int))
                    return Math.Max(1, (int)itemProperty2.GetValue(item));
                var itemField2 = itemType2.GetField("StackMaxSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (itemField2 != null && itemField2.FieldType == typeof(int))
                    return Math.Max(1, (int)itemField2.GetValue(item));
            }
        }
        catch
        {
            /* good girl action */
        }

        return 1;
    }

    public (bool ok, string templateId, string displayName) TryResolveTemplate(string query)
    {
        var searchItems = SearchItems(query, 64);
        if (searchItems == null || searchItems.Length == 0) return (false, null, null);
        var first = searchItems[0];
        var id = first?._id.ToString();
        var name = LocalizationUtils.GetBestItemLabel(first);
        return string.IsNullOrWhiteSpace(id) ? (false, null, null) : (true, id, string.IsNullOrWhiteSpace(name) ? id : name);
    }

    static void SetStackCount(Item item, int count)
    {
        count = Mathf.Max(1, count);
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var itemType = item.GetType();

        var itemProperty = itemType.GetProperty("StackObjectsCount", flags);
        if (itemProperty != null && itemProperty.CanWrite && itemProperty.PropertyType == typeof(int))
        {
            itemProperty.SetValue(item, count);
            return;
        }

        var itemField = itemType.GetField("StackObjectsCount", flags);
        if (itemField != null && itemField.FieldType == typeof(int))
        {
            itemField.SetValue(item, count);
            return;
        }

        var itemMethod = itemType.GetMethod("set_StackObjectsCount", flags, null, new[] { typeof(int) }, null)
                         ?? itemType.GetMethod("SetStackObjectsCount", flags, null, new[] { typeof(int) }, null)
                         ?? itemType.GetMethod("SetStackSize", flags, null, new[] { typeof(int) }, null);
        if (itemMethod != null)
        {
            itemMethod.Invoke(item, new object[] { count });
        }
    }

    public async Task DropItemAsync(string templateId, Player player, int stack = 1)
    {
        try
        {
            var itemFactory = Singleton<EFT.ItemFactory>.Instance;
            var gameWorld = Singleton<GameWorld>.Instance;
            var poolManager = Singleton<EFT.ObjectsFactory>.Instance;

            if (itemFactory == null || !gameWorld || !player)
            {
                Plugin.Logger.LogError("[DevTool] Drop Failed: Missing ItemFactoryClass/GameWorld or Player.");
                EFT.Communications.NotificationManager.DisplayMessageNotification("[DevTool] Drop failed: Missing ItemFactoryClass/GameWorld or Player.");
                return;
            }

            var remaining = Mathf.Max(1, stack);
            var maxPerStack = Mathf.Max(1, GetStackMax(templateId));

            var probeId = MongoID.Generate();
            var probe = itemFactory.CreateItem(probeId, templateId, null);
            if (probe == null)
            {
                Plugin.Logger.LogError("[DevTool] Drop Failed: CreateItem returned null.");
                EFT.Communications.NotificationManager.DisplayMessageNotification("[DevTool] Drop failed: CreateItem returned null.");
                return;
            }

            var keys = new List<ResourceKey>();
            if (probe.Template?.Prefab != null) keys.Add(probe.Template.Prefab);
            if (probe.Template?.UsePrefab != null) keys.Add(probe.Template.UsePrefab);

            if (keys.Count > 0 && poolManager != null)
            {
                await poolManager.LoadBundlesAndCreatePools(
                    0,
                    EFT.ObjectsFactory.AssemblyType.Local,
                    keys.ToArray(),
                    Diz.Jobs.JobYieldPriority.Immediate,
                    null,
                    CancellationToken.None
                );
            }

            var headPos = player.PlayerBones.Head.Original.position;
            var forward = player.Transform.forward;
            var rotation = player.Transform.rotation;

            Vector3 RandXZ(float radius)
            {
                var v = Random.insideUnitCircle * radius;
                return new Vector3(v.x, 0f, v.y);
            }

            void Drop(Item it)
            {
                const float posSpread = 0.55f;
                const float fwdOffset = 0.50f;

                var pos = headPos
                          + forward * fwdOffset
                          + RandXZ(posSpread)
                          + new Vector3(0f, Random.Range(-0.05f, 0.15f), 0f);

                var rot = Quaternion.Euler(Random.Range(0f, 35f), Random.Range(0f, 360f), Random.Range(0f, 35f)) * rotation;
                var push = (forward + RandXZ(0.15f)).normalized * Random.Range(0.6f, 1.2f);
                var spin = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f));

                gameWorld.ThrowItem(it, null, pos, rot, push, spin, true);
            }

            var thisStack = Mathf.Min(remaining, maxPerStack);
            SetStackCount(probe, thisStack);
            Drop(probe);
            remaining -= thisStack;

            while (remaining > 0)
            {
                thisStack = Mathf.Min(remaining, maxPerStack);
                var id = MongoID.Generate();
                var item = itemFactory.CreateItem(id, templateId, null);
                if (item != null)
                {
                    SetStackCount(item, thisStack);
                    Drop(item);
                }

                remaining -= thisStack;
            }

            EFT.Communications.NotificationManager.DisplayMessageNotification($"[DevTool] Dropped {(stack <= 1 ? "item" : $"{stack} items (stacked)")}: {templateId}");
        }
        catch (Exception e)
        {
            Plugin.Logger?.LogError($"[Items] Drop exception: {e}");
            EFT.Communications.NotificationManager.DisplayMessageNotification("[DevTool] Drop failed: " + e.Message);
        }
    }
}