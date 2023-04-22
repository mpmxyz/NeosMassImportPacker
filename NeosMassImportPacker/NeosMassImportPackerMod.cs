using BaseX;
using CodeX;
using FrooxEngine;
using HarmonyLib;
using NeosModLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace NeosMassImportPacker
{
    public class NeosMassImportPackerMod : NeosMod
    {
        public override string Name => "NeosMassImportPacker";
        public override string Author => "mpmxyz";
        public override string Version => "1.0.0";
        public override string Link => "https://github.com/mpmxyz/NeosMassImportPacker/";
        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony("com.github.mpmxyz.massimportpacker");
            harmony.PatchAll();
        }

        private static ConditionalWeakTable<World, ImportWatcher> watchers = new ConditionalWeakTable<World, ImportWatcher>();


        [HarmonyPatch(typeof(UniversalImporter), "UndoableImport")]
        class NeosPDFImportPatch
        {
            public static bool Prefix(Slot root, Func<Task> import)
            {
                var watcher = watchers.GetOrCreateValue(root.World);
                watcher.OnImport(root);
                return true;
            }
        }
    }
}