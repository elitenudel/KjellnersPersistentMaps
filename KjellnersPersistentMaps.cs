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
    // Stick a UUID into the save file to identify this save and tie it to external serialized map data
    public class GameComponent_PersistentMaps : GameComponent
    {
        public string persistentId;

        // When loading a save the UUID will be generated and then overwritten by Scribe in LoadingVars mode
        public GameComponent_PersistentMaps(Game game)
        {
            persistentId = Guid.NewGuid().ToString();
            Log.Message($"[PersistentMaps] Generated persistent ID: {persistentId}");
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref persistentId, "persistentId");
        }
    }

    // Harmony Initialization
    [StaticConstructorOnStartup]
    public static class PersistentMapsInit
    {
        static PersistentMapsInit()
        {
            var harmony = new Harmony("kjellner.persistentmaps");
            harmony.PatchAll();

            Log.Message("[PersistentMaps] All patches applied.");
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
                Log.Message($"[PersistentMaps] Allowing settlement on abandoned tile {tile.tileId}");

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
            PlanetTile tile = caravan.Tile;

            var abandoned = Find.WorldObjects.WorldObjectAt<AbandonedSettlement>(tile);

            if (abandoned != null)
            {
                Log.Message("[PersistentMaps] Removing abandoned settlement before settling.");
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

            if (map == null)
                return;

            if (!map.IsPlayerHome)
                return;

            Log.Message($"[PersistentMaps] Serializing map before abandonment. Tile: {__instance.Tile.tileId}");

            PersistentMapSerializer.SaveMap(map, __instance.Tile);
        }
    }

    // Block ancient danger generation for reloaded maps.
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
    public static class Patch_GetOrGenerateMap_Prefix
    {
        public static void Prefix(
            PlanetTile tile,
            ref IEnumerable<GenStepWithParams> extraGenStepDefs)
        {
            if (!PersistentMapSerializer.PersistentFileExists(tile))
                return;

            if (extraGenStepDefs == null)
                return;

            extraGenStepDefs = extraGenStepDefs
                .Where(gs =>
                    !(gs.genStep is GenStep_ScatterRuinsSimple) &&
                    !(gs.genStep is GenStep_AncientShrines))
                .ToList();

            Log.Message($"[PersistentMaps] Filtered ruin gensteps for tile {tile}");
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
        public static void Postfix(
            PlanetTile tile,
            ref Map __result)
        {
            if (__result == null)
                return;

            if (!PersistentMapSerializer.PersistentFileExists(tile))
                return;

            Log.Message($"[PersistentMaps] Applying persistent data to tile {tile}");

            Map map = __result;

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                PersistentMapSerializer.LoadSavedMap(map, tile);
            });
        }
    }


    // Persistent Map Serializer
    public static class PersistentMapSerializer
    {
        public static void SaveMap(Map map, PlanetTile tile)
        {
            string folder = GetPersistentFolder();
            if (string.IsNullOrEmpty(folder))
                return;

            string file = Path.Combine(folder, $"Tile_{tile.tileId}.xml");

            try
            {
                // -----------------------------
                // Build lightweight persistent data
                // -----------------------------
                PersistentMapData data = new PersistentMapData();

                // Save when this was abandoned so we can calculate a diff and decay stuff
                data.abandonedAtTick = Find.TickManager.TicksGame;

                // Terrain
                data.terrainData = MapSerializeUtility.SerializeUshort(
                    map,
                    c => (ushort)map.terrainGrid.TerrainAt(c).shortHash
                );

                // Roof
                data.roofData = MapSerializeUtility.SerializeByte(
                    map,
                    c => map.roofGrid.Roofed(c) ? (byte)1 : (byte)0
                );

                // Snow
                data.snowData = MapSerializeUtility.SerializeByte(
                    map,
                    c => (byte)map.snowGrid.GetDepth(c)
                );

                // Pollution (if Biotech)
                if (ModsConfig.BiotechActive && map.pollutionGrid != null)
                {
                    data.pollutionData = MapSerializeUtility.SerializeByte(
                        map,
                        c => map.pollutionGrid.IsPolluted(c) ? (byte)1 : (byte)0
                    );
                }

                // -----------------------------
                // Save Compressible Buildings (SAFE)
                // -----------------------------
                data.buildings = new List<PersistentBuildingData>();

                // Make sure to mirror this logic in load otherwise bad things happen mate
                foreach (Thing t in map.listerThings.AllThings)
                {
                    if (t.def.category != ThingCategory.Building)
                        continue;

                    if (t.def.IsBlueprint || t.def.IsFrame)
                        continue;
                    
                    if (!t.def.destroyable)
                        continue;

                    data.buildings.Add(new PersistentBuildingData
                    {
                        defName = t.def.defName,
                        stuffDefName = t.Stuff?.defName,
                        factionDefName = t.Faction?.def.defName,
                        position = t.Position,
                        rotation = t.Rotation.AsInt,
                        hitPoints = t.HitPoints
                    });
                }



                // -----------------------------
                // Save to XML
                // -----------------------------
                Scribe.saver.InitSaving(file, "PersistentMap");
                Scribe.mode = LoadSaveMode.Saving;

                Scribe_Deep.Look(ref data, "MapData");

                Scribe.saver.FinalizeSaving();

                Log.Message($"[PersistentMaps] Saved persistent map to {file}");
            }
            catch (Exception e)
            {
                Log.Error($"[PersistentMaps] Failed saving map: {e}");
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
            }
        }
    
        public static void LoadSavedMap(Map map, PlanetTile tile)
        {
            string folder = GetPersistentFolder();
            if (string.IsNullOrEmpty(folder))
                return;

            string file = Path.Combine(folder, $"Tile_{tile.tileId}.xml");

            if (!File.Exists(file))
                return;

            try
            {
                PersistentMapData data = null;

                // -----------------------------
                // Load persistent data object
                // -----------------------------
                Scribe.loader.InitLoading(file);
                Scribe.mode = LoadSaveMode.LoadingVars;

                Scribe_Deep.Look(ref data, "MapData");

                Scribe.loader.FinalizeLoading();

                if (data == null)
                {
                    Log.Error("[PersistentMaps] Loaded data is null.");
                    return;
                }

                // -----------------------------
                // Apply Terrain
                // -----------------------------
                if (data.terrainData != null)
                {
                    MapSerializeUtility.LoadUshort(
                        data.terrainData,
                        map,
                        (c, val) =>
                        {
                            TerrainDef def = DefDatabase<TerrainDef>.GetByShortHash(val);
                            if (def != null)
                                map.terrainGrid.SetTerrain(c, def);
                        });
                }

                // -----------------------------
                // Apply Roof
                // -----------------------------
                if (data.roofData != null)
                {
                    MapSerializeUtility.LoadByte(
                        data.roofData,
                        map,
                        (c, val) =>
                        {
                            if (val == 1)
                                map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                            else
                                map.roofGrid.SetRoof(c, null);
                        });
                }

                // -----------------------------
                // Apply Snow
                // -----------------------------
                if (data.snowData != null)
                {
                    MapSerializeUtility.LoadByte(
                        data.snowData,
                        map,
                        (c, val) =>
                        {
                            map.snowGrid.SetDepth(c, val);
                        });
                }

                // -----------------------------
                // Apply Pollution (Biotech)
                // -----------------------------
                if (ModsConfig.BiotechActive && map.pollutionGrid != null && data.pollutionData != null)
                {
                    MapSerializeUtility.LoadByte(
                        data.pollutionData,
                        map,
                        (c, val) =>
                        {
                            map.pollutionGrid.SetPolluted(c, val == 1, silent: true);
                        });
                }

                if (data.buildings != null)
                {
                    // Make sure to mirror this logic in save otherwise bad things happen mate
                    foreach (Thing thing in map.listerThings.AllThings.ToList())
                    {
                        if (!thing.def.destroyable)
                            continue;
                        
                        if (thing.def.category == ThingCategory.Building)
                            thing.Destroy(DestroyMode.Vanish);
                    }

                    foreach (PersistentBuildingData b in data.buildings)
                    {
                        ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(b.defName);
                        if (def == null)
                            continue;

                        ThingDef stuff = null;
                        if (!string.IsNullOrEmpty(b.stuffDefName))
                            stuff = DefDatabase<ThingDef>.GetNamedSilentFail(b.stuffDefName);

                        Thing thing = ThingMaker.MakeThing(def, stuff);

                        thing.HitPoints = b.hitPoints < 1
                            ? 1
                            : (b.hitPoints > thing.MaxHitPoints ? thing.MaxHitPoints : b.hitPoints);
                        thing.Rotation = new Rot4(b.rotation);


                        if (!string.IsNullOrEmpty(b.factionDefName))
                        {
                            FactionDef fDef = DefDatabase<FactionDef>.GetNamedSilentFail(b.factionDefName);
                            if (fDef != null)
                            {
                                Faction faction = Find.FactionManager.FirstFactionOfDef(fDef);
                                if (faction != null)
                                    thing.SetFaction(faction);
                            }
                        }

                        GenSpawn.Spawn(thing, b.position, map);
                    }
                }

                Log.Message($"[PersistentMaps] Applied saved data for tile {tile}");
            }
            catch (Exception e)
            {
                Log.Error($"[PersistentMaps] Failed applying saved data: {e}");
                Scribe.mode = LoadSaveMode.Inactive;
            }
        }


        
        public static bool PersistentFileExists(PlanetTile tile)
        {
            string folder = GetPersistentFolder();
            string file = Path.Combine(folder, $"Tile_{tile.tileId}.xml");
            return File.Exists(file);
        }

        private static string GetPersistentFolder()
        {
            if (Current.Game == null)
            {
                Log.Error("[PersistentMaps] Current.Game is null.");
                return null;
            }

            var comp = Current.Game.GetComponent<GameComponent_PersistentMaps>();

            if (comp == null)
            {
                Log.Error("[PersistentMaps] GameComponent_PersistentMaps not found.");
                return null;
            }

            if (string.IsNullOrEmpty(comp.persistentId))
            {
                Log.Error("[PersistentMaps] persistentId is null or empty.");
                return null;
            }

            string folder = Path.Combine(
                GenFilePaths.SaveDataFolderPath,
                "PersistentMaps",
                comp.persistentId
            );

            Directory.CreateDirectory(folder);

            return folder;
        }


    }
}
