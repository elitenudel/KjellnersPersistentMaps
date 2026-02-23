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
    public class PersistentMapData : IExposable
    {
        public byte[] terrainData;
        public byte[] roofData;
        public byte[] snowData;
        public byte[] pollutionData;
        public byte[] fogData;

        public List<Thing> savedThings;

        public int abandonedAtTick;

        public void ExposeData()
        {
            DataExposeUtility.LookByteArray(ref terrainData, "terrainData");
            DataExposeUtility.LookByteArray(ref roofData, "roofData");
            DataExposeUtility.LookByteArray(ref snowData, "snowData");
            DataExposeUtility.LookByteArray(ref pollutionData, "pollutionData");
            DataExposeUtility.LookByteArray(ref fogData, "fogData");

            Scribe_Collections.Look(ref savedThings, "savedThings", LookMode.Deep);

            Scribe_Values.Look(ref abandonedAtTick, "abandonedAtTick", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && savedThings == null)
                savedThings = new List<Thing>();
        }

        public static bool ShouldPersistThing(Thing t)
        {
            if (t.Destroyed) return false;
            if (t is Pawn p)
            {
                if (p.RaceProps.Humanlike) return false; // transient NPCs; colonists leave with caravan
                // Non-humanlike world pawns (e.g. ancient danger Megascarab bodyguards freed
                // from caskets) stay tracked in WorldPawns even while spawned on the map.
                // GenStep_AncientDanger re-creates them on every mapgen, so saving them here
                // causes ID collisions and duplicate pawns on restore.
                if (Find.WorldPawns.GetSituation(p) != WorldPawnSituation.None) return false;
            }
            if (t is Corpse) return false;     // corpse inner-pawn has unresolvable cross-refs in isolation
            if (t is Building_Casket casket)
            {
                // Ancient danger casket inner pawns are world pawns: they live in
                // WorldPawns.AllPawnsAlive AND inside their casket simultaneously.
                // GenStep_AncientDanger re-populates caskets from WorldPawns on every map
                // generation, so we must NOT deep-save them — doing so creates a duplicate
                // ID registration at restore time (RefInjector registers them from WorldLevel;
                // our XML would then try to register new Pawn objects with the same IDs).
                //
                // Skip any casket whose inner pawn is already tracked as a live world pawn.
                // Empty caskets and caskets with non-world-pawn inner pawns (player cryo
                // colonists) fall through and are included in savedThings.
                ThingOwner held = casket.GetDirectlyHeldThings();
                for (int i = 0; i < held.Count; i++)
                {
                    if (held[i] is Pawn ip &&
                        Find.WorldPawns.GetSituation(ip) != WorldPawnSituation.None)
                        return false;
                }
            }
            if (t.def.IsBlueprint || t.def.IsFrame) return false;
            if (t.def.category == ThingCategory.Mote) return false;
            if (t.def.category == ThingCategory.Ethereal) return false;
            if (t is Skyfaller) return false;
            if (t is Projectile) return false;
            if (t is Filth) return false;
            if (!t.Spawned) return false;
            if (!t.def.destroyable) return false;

            return true;
        }

        // Returns true for spawned pawns that should be moved to WorldPawns
        // on map abandonment rather than serialized to our XML.
        // Currently covers wildlife; humanlike non-player pawns (visitors,
        // raiders) are transient and intentionally excluded.
        // Player colonists in cryptosleep caskets: TODO — future pass.
        public static bool ShouldMoveToWorldPawns(Pawn p)
        {
            if (p == null || p.Destroyed) return false;
            if (!p.Spawned) return false;
            if (p.Faction == Faction.OfPlayer) return false; // leave with caravan
            if (p.RaceProps.Humanlike) return false;          // transient NPCs
            return true;                                       // wildlife
        }
    }
}