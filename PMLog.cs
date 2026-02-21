using RimWorld;
using RimWorld.Planet;
using Verse;
using HarmonyLib;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;


namespace KjellnersPersistentMaps
{
    public static class KLog
    {
        public static void Message(string msg)
        {
            if (Prefs.DevMode)
                Log.Message("[PersistentMaps] " + msg);
        }

        public static void Warning(string msg)
        {
            Log.Warning("[PersistentMaps] " + msg);
        }

        public static void Error(string msg)
        {
            Log.Error("[PersistentMaps] " + msg);
        }
    }
}