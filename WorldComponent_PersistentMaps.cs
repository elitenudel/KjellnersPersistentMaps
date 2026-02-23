using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using Verse;

namespace KjellnersPersistentMaps
{
    // Position + pawn reference for a colonist parked in WorldPawns while their
    // casket is serialised to our tile XML. WorldPawns deep-saves the pawn in the
    // main .rws; we only store the casket position so we can re-insert on restore.
    public class CryoPawnRecord : IExposable
    {
        public Pawn pawn;
        public IntVec3 position;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref position, "position");
        }
    }

    // One record per abandoned tile. Serialized inside the main .rws save
    // alongside WorldPawns so that RAM-resident pawn objects and their
    // cross-references (relations, ideo roles, faction membership) remain
    // valid while the tile is unloaded on disk.
    public class TileRecord : IExposable
    {
        public int tileId;

        // Animals moved to WorldPawns when this tile was abandoned.
        // WorldPawns deep-saves them in the main save; we hold references
        // here so we know which WorldPawn entries belong to this tile and
        // can pull them back on restoration.
        public List<Pawn> parkedPawns = new List<Pawn>();

        // Player colonists that were in cryptosleep caskets when the tile was
        // abandoned. Moved to WorldPawns(KeepForever) so faction membership and
        // roles stay intact in the main save. Re-inserted into their caskets on
        // restoration via TryAcceptThing.
        public List<CryoPawnRecord> cryoPawns = new List<CryoPawnRecord>();

        // TODO: protectedTales — Artworks on this tile reference Tales in
        // TaleManager by load ID. TaleManager.RemoveExpiredTales() culls
        // tales older than def.expireDays. Holding Tale references here would
        // prevent that GC during long absences. Deferred; worst effect without
        // it is artworks losing their specific narrative description over time.

        public void ExposeData()
        {
            Scribe_Values.Look(ref tileId, "tileId");

            // LookMode.Reference: WorldPawns owns the deep-save of each pawn.
            // We only record which ones belong to this tile.
            Scribe_Collections.Look(ref parkedPawns, "parkedPawns", LookMode.Reference);
            Scribe_Collections.Look(ref cryoPawns, "cryoPawns", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                parkedPawns ??= new List<Pawn>();
                parkedPawns.RemoveAll(p => p == null);
                cryoPawns ??= new List<CryoPawnRecord>();
                cryoPawns.RemoveAll(r => r?.pawn == null);
            }
        }
    }

    // Auto-discovered by RimWorld via reflection; the (World world) constructor
    // signature is required. Serialized with the main save.
    public class WorldComponent_PersistentMaps : WorldComponent
    {
        private List<TileRecord> records = new List<TileRecord>();

        public WorldComponent_PersistentMaps(World world) : base(world) { }

        public TileRecord GetOrCreate(int tileId)
        {
            var rec = records.FirstOrDefault(r => r.tileId == tileId);
            if (rec == null)
            {
                rec = new TileRecord { tileId = tileId };
                records.Add(rec);
            }
            return rec;
        }

        public TileRecord TryGet(int tileId) =>
            records.FirstOrDefault(r => r.tileId == tileId);

        public void Release(int tileId) =>
            records.RemoveAll(r => r.tileId == tileId);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref records, "tileRecords", LookMode.Deep);
            records ??= new List<TileRecord>();
        }
    }
}
