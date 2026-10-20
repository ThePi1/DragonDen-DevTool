using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

namespace DragonDenDevTool.Utilities;

public static class LocalizationUtils
{
  public static string GetBestItemLabel(Item item, bool preferShort = false)
  {
    if (item == null) return "??????";
    var tpl = item.Template;
    if (tpl == null) return item.StringTemplateId ?? "??????";

    if (preferShort)
      return FirstNonEmpty(
        TryLocalized(tpl.ShortNameLocalizationKey),
        TryLocalized(tpl.NameLocalizationKey),
        tpl.ShortName,
        tpl.Name,
        tpl._name,
        item.StringTemplateId
      );

    return FirstNonEmpty(
      TryLocalized(tpl.NameLocalizationKey),
      TryLocalized(tpl.ShortNameLocalizationKey),
      tpl.Name,
      tpl.ShortName,
      tpl._name,
      item.StringTemplateId
    );
  }

  public static string GetBestItemLabel(ItemTemplate tpl, bool preferShort = false)
  {
    if (tpl == null) return "??????";

    if (preferShort)
      return FirstNonEmpty(
        TryLocalized(tpl.ShortNameLocalizationKey),
        TryLocalized(tpl.NameLocalizationKey),
        tpl.ShortName,
        tpl.Name,
        tpl._name,
        tpl.StringId
      );

    return FirstNonEmpty(
      TryLocalized(tpl.NameLocalizationKey),
      TryLocalized(tpl.ShortNameLocalizationKey),
      tpl.Name,
      tpl.ShortName,
      tpl._name,
      tpl.StringId
    );
  }

  public static string GetBestItemLabelByTpl(string tplId, bool preferShort = false)
  {
    if (string.IsNullOrEmpty(tplId)) return "??????";
    var tpl = TryGetTemplate(tplId);
    return tpl != null ? GetBestItemLabel(tpl, preferShort) : tplId;
  }

  static ItemTemplate TryGetTemplate(string tplId)
  {
    try
    {
      var fac = Singleton<EFT.ItemFactory>.Instance;
      if (fac == null || fac.ItemTemplates == null) return null;
      return fac.ItemTemplates.TryGetValue((MongoID)tplId, out var tpl) ? tpl : null;
    }
    catch
    {
      return null;
    }
  }

  static string TryLocalized(string key)
  {
    if (string.IsNullOrEmpty(key)) return null;
    var s = key.Localized();
    return string.IsNullOrEmpty(s) || s == "null" ? null : s;
  }

  static string FirstNonEmpty(params string[] candidates)
  {
    foreach (var s in candidates)
    {
      if (!string.IsNullOrEmpty(s) && s != "null") return s;
    }

    return "??????";
  }
}