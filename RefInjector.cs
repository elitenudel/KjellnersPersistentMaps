using System.Reflection;
using Verse;
using RimWorld;

namespace KjellnersPersistentMaps
{
    public static class RefInjector
    {
        // loadedObjectDirectory is private on CrossRefHandler.
        // We inject live game objects directly into it so they are findable
        // as cross-ref targets when the persistent map's objects resolve their
        // references — without adding them to crossReferencingExposables, which
        // would cause their ExposeData() to be called in ResolvingCrossRefs mode
        // and corrupt their live fields (setting faction leaders, ideos, etc. to null).
        private static readonly FieldInfo _loadedObjectDirectoryField =
            typeof(CrossRefHandler).GetField("loadedObjectDirectory", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void PreRegisterActiveGame()
        {
            if (Scribe.loader?.crossRefs == null)
                return;

            if (_loadedObjectDirectoryField == null)
            {
                Log.Error("[PersistentMaps] Could not find loadedObjectDirectory field on CrossRefHandler via reflection.");
                return;
            }

            var dir = (LoadedObjectDirectory)_loadedObjectDirectoryField.GetValue(Scribe.loader.crossRefs);
            if (dir == null)
                return;

            RegisterGameLevel(dir);
            RegisterWorldLevel(dir);
            RegisterMapLevel(dir);
        }

        private static void TryRegister(ILoadReferenceable reffable, LoadedObjectDirectory dir)
        {
            if (reffable == null) return;
            dir.RegisterLoaded(reffable);
        }

        private static void RegisterGameLevel(LoadedObjectDirectory dir)
        {
            if (Current.Game == null)
                return;

            TryRegister(Current.Game as ILoadReferenceable, dir);

            foreach (var comp in Current.Game.components)
                TryRegister(comp as ILoadReferenceable, dir);
        }

        private static void RegisterWorldLevel(LoadedObjectDirectory dir)
        {
            if (Find.World == null)
                return;

            TryRegister(Find.World as ILoadReferenceable, dir);

            foreach (var comp in Find.World.components)
                TryRegister(comp as ILoadReferenceable, dir);

            if (Find.FactionManager != null)
                foreach (var faction in Find.FactionManager.AllFactionsListForReading)
                    TryRegister(faction, dir);

            if (Find.IdeoManager != null)
                foreach (var ideo in Find.IdeoManager.IdeosListForReading)
                    TryRegister(ideo, dir);

            if (Find.WorldPawns != null)
                foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead)
                    TryRegister(pawn, dir);

            if (Find.WorldObjects != null)
                foreach (var obj in Find.WorldObjects.AllWorldObjects)
                    TryRegister(obj, dir);
        }

        private static void RegisterMapLevel(LoadedObjectDirectory dir)
        {
            if (Find.Maps == null)
                return;

            foreach (var map in Find.Maps)
            {
                TryRegister(map, dir);

                foreach (var comp in map.components)
                    TryRegister(comp as ILoadReferenceable, dir);

                foreach (var thing in map.listerThings.AllThings)
                    TryRegister(thing, dir);
            }
        }
    }
}
