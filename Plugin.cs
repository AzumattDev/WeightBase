using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;
using WeightBase.Patches;
using WeightBase.Tools;

namespace WeightBase;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class WeightBasePlugin : BaseUnityPlugin
{
    internal const string ModName = "WeightBase";
    internal const string ModVersion = "1.1.6";
    internal const string Author = "MadBuffoon";
    private const string ModGUID = Author + "." + ModName;
    private const string ConfigFileName = ModGUID + ".cfg";
    private static readonly string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;

    internal static string ConnectionError = "";

    public static readonly ManualLogSource WeightBaseLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

    private static readonly ConfigSync ConfigSync = new(ModGUID)
        { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

    private readonly Harmony _harmony = new(ModGUID);

    public enum Toggle
    {
        On = 1,
        Off = 0
    }

    public void Start()
    {
        // 1 - General
        ServerConfigLocked = config("1 - General", "1 Lock Configuration", Toggle.On,
            "If on, the configuration is locked and can be changed by server admins only.");
        _ = ConfigSync.AddLockingConfigEntry(ServerConfigLocked);

        ItemUnlimitedStackEnabledConfig = config("2 - Items", "1 Remove Stack Limit", true, "Should item stack size limit be removed? Will need to restart game/server!");
        ItemUnlimitedStackEnabledConfig.SettingChanged += (_, _) => _ItemUpdateChange_SettingChange();
        ItemWeightEnabledConfig = config("2 - Items", "2 Weight Reduction", true, "Should item weight Reduction be enabled? Will need to restart game/server!");
        ItemWeightEnabledConfig.SettingChanged += (_, _) => _ItemUpdateChange_SettingChange();
        ItemWeightConfig = config("2 - Items", "3 Item Weight", 1.0f,
            new ConfigDescription(
                "How much an item weighs. 1 is normal weight and 2 being 2x the normal weight then 0.5 is half normal weight. ", new AcceptableValueRange<float>(0f, 2f)));
        ItemWeightConfig.SettingChanged += (_, _) => _ItemUpdateChange_SettingChange();

        ItemIncludeListConfig = config("2 - Items", "4 Include List", "DragonEgg,CryptKey,Wishbone,", "Items to include that don't stack already.\nYou must add a comma at the end.\nExample: DragonEgg,CryptKey,Wishbone,");
        ItemIncludeListConfig.SettingChanged += (_, _) => _ItemUpdateChange_SettingChange();
        ItemExcludeListConfig = config("2 - Items", "5 Exclude List", string.Empty, "Items to Exclude items from Stack/Weight Change.\nYou must add a comma at the end.\nExample: DragonEgg,CryptKey,Wishbone,");
        ItemExcludeListConfig.SettingChanged += (_, _) => _ItemUpdateChange_SettingChange();
        ItemNoWeightListConfig = config("2 - Items", "6 No Weight List", "Coins,", "Items to have the stack change but have no weight.\nYou must add a comma at the end.\nExample: DragonEgg,CryptKey,Wishbone,");
        ItemNoWeightListConfig.SettingChanged += (_, _) => _ItemUpdateChange_SettingChange();

        ShipMassToWeightEnabledConfig = config("3 - Ship Weight", "1 Weight Matters", true, "Should weight in the cargo matter?");
        ShipMassScaleConfig = config("3 - Ship Weight", "2 Weight Capacity Scale", 2f, new ConfigDescription("This scales the total weight the ship can carry.", new AcceptableValueRange<float>(1f, 20f)));
        ShipMassWeightLookEnableConfig = config("3 - Ship Weight", "3 Got Weight?", false, "Should the ship show that it's over weight?");
        ShipMassSinkEnableConfig = config("3 - Ship Weight", "4 Sinking", false, "Should weight in the cargo sink your ship?");
        // End of Config Settings


        var assembly = Assembly.GetExecutingAssembly();
        _harmony.PatchAll(assembly);
        SetupWatcher();
    }

    private void OnDestroy()
    {
        Config.Save();
    }


    private void _ItemUpdateChange_SettingChange()
    {
        if (!ObjectDB.instance) return;
        WeightBaseLogger.LogDebug("_ItemUpdateChange_SettingChange");
        Helper.UpdateItemDatabase(ObjectDB.instance);
        var itemDrops = Resources.FindObjectsOfTypeAll<ItemDrop>();
        foreach (var item in itemDrops)
        {
            var nameOfItem = Utils.GetPrefabName(item.gameObject) + ",";
            Helper.UpdateItem(item.m_itemData, nameOfItem);
        }

        if (!Player.m_localPlayer) return;
        foreach (var i in Player.m_localPlayer.m_inventory.m_inventory)
        {
            var nameOfItem = Utils.GetPrefabName(i.m_dropPrefab) + ",";
            Helper.UpdateItem(i, nameOfItem);
        }

        Player.m_localPlayer.m_inventory.UpdateTotalWeight();

        if (InventoryGui.instance == null) return;
        InventoryGui.instance.UpdateInventoryWeight(Player.m_localPlayer);
        if (!InventoryGui.instance.m_currentContainer) return;
        foreach (var i in InventoryGui.instance.m_currentContainer.m_inventory.m_inventory)
        {
            var nameOfItem = Utils.GetPrefabName(i.m_dropPrefab) + ",";
            Helper.UpdateItem(i, nameOfItem);
        }

        InventoryGui.instance.m_currentContainer.m_inventory.UpdateTotalWeight();
        InventoryGui.instance.UpdateContainerWeight();
    }

    private void SetupWatcher()
    {
        FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
        watcher.Changed += ReadConfigValues;
        watcher.Created += ReadConfigValues;
        watcher.Renamed += ReadConfigValues;
        watcher.IncludeSubdirectories = true;
        watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        watcher.EnableRaisingEvents = true;
    }

    private void ReadConfigValues(object sender, FileSystemEventArgs e)
    {
        if (!File.Exists(ConfigFileFullPath)) return;
        try
        {
            WeightBaseLogger.LogDebug("ReadConfigValues called");
            Config.Reload();
        }
        catch
        {
            WeightBaseLogger.LogError($"There was an issue loading your {ConfigFileName}");
            WeightBaseLogger.LogError("Please check your config entries for spelling and format!");
        }
    }


    #region ConfigOptions

    internal static ConfigEntry<Toggle> ServerConfigLocked = null!;

    internal static ConfigEntry<bool> ItemUnlimitedStackEnabledConfig = null!;
    internal static ConfigEntry<bool> ItemWeightEnabledConfig = null!;
    internal static ConfigEntry<float> ItemWeightConfig = null!;
    internal static ConfigEntry<string> ItemIncludeListConfig = null!;
    internal static ConfigEntry<string> ItemExcludeListConfig = null!;
    internal static ConfigEntry<string> ItemNoWeightListConfig = null!;


    internal static ConfigEntry<bool> ShipMassToWeightEnabledConfig = null!;
    internal static ConfigEntry<float> ShipMassScaleConfig = null!;
    internal static ConfigEntry<bool> ShipMassWeightLookEnableConfig = null!;
    internal static ConfigEntry<bool> ShipMassSinkEnableConfig = null!;


    private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
        bool synchronizedSetting = true)
    {
        ConfigDescription extendedDescription =
            new(
                description.Description +
                (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
                description.AcceptableValues, description.Tags);
        var configEntry = Config.Bind(group, name, value, extendedDescription);
        //var configEntry = Config.Bind(group, name, value, description);

        var syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private ConfigEntry<T> config<T>(string group, string name, T value, string description,
        bool synchronizedSetting = true)
    {
        return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
    }

    private class ConfigurationManagerAttributes
    {
        [UsedImplicitly] public int? Order = null!;
        [UsedImplicitly] public bool? Browsable = null!;
        [UsedImplicitly] public string? Category = null!;
        [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer = null!;
    }

    private class AcceptableShortcuts : AcceptableValueBase
    {
        public AcceptableShortcuts() : base(typeof(KeyboardShortcut))
        {
        }

        public override object Clamp(object value)
        {
            return value;
        }

        public override bool IsValid(object value)
        {
            return true;
        }

        public override string ToDescriptionString()
        {
            return "# Acceptable values: " + string.Join(", ", UnityInput.Current.SupportedKeyCodes);
        }
    }

    #endregion
}