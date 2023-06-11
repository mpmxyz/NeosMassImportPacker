using FrooxEngine;
using NeosModLoader;
using NeosAssetImportHook;
using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using HarmonyLib;
using BaseX;
using CodeX;

namespace NeosMassImportPacker
{
    public class NeosMassImportPackerMod : NeosMod
    {
        public override string Name => "NeosMassImportPacker";
        public override string Author => "mpmxyz";
        public override string Version => "2.1.0";
        public override string Link => "https://github.com/mpmxyz/NeosMassImportPacker/";

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> KEY_ENABLED = new ModConfigurationKey<bool>("enabled", "Enable the spawning a reparenting wizard when importing multiple assets", () => true);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<float> KEY_MAX_GAP = new ModConfigurationKey<float>("maxGap", "Maximum time in seconds before two imports are considered separate", () => 10, valueValidator: (t) => t >= 0 && !float.IsNaN(t));

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> KEY_NON_PERSISTENT_UI = new ModConfigurationKey<bool>("nonPersistentUI", "Prevent the wizard from being a permanent part of the world", () => false);

        private static ConditionalWeakTable<World, ImportWatcher> watchers = new ConditionalWeakTable<World, ImportWatcher>();

        private static NeosMassImportPackerMod Instance;

        internal static float MaxGap => Instance?.GetConfiguration()?.GetValue(KEY_MAX_GAP) ?? 10;
        internal static bool PersistUI => !(Instance?.GetConfiguration()?.GetValue(KEY_NON_PERSISTENT_UI) ?? false);

        public override void OnEngineInit()
        {
            Instance = this;

            AssetImportHooks.PostImport += (Slot slot, Type assetType, IList<IAssetProvider> assets) =>
            {
                if (GetConfiguration().GetValue(KEY_ENABLED))
                {
                    if (watchers.TryGetValue(slot.World, out var watcher))
                    {
                        watcher.OnImport(slot);
                    }
                }
            };
            Harmony harmony = new Harmony("com.github.mpmxyz.massimportpacker");
            harmony.PatchAll();
        }

        [HarmonyPatch(typeof(UniversalImporter), "ImportTask")]
        class NeosMassImportPackerPatch
        {
            public static bool Prefix(AssetClass assetClass, IEnumerable<string> files, World world, float3 position, floatQ rotation, float3 scale, bool silent = false)
            {
                watchers.GetOrCreateValue(world).ResetImportGroup();
                return true;
            }
        }
    }
}