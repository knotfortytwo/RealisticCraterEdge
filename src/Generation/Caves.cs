using System;
using System.Collections.Generic;

namespace CraterEdge.Generation
{
    public sealed class Caves
    {
        private const int Tubes = 10, MinSegments = 8, MaxSegments = 20, BranchSegments = 5;
        private const double SegmentLength = 40, BoxMargin = 90, FloorMargin = 70, CutMargin = 60, EntranceOutside = 20,
            EntranceTop = -1450, EntranceBottom = -1900, RadiusMin = 8, RadiusMax = 15, Turn = 0.3, ClimbMin = -0.12, ClimbMax = 0.06,
            ChamberMin = 25, ChamberMax = 45, BranchChance = 0.5, BranchTurn = 0.8, BranchRadius = 0.7, BranchChamberMin = 18,
            BranchChamberMax = 25, Roughness = 2, RoughnessSize = 12, RoughnessNear = 4, RoughnessFar = 8;
        private const double CullMargin = 30;
        private const uint SeedCaves = 0x51ed270bU, SeedRough = 0x1656667bU;

        public readonly struct Segment
        {
            public readonly double Ax, Ay, Az, Bx, By, Bz, Ra, Rb, MinX, MaxX, MinY, MaxY, MinZ, MaxZ;

            public Segment(double ax, double ay, double az, double bx, double by, double bz, double ra, double rb)
            {
                Ax = ax; Ay = ay; Az = az; Bx = bx; By = by; Bz = bz; Ra = ra; Rb = rb;
                double m = Math.Max(ra, rb) + Roughness + CullMargin;
                MinX = Math.Min(ax, bx) - m; MaxX = Math.Max(ax, bx) + m;
                MinY = Math.Min(ay, by) - m; MaxY = Math.Max(ay, by) + m;
                MinZ = Math.Min(az, bz) - m; MaxZ = Math.Max(az, bz) + m;
            }
        }

        public readonly struct Chamber
        {
            public readonly double X, Y, Z, Radius;

            public Chamber(double x, double y, double z, double radius)
            {
                X = x; Y = y; Z = z; Radius = radius;
            }
        }

        public readonly List<Segment> Segments = new List<Segment>();
        public readonly List<Chamber> Chambers = new List<Chamber>();
        public readonly List<Chamber> Entrances = new List<Chamber>();
        private double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;

        public static Caves Build(Field field, IslandModel model)
        {
            var caves = new Caves();
            var column = new Field.Column();
            for (int k = 0; k < Tubes; k++)
            {
                caves.Tube(k, field, model, column);
            }
            foreach (var s in caves.Segments)
            {
                caves.minX = Math.Min(caves.minX, s.MinX);
                caves.maxX = Math.Max(caves.maxX, s.MaxX);
                caves.minZ = Math.Min(caves.minZ, s.MinZ);
                caves.maxZ = Math.Max(caves.maxZ, s.MaxZ);
            }
            return caves;
        }

        public void Near(double x, double z, List<int> result)
        {
            result.Clear();
            if (x < minX || x > maxX || z < minZ || z > maxZ)
            {
                return;
            }
            for (int i = 0; i < Segments.Count; i++)
            {
                var s = Segments[i];
                if (x >= s.MinX && x <= s.MaxX && z >= s.MinZ && z <= s.MaxZ)
                {
                    result.Add(i);
                }
            }
        }

        public double Distance(double x, double y, double z, List<int> near)
        {
            double best = double.MaxValue;
            foreach (int i in near)
            {
                var s = Segments[i];
                if (y < s.MinY || y > s.MaxY)
                {
                    continue;
                }
                double abx = s.Bx - s.Ax, aby = s.By - s.Ay, abz = s.Bz - s.Az;
                double apx = x - s.Ax, apy = y - s.Ay, apz = z - s.Az;
                double ab2 = abx * abx + aby * aby + abz * abz;
                double t = ab2 > 0 ? (apx * abx + apy * aby + apz * abz) / ab2 : 0;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double dx = apx - abx * t, dy = apy - aby * t, dz = apz - abz * t;
                best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy + dz * dz) - Noise.Lerp(s.Ra, s.Rb, t));
            }
            if (best < RoughnessFar)
            {
                best += Roughness * (1 - Noise.SmoothStep(RoughnessNear, RoughnessFar, best))
                        * Noise.Gradient(x / RoughnessSize, y / RoughnessSize, z / RoughnessSize, SeedRough);
            }
            return best;
        }

        private void Tube(int k, Field field, IslandModel model, Field.Column column)
        {
            uint h = Noise.Hash((uint)k ^ SeedCaves);
            double ux = Landforms.Unit(Noise.Hash(h + 1)) - 0.5, uz = Landforms.Unit(Noise.Hash(h + 2)) - 0.5;
            double len = Math.Sqrt(ux * ux + uz * uz);
            if (len < 0.05)
            {
                return;
            }
            ux /= len;
            uz /= len;
            double entranceY = Noise.Lerp(EntranceTop, EntranceBottom, Landforms.Unit(Noise.Hash(h + 3)));

            double r = 0;
            int outside = 0;
            while (r < 3000 && outside < 3)
            {
                r += 4;
                outside = IsIsland(model, r * ux, r * uz) ? 0 : outside + 1;
            }
            double wall = -1;
            for (double d = r - 12; d < r + 1500; d += 2)
            {
                double x = d * ux, z = d * uz;
                if (!InBox(x, z, BoxMargin))
                {
                    return;
                }
                field.Prepare(column, x, z);
                if (field.EvaluateSurface(column, BaseAt(model, x, z), entranceY) < 0)
                {
                    wall = d;
                    break;
                }
            }
            if (wall < 0)
            {
                return;
            }

            double radius = Noise.Lerp(RadiusMin, RadiusMax, Landforms.Unit(Noise.Hash(h + 4)));
            double climb = Noise.Lerp(ClimbMin, ClimbMax, Landforms.Unit(Noise.Hash(h + 5)));
            int segments = MinSegments + (int)(Noise.Hash(h + 6) % (uint)(MaxSegments - MinSegments + 1));
            double px = (wall + EntranceOutside) * ux, py = entranceY, pz = (wall + EntranceOutside) * uz;
            double hx = -ux, hz = -uz, pr = radius;
            var path = new List<(double x, double y, double z, double hx, double hz)> { (px, py, pz, hx, hz) };
            for (int i = 0; i < segments; i++)
            {
                Rotate(ref hx, ref hz, Turn * (Landforms.Unit(Noise.Hash(h + 10 + (uint)i)) - 0.5));
                double nx = px + hx * SegmentLength, ny = py + climb * SegmentLength, nz = pz + hz * SegmentLength;
                double nr = radius * (0.8 + 0.4 * Landforms.Unit(Noise.Hash(h + 40 + (uint)i)));
                if (!Clear(model, nx, ny, nz, nr))
                {
                    break;
                }
                Segments.Add(new Segment(px, py, pz, nx, ny, nz, pr, nr));
                px = nx; py = ny; pz = nz; pr = nr;
                path.Add((px, py, pz, hx, hz));
            }
            if (path.Count > 1)
            {
                Entrances.Add(new Chamber(path[0].x, path[0].y, path[0].z, radius));
            }
            if (path.Count - 1 < MinSegments / 2)
            {
                return;
            }
            AddChamber(model, px, py, pz, Noise.Lerp(ChamberMin, ChamberMax, Landforms.Unit(Noise.Hash(h + 7))));

            if (Landforms.Unit(Noise.Hash(h + 8)) < BranchChance && path.Count > MinSegments)
            {
                var (bx, by, bz, bhx, bhz) = path[path.Count / 2];
                Rotate(ref bhx, ref bhz, (Noise.Hash(h + 9) & 1) == 0 ? BranchTurn : -BranchTurn);
                double br = radius * BranchRadius;
                for (int i = 0; i < BranchSegments; i++)
                {
                    Rotate(ref bhx, ref bhz, Turn * (Landforms.Unit(Noise.Hash(h + 80 + (uint)i)) - 0.5));
                    double nx = bx + bhx * SegmentLength, ny = by + climb * SegmentLength, nz = bz + bhz * SegmentLength;
                    if (!Clear(model, nx, ny, nz, br))
                    {
                        break;
                    }
                    Segments.Add(new Segment(bx, by, bz, nx, ny, nz, br, br));
                    bx = nx; by = ny; bz = nz;
                }
                AddChamber(model, bx, by, bz, Noise.Lerp(BranchChamberMin, BranchChamberMax, Landforms.Unit(Noise.Hash(h + 90))));
            }
        }

        private void AddChamber(IslandModel model, double x, double y, double z, double radius)
        {
            for (int attempt = 0; attempt < 3; attempt++, radius *= 0.7)
            {
                if (Clear(model, x, y, z, radius))
                {
                    Segments.Add(new Segment(x, y, z, x, y, z, radius, radius));
                    Chambers.Add(new Chamber(x, y, z, radius));
                    return;
                }
            }
        }

        private static bool Clear(IslandModel model, double x, double y, double z, double r)
        {
            if (!InBox(x, z, BoxMargin + r) || y - r < World.MinY + FloorMargin)
            {
                return false;
            }
            for (int i = -1; i <= 1; i++)
            {
                for (int k = -1; k <= 1; k++)
                {
                    if (y + r > CutAt(model, x + i * r, z + k * r) - CutMargin)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool InBox(double x, double z, double margin) =>
            x > World.MinX + margin && x < World.MinX + World.SizeX - margin && z > World.MinZ + margin && z < World.MinZ + World.SizeZ - margin;

        private static int Column(double x, double z)
        {
            int cx = (int)Math.Floor(x) - World.MinX, cz = (int)Math.Floor(z) - World.MinZ;
            cx = cx < 0 ? 0 : cx >= World.SizeX ? World.SizeX - 1 : cx;
            cz = cz < 0 ? 0 : cz >= World.SizeZ ? World.SizeZ - 1 : cz;
            return cx * IslandModel.Width + cz;
        }

        private static bool IsIsland(IslandModel model, double x, double z) => model.Island[Column(x, z)];

        private static double BaseAt(IslandModel model, double x, double z) => model.Base[Column(x, z)];

        private static double CutAt(IslandModel model, double x, double z) => model.Cut[Column(x, z)];

        private static void Rotate(ref double hx, ref double hz, double a)
        {
            double a2 = a * a;
            double c = 1 - a2 / 2 + a2 * a2 / 24, s = a * (1 - a2 / 6 + a2 * a2 / 120);
            double x = hx * c - hz * s, z = hx * s + hz * c;
            double n = Math.Sqrt(x * x + z * z);
            hx = x / n;
            hz = z / n;
        }
    }
}
