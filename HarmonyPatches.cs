using RimWorld;
using RimWorld.Planet;
using Verse;
using HarmonyLib;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace KjellnersPersistentMaps
{
    [StaticConstructorOnStartup]
    public static class PersistentMapsInit
    {
        static PersistentMapsInit()
        {
            var harmony = new Harmony("kjellner.persistentmaps");
            harmony.PatchAll();

            OrphanDataCleaner.CleanOrphanedFolders();

            KLog.Message("[PersistentMaps] All patches applied.");
        }
    }

    // Deletes PersistentMaps data folders that no longer have a matching save file.
    // Runs once at startup so deleted saves don't leave tile XML behind indefinitely.
    public static class OrphanDataCleaner
    {
        public static void CleanOrphanedFolders()
        {
            try
            {
                string mapsRoot = Path.Combine(GenFilePaths.SaveDataFolderPath, "PersistentMaps");
                if (!Directory.Exists(mapsRoot))
                    return;

                string savesDir = Path.Combine(GenFilePaths.SaveDataFolderPath, "Saves");
                if (!Directory.Exists(savesDir))
                    return;

                string[] saveFiles = Directory.GetFiles(savesDir, "*.rws");
                string[] candidates = Directory.GetDirectories(mapsRoot);

                foreach (string dir in candidates)
                {
                    string id = Path.GetFileName(dir);
                    bool referenced = saveFiles.Any(f => SaveContainsId(f, id));
                    if (!referenced)
                    {
                        KLog.Message($"[PersistentMaps] Deleting orphaned data for save id {id}");
                        Directory.Delete(dir, recursive: true);
                    }
                }
            }
            catch (Exception e)
            {
                KLog.Error($"[PersistentMaps] OrphanDataCleaner failed: {e}");
            }
        }

        private static bool SaveContainsId(string filePath, string id)
        {
            try
            {
                return File.ReadAllText(filePath).Contains(id);
            }
            catch
            {
                return true; // unreadable file: assume referenced, don't delete
            }
        }
    }

    // Allow settling on AbandonedSettlement tiles
    [HarmonyPatch(typeof(TileFinder), nameof(TileFinder.IsValidTileForNewSettlement))]
    public static class Patch_IsValidTileForNewSettlement
    {
        public static void Postfix(
            PlanetTile tile,
            StringBuilder reason,
            bool forGravship,
            ref bool __result)
        {
            if (__result)
                return;

            var abandoned = Find.WorldObjects.WorldObjectAt<AbandonedSettlement>(tile);
            if (abandoned != null)
            {
                __result = true;
                if (reason != null)
                    reason.Length = 0;
            }
        }
    }

    // Remove AbandonedSettlement before settling
    [HarmonyPatch(typeof(SettleInEmptyTileUtility), nameof(SettleInEmptyTileUtility.Settle))]
    public static class Patch_SettleInEmptyTileUtility_Settle
    {
        public static void Prefix(Caravan caravan)
        {
            var abandoned = Find.WorldObjects.WorldObjectAt<AbandonedSettlement>(caravan.Tile);
            if (abandoned != null)
            {
                KLog.Message("[PersistentMaps] Removing abandoned settlement before settling.");
                Find.WorldObjects.Remove(abandoned);
            }
        }
    }

    // Serialize map before abandonment
    [HarmonyPatch(typeof(MapParent), nameof(MapParent.Abandon))]
    public static class Patch_MapParent_Abandon
    {
        public static void Prefix(MapParent __instance, bool wasGravshipLaunch)
        {
            Map map = __instance.Map;
            if (map == null || !map.IsPlayerHome)
                return;

            KLog.Message($"[PersistentMaps] Serializing map before abandonment. Tile: {__instance.Tile.tileId}");
            PersistentMapSerializer.SaveMap(map, __instance.Tile);
        }
    }

    // Inject map loading logic
    [HarmonyPatch(
        typeof(GetOrGenerateMapUtility),
        nameof(GetOrGenerateMapUtility.GetOrGenerateMap),
        new Type[]
        {
            typeof(PlanetTile),
            typeof(IntVec3),
            typeof(WorldObjectDef),
            typeof(IEnumerable<GenStepWithParams>),
            typeof(bool)
        }
    )]
    public static class Patch_GetOrGenerateMap
    {
        public static void Postfix(PlanetTile tile, ref Map __result)
        {
            if (__result == null || !PersistentMapSerializer.PersistentFileExists(tile))
                return;

            KLog.Message($"[PersistentMaps] Applying persistent data to tile {tile}");
            Map map = __result;
            LongEventHandler.ExecuteWhenFinished(() => PersistentMapSerializer.LoadSavedMap(map, tile));
        }
    }

    // Skip mapgen animal population for tiles that have persistent data.
    // Our wildlife is deep-saved directly to tile XML and respawned on restore.
    // WildAnimalSpawner handles natural regrowth going forward.
    [HarmonyPatch(typeof(GenStep_Animals), "Generate")]
    public static class Patch_GenStep_Animals
    {
        public static bool Prefix(Map map) =>
            !PersistentMapSerializer.PersistentFileExists(map.Tile);
    }

    // Suppress roof collapse checks while restoring a map
    [HarmonyPatch(typeof(RoofCollapseCellsFinder), "CheckAndRemoveCollpsingRoofs")]
    public static class Patch_DisableRoofCollapseDuringLoad
    {
        public static bool Prefix() => !PersistentMapSerializer.IsRestoring;
    }

    // Suppress area-revealed letters while rebuilding fog
    [HarmonyPatch(typeof(FogGrid), "NotifyAreaRevealed")]
    public static class Patch_DisableAreaRevealLettersDuringLoad
    {
        public static bool Prefix() => !PersistentMapSerializer.IsRestoring;
    }
}
