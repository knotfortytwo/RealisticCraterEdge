using System.Collections.Generic;
using CraterEdge.Generation;
using HarmonyLib;
using UnityEngine;

namespace RealisticCraterEdge
{
    [HarmonyPatch(typeof(WaterTemperatureSimulation), nameof(WaterTemperatureSimulation.GetTemperature), new[] { typeof(Vector3) })]
    internal static class Heat
    {
        private const float VentHeat = 75, VentReach = 45, GeyserHeat = 90, GeyserReach = 60, Near = 2;
        private const int Cell = 64;

        private static Features indexed;
        private static readonly Dictionary<long, List<Features.Feature>> cells = new Dictionary<long, List<Features.Feature>>();

        private static void Postfix(Vector3 wsPos, ref float __result)
        {
            Features features = Terrain.Cache?.Features;
            if (features == null)
            {
                return;
            }
            if (!ReferenceEquals(features, indexed))
            {
                Index(features);
            }
            __result += At(wsPos);
        }

        public static float At(Vector3 p)
        {
            int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
            float best = 0;
            for (int i = cx - 1; i <= cx + 1; i++)
            {
                for (int k = cz - 1; k <= cz + 1; k++)
                {
                    if (!cells.TryGetValue(Key(i, k), out var list))
                    {
                        continue;
                    }
                    foreach (var f in list)
                    {
                        bool geyser = f.Kind == Features.Kind.Geyser;
                        float reach = geyser ? GeyserReach : VentReach;
                        float dx = p.x - f.X, dy = p.y - f.Y, dz = p.z - f.Z;
                        float d2 = dx * dx + dy * dy + dz * dz;
                        if (d2 >= reach * reach)
                        {
                            continue;
                        }
                        float t = Mathf.InverseLerp(Near, reach, Mathf.Sqrt(d2));
                        best = Mathf.Max(best, (geyser ? GeyserHeat : VentHeat) * (1 - t * t * (3 - 2 * t)));
                    }
                }
            }
            return best;
        }

        private static void Index(Features features)
        {
            cells.Clear();
            foreach (var f in features.All)
            {
                if (f.Kind != Features.Kind.Vent && f.Kind != Features.Kind.Geyser)
                {
                    continue;
                }
                long key = Key(Mathf.FloorToInt(f.X / Cell), Mathf.FloorToInt(f.Z / Cell));
                if (!cells.TryGetValue(key, out var list))
                {
                    cells[key] = list = new List<Features.Feature>();
                }
                list.Add(f);
            }
            indexed = features;
        }

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}
