using RimWorld;
using Verse;
using System;

namespace KjellnersPersistentMaps
{
    // Stores a UUID in the save file to tie it to the external PersistentMaps data folder.
    public class GameComponent_PersistentMaps : GameComponent
    {
        public string persistentId;

        public GameComponent_PersistentMaps(Game game)
        {
            persistentId = Guid.NewGuid().ToString();
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref persistentId, "persistentId");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                KLog.Message($"[PersistentMaps] Persistent ID: {persistentId}");
        }
    }
}
