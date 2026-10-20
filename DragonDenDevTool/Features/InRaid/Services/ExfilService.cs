using System;
using System.Linq;
using Comfort.Common;
using DragonDenDevTool.Features.InRaid.Interfaces;
using DragonDenDevTool.Utilities;
using EFT;
using EFT.Interactive;
using EFT.Interactive.SecretExfiltrations;
using UnityEngine;

namespace DragonDenDevTool.Features.InRaid.Services;


using Utils = DragonDenDevTool.Utilities.Utils;
public class ExfilService : IExfilService
{
    public event Action<ExfiltrationPoint> OnStatusChanged;

    public ExfilService()
    {
        var exfilController = CommonAssets.Scripts.Game.ExfiltrationController.Instance;
        if (exfilController != null)
            exfilController.StatusChanged += Exfil_StatusChanged;
    }

    void Exfil_StatusChanged(ExfiltrationPoint obj)
    {
        try
        {
            OnStatusChanged?.Invoke(obj);
        }
        catch
        {
            /* good girl action */
        }
    }

    public (ExfiltrationPoint[] pmc, ScavExfiltrationPoint[] scav, SecretExfiltrationPoint[] secret) GetCurrentExfil()
    {
        var exfilController = CommonAssets.Scripts.Game.ExfiltrationController.Instance;
        if (exfilController == null)
            return (Array.Empty<ExfiltrationPoint>(), Array.Empty<ScavExfiltrationPoint>(), Array.Empty<SecretExfiltrationPoint>());

        return (exfilController.ExfiltrationPoints ?? Array.Empty<ExfiltrationPoint>(),
            exfilController.ScavExfiltrationPoints ?? Array.Empty<ScavExfiltrationPoint>(),
            exfilController.SecretExfiltrationPoints ?? Array.Empty<SecretExfiltrationPoint>());
    }

    public void TeleportToExfil(Vector3 target)
    {
        try
        {
            var me = GamePlayerOwner.MyPlayer ?? Singleton<GameWorld>.Instance?.MainPlayer;
            if (!me) return;
            TeleportUtils.SafeTeleport(me, target);
        }
        catch
        {
            /* good girl action */
        }
    }

    public string ExfilStatusToText(EExfiltrationStatus s)
    {
        return s switch
        {
            EExfiltrationStatus.NotPresent => "NotPresent",
            EExfiltrationStatus.UncompleteRequirements => "Unmet Requirements",
            EExfiltrationStatus.Countdown => "Countdown",
            EExfiltrationStatus.RegularMode => "Ready",
            EExfiltrationStatus.Pending => "Pending",
            EExfiltrationStatus.AwaitsManualActivation => "Awaiting Manual",
            EExfiltrationStatus.Hidden => "Hidden",
            _ => s.ToString()
        };
    }

    public bool PassesFilter(ExfiltrationPoint p, string filter)
    {
        if (!p) return false;
        if (string.IsNullOrWhiteSpace(filter)) return true;

        var f = filter.Trim();
        if (!string.IsNullOrEmpty(p.Settings?.Name) && p.Settings.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (!string.IsNullOrEmpty(p.Description) && p.Description.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return p.Id.ToString().IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public string[] GetTips(ExfiltrationPoint p)
    {
        try
        {
            if (!p) return Array.Empty<string>();
            var pid = GamePlayerOwner.MyPlayer ? GamePlayerOwner.MyPlayer.ProfileId : "";
            var raw = p.GetTips(pid) ?? Array.Empty<string>();
            return raw.Select(CleanTip)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    static string CleanTip(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        s = Utils.PriceWithOld.Replace(s, "($1, (original $2))");

        s = Utils.UnityTags.Replace(s, "");

        s = s.Replace("&nbsp;", " ")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&amp;", "&");

        s = Utils.MultiSpace.Replace(s, " ").Trim();
        return s;
    }
}