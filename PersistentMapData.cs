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

        public List<PersistentBuildingData> buildings;

        public int abandonedAtTick;

        public void ExposeData()
        {
            DataExposeUtility.LookByteArray(ref terrainData, "terrainData");
            DataExposeUtility.LookByteArray(ref roofData, "roofData");
            DataExposeUtility.LookByteArray(ref snowData, "snowData");
            DataExposeUtility.LookByteArray(ref pollutionData, "pollutionData");
            Scribe_Collections.Look(ref buildings, "buildings", LookMode.Deep);
            Scribe_Values.Look(ref abandonedAtTick, "abandonedAtTick", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && buildings == null)
                buildings = new List<PersistentBuildingData>();
            
        }
    }

    public class PersistentBuildingData : IExposable
    {
        public string defName;
        public string stuffDefName;
        public string factionDefName;

        public IntVec3 position;
        public int rotation;
        public int hitPoints;

        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName");
            Scribe_Values.Look(ref stuffDefName, "stuffDefName");
            Scribe_Values.Look(ref factionDefName, "factionDefName");

            Scribe_Values.Look(ref position, "position");
            Scribe_Values.Look(ref rotation, "rotation");
            Scribe_Values.Look(ref hitPoints, "hitPoints");
        }
    }
}