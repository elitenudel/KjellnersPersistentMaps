using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;
using System;

namespace KjellnersPersistentMaps
{
    public struct OfflineDecayResult
    {
        public bool shouldSpawn;
        public int resultingHp;
        public float resultingRotProgress;
    }

    public struct OfflineDecayContext
    {
        public Map map;
        public int startTick;
        public int ticksPassed;
        public int tileId;
        public float rainfall;
    }

    public static class DecayUtility
    {
        public static void ApplyDecay(Thing thing, OfflineDecayContext context)
        {
            if (thing.Destroyed)
                return;

            // -------------------------------------------------
            // ROT
            // -------------------------------------------------
            var rotComp = thing.TryGetComp<CompRottable>();
            if (rotComp != null)
            {
                float rot = rotComp.RotProgress;

                int startTick = context.startTick;
                int endTick = startTick + context.ticksPassed;

                const int hourStep = 2500;

                var rotProps = rotComp.PropsRot;

                for (int tick = startTick; tick < endTick; tick += hourStep)
                {
                    int remaining = endTick - tick;
                    int step = remaining < hourStep ? remaining : hourStep;

                    float temp =
                        GenTemperature.GetTemperatureFromSeasonAtTile(
                            tick,
                            context.tileId);

                    temp += GenTemperature.OffsetFromSunCycle(
                        tick,
                        context.tileId);

                    float rotRate = GenTemperature.RotRateAtTemperature(temp);

                    rot += rotRate * step;

                    if (rot >= rotProps.TicksToRotStart)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                        return;
                    }
                }

                rotComp.RotProgress = rot;
            }

            // -------------------------------------------------
            // OUTDOOR DETERIORATION
            // -------------------------------------------------
            if (!thing.Destroyed && !context.map.roofGrid.Roofed(thing.Position))
            {
                float intervals = context.ticksPassed / 250f;

                // Clamp 0..1
                float normalizedRain =
                    Math.Max(0f, Math.Min(1f, context.rainfall / 4000f));

                // Lerp 0.5 → 2.0 manually
                float rainFactor =
                    0.5f + (2f - 0.5f) * normalizedRain;

                float expectedDamage =
                    intervals * 0.015f * rainFactor;

                int damage = (int)Math.Round(expectedDamage);

                if (damage > 0)
                {
                    thing.TakeDamage(
                        new DamageInfo(DamageDefOf.Deterioration, damage)
                    );
                }
            }
        }

        private static bool IsRottingFood(ThingDef def)
        {
            return def.IsIngestible &&
                   def.GetCompProperties<CompProperties_Rottable>() != null;
        }

        private static int CalculateOutdoorDeterioration(
            OfflineDecayContext context)
        {
            float deteriorationIntervals =
                context.ticksPassed / 250f;

            // Clamp 0..1 manually
            float normalizedRain =
                Math.Max(0f, Math.Min(1f, context.rainfall / 4000f));

            // Manual Lerp between 0.5 and 2.0
            float rainFactor =
                0.5f + (2f - 0.5f) * normalizedRain;

            float expectedDamage =
                deteriorationIntervals * 0.015f * rainFactor;

            return (int)Math.Round(expectedDamage);
        }
    }
}