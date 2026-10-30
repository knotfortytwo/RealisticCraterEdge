using System;
using System.Collections.Generic;

namespace CraterEdge.Generation
{
    public static class Patrols
    {
        public sealed class Zone
        {
            public readonly double X, Z, Radius;
            public readonly List<(double X, double Y, double Z)> Route;

            public Zone(double x, double z, double radius, List<(double, double, double)> route)
            {
                X = x; Z = z; Radius = radius; Route = route;
            }
        }

        private const double Cell = 3000, Jitter = 0.6, Chance = 0.45;
        private const double BoxClearance = 700, RimClearance = 400, MinDepth = -2000;
        private const double RadiusMin = 300, RadiusMax = 500, RouteMin = 0.75, RouteSpread = 0.5;
        private const double Clearance = 60, ClearanceSpread = 40, Lateral = 40, Step = 25, Ceiling = -150;
        private const uint SeedZone = 0x2c1b3c6dU, SeedRoute = 0x297a2d39U;

        private const double S = 0.8660254037844386;
        private static readonly double[] DirX = { 1, S, 0.5, 0, -0.5, -S, -1, -S, -0.5, 0, 0.5, S };
        private static readonly double[] DirZ = { 0, 0.5, S, 1, S, 0.5, 0, -0.5, -S, -1, -S, -0.5 };

        public static List<Zone> Place(FarField far)
        {
            var zones = new List<Zone>();
            double origin = -FarField.Extent;
            int n = (int)Math.Ceiling(2 * FarField.Extent / Cell);
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    uint h = Noise.Hash(i, k, SeedZone);
                    if (Landforms.Unit(h) >= Chance)
                    {
                        continue;
                    }
                    double x = origin + (i + 0.5 + Jitter * (Landforms.Unit(Noise.Hash(h + 1)) - 0.5)) * Cell;
                    double z = origin + (k + 0.5 + Jitter * (Landforms.Unit(Noise.Hash(h + 2)) - 0.5)) * Cell;
                    double radius = Noise.Lerp(RadiusMin, RadiusMax, Landforms.Unit(Noise.Hash(h + 3)));
                    double reach = radius * (RouteMin + RouteSpread) + Lateral;
                    double outsideBox = Math.Max(Math.Abs(x) - World.SizeX / 2.0, Math.Abs(z) - World.SizeZ / 2.0);
                    if (outsideBox < BoxClearance + reach || Math.Max(Math.Abs(x), Math.Abs(z)) + reach > FarField.Extent - RimClearance)
                    {
                        continue;
                    }
                    double ground = far.HeightAt(x, z, false);
                    if (double.IsNaN(ground) || ground > MinDepth)
                    {
                        continue;
                    }
                    zones.Add(new Zone(x, z, radius, Route(far, x, z, radius, h ^ SeedRoute)));
                }
            }
            return zones;
        }

        private static List<(double, double, double)> Route(FarField far, double cx, double cz, double radius, uint h)
        {
            int count = DirX.Length;
            var points = new (double x, double z)[count];
            for (int i = 0; i < count; i++)
            {
                double r = radius * (RouteMin + RouteSpread * Landforms.Unit(Noise.Hash(h + 10 + (uint)i)));
                points[i] = (cx + r * DirX[i], cz + r * DirZ[i]);
            }
            var route = new List<(double, double, double)>(count);
            for (int i = 0; i < count; i++)
            {
                double top = Math.Max(Highest(far, points[(i + count - 1) % count], points[i]), Highest(far, points[i], points[(i + 1) % count]));
                double y = Math.Min(top + Clearance + ClearanceSpread * Landforms.Unit(Noise.Hash(h + 30 + (uint)i)), Ceiling);
                route.Add((points[i].x, y, points[i].z));
            }
            return route;
        }

        private static double Highest(FarField far, (double x, double z) a, (double x, double z) b)
        {
            double dx = b.x - a.x, dz = b.z - a.z, length = Math.Sqrt(dx * dx + dz * dz);
            double sx = length > 0 ? -dz / length : 0, sz = length > 0 ? dx / length : 0;
            int steps = Math.Max(1, (int)Math.Ceiling(length / Step));
            double top = double.MinValue;
            for (int s = 0; s <= steps; s++)
            {
                double x = a.x + dx * s / steps, z = a.z + dz * s / steps;
                for (int side = -1; side <= 1; side++)
                {
                    double h = far.HeightAt(x + side * Lateral * sx, z + side * Lateral * sz, false);
                    if (!double.IsNaN(h))
                    {
                        top = Math.Max(top, h);
                    }
                }
            }
            return top;
        }
    }
}
