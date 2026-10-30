using System;
using System.Collections.Generic;

namespace CraterEdge.Generation
{
    public static class Deposits
    {
        public static readonly string[] Resources =
        {
            "Copper", "Silver", "Gold", "Lead", "Titanium", "Quartz", "Salt", "Lithium", "Magnetite", "Nickel", "Sulphur",
            "Kyanite", "KyaniteLarge", "Diamond", "Ruby", "Uraninite",
        };
        private const int Copper = 0, Silver = 1, Gold = 2, Lead = 3, Titanium = 4, Quartz = 5, Salt = 6, Lithium = 7, Magnetite = 8,
            Nickel = 9, Sulphur = 10, Kyanite = 11, KyaniteLarge = 12, Diamond = 13, Ruby = 14, Uraninite = 15;

        private static readonly int[,] Upper = { { Copper, 25 }, { Lead, 20 }, { Titanium, 20 }, { Silver, 15 }, { Quartz, 10 }, { Salt, 10 } };
        private static readonly int[,] Middle =
        {
            { Titanium, 16 }, { Quartz, 14 }, { Copper, 12 }, { Silver, 12 }, { Gold, 10 }, { Lithium, 12 }, { Magnetite, 12 }, { Lead, 6 },
            { Nickel, 6 },
        };
        private static readonly int[,] Deep =
        {
            { Titanium, 20 }, { Quartz, 18 }, { Nickel, 11 }, { Magnetite, 9 }, { Kyanite, 10 }, { Uraninite, 7 }, { Diamond, 8 }, { Ruby, 8 },
            { Lithium, 5 }, { Gold, 4 },
        };
        private static readonly int[,] VentOres = { { Copper, 24 }, { Silver, 15 }, { Gold, 16 }, { Sulphur, 18 }, { Lead, 12 }, { Quartz, 15 } };
        private static readonly int[,] CrustOres = { { Nickel, 32 }, { Magnetite, 28 }, { Titanium, 20 }, { Lithium, 12 }, { Copper, 8 } };
        private static readonly int[,] TubeOres =
        {
            { Kyanite, 26 }, { KyaniteLarge, 10 }, { Diamond, 20 }, { Ruby, 18 }, { Uraninite, 10 }, { Quartz, 16 },
        };
        private const double UpperBottom = -1600, MiddleBottom = -2300, BandWarp = 80, BandWarpSize = 500;

        private const double SliceStep = 24, SliceCell = 24, SliceTop = -1150, BoxMargin = 40, GroundCell = 40,
            WallSampleArea = 750, GroundSampleArea = GroundCell * GroundCell;
        private const double Steep = 0.65, MemberSteep = 0.45, Overhang = -0.3;
        private const double CutClearance = 30;
        private const double DepthFrom = -1150, DepthFull = -3000, SeedsTop = 1.2, SeedsDeep = 5, SizeTop = 1.5, SizeDeep = 5.5;
        private const double ProvinceSize = 1200, ProvinceFrom = -0.05, ProvinceFull = 0.3, Barren = 0.08, Rich = 2.4;
        private const double VentReach = 90, VentBoost = 4, TubeBoost = 15;
        private const double ClusterBase = 10, ClusterGrow = 4, TubeSpread = 0.5, Spacing = 7, MixedOre = 0.3;
        private const int MaxClusterSize = 8, MemberTries = 6, Projections = 4;
        private const int CraterDepositsMin = 2, CraterDepositsMax = 3;

        private const uint SeedPick = 0x4f1bbcdcU, SeedProvince = 0x2f8f6e8aU, SeedSize = 0x0b4b2b7bU, SeedBand = 0x7a3c5e91U,
            SeedSlice = 0x3a1e4f5dU, SeedGround = 0x6d2b79f5U, SeedCrater = 0x1c69b3f7U, SeedMember = 0x5bd1e995U;

        public readonly struct Deposit
        {
            public readonly double X, Y, Z, NX, NY, NZ;
            public readonly int Resource;

            public Deposit(double x, double y, double z, double nx, double ny, double nz, int resource)
            {
                X = x; Y = y; Z = z; NX = nx; NY = ny; NZ = nz; Resource = resource;
            }
        }

        public static List<Deposit> Place(Field field, FarDistance distance, List<Landforms.Site> exposedCraters)
        {
            var placer = new Placer(field, distance);
            Walls(field, exposedCraters, placer);
            Ground(field, distance, exposedCraters, placer);
            CraterFloors(field, distance, exposedCraters, placer);
            return placer.Result;
        }

        private static void Walls(Field field, List<Landforms.Site> craters, Placer placer)
        {
            int n = (int)((World.SizeX - 2 * BoxMargin) / SliceCell) + 1;
            double x0 = World.MinX + BoxMargin, z0 = World.MinZ + BoxMargin;
            var columns = new Field.Column[n * n];
            var bases = new double[n * n];
            var cuts = new double[n * n];
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    double x = x0 + i * SliceCell, z = z0 + k * SliceCell;
                    var c = new Field.Column();
                    field.Prepare(c, x, z);
                    columns[i * n + k] = c;
                    int col = Column(x, z);
                    bases[i * n + k] = field.Model.Base[col];
                    cuts[i * n + k] = field.Model.Cut[col];
                }
            }
            var values = new double[n * n];
            var scratch = new Field.Column();
            int slices = (int)((SliceTop - (World.MinY + 60)) / SliceStep);
            for (int s = 0; s <= slices; s++)
            {
                double y = SliceTop - s * SliceStep;
                for (int i = 0; i < n * n; i++)
                {
                    values[i] = y >= cuts[i] - CutClearance ? double.NaN : field.Evaluate(columns[i], bases[i], y);
                }
                for (int i = 0; i + 1 < n; i++)
                {
                    for (int k = 0; k + 1 < n; k++)
                    {
                        double a = values[i * n + k], bx = values[(i + 1) * n + k], bz = values[i * n + k + 1];
                        if (double.IsNaN(a) || double.IsNaN(bx) || double.IsNaN(bz))
                        {
                            continue;
                        }
                        double px, pz;
                        if ((a >= 0) != (bx >= 0))
                        {
                            px = x0 + (i + a / (a - bx)) * SliceCell;
                            pz = z0 + k * SliceCell;
                        }
                        else if ((a >= 0) != (bz >= 0))
                        {
                            px = x0 + i * SliceCell;
                            pz = z0 + (k + a / (a - bz)) * SliceCell;
                        }
                        else
                        {
                            continue;
                        }
                        uint h = Noise.Hash(i, k, SeedSlice + (uint)s * 0x9e3779b9U);
                        bool tube = field.InCave(scratch, px, y, pz);
                        if (Landforms.Unit(h) >= SeedRate(px, y, pz, craters, tube) * WallSampleArea)
                        {
                            continue;
                        }
                        Normal(field, scratch, px, y, pz, out double nx, out double ny, out double nz);
                        if (Math.Abs(ny) >= Steep && !tube || ny < Overhang)
                        {
                            continue;
                        }
                        placer.Cluster(px, y, pz, nx, ny, nz, true, tube, tube ? TubeOres : Table(px, y, pz, craters), h);
                    }
                }
            }
        }

        private static void Ground(Field field, FarDistance distance, List<Landforms.Site> craters, Placer placer)
        {
            var column = new Field.Column();
            int n = (int)(2 * (FarField.Extent - 200) / GroundCell);
            double origin = -(FarField.Extent - 200);
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    uint h = Noise.Hash(i, k, SeedGround);
                    double x = origin + (i + Landforms.Unit(Noise.Hash(h + 1))) * GroundCell;
                    double z = origin + (k + Landforms.Unit(Noise.Hash(h + 2))) * GroundCell;
                    if (!Surface(field, distance, column, x, z, out double y))
                    {
                        continue;
                    }
                    if (Landforms.Unit(Noise.Hash(h + 3)) >= SeedRate(x, y, z, craters, false) * GroundSampleArea)
                    {
                        continue;
                    }
                    if (!GroundNormal(field, distance, column, x, z, out double nx, out double ny, out double nz) || ny < Steep)
                    {
                        continue;
                    }
                    placer.Cluster(x, y, z, nx, ny, nz, false, false, Table(x, y, z, craters), h);
                }
            }
        }

        private static void CraterFloors(Field field, FarDistance distance, List<Landforms.Site> craters, Placer placer)
        {
            var column = new Field.Column();
            foreach (var crater in craters)
            {
                uint h = Noise.Hash(crater.Hash ^ SeedCrater);
                int count = CraterDepositsMin + (int)(h % (uint)(CraterDepositsMax - CraterDepositsMin + 1));
                for (int i = 0; i < count; i++)
                {
                    uint g = Noise.Hash(h + 1 + (uint)i);
                    double dx = Landforms.Unit(Noise.Hash(g + 1)) - 0.5, dz = Landforms.Unit(Noise.Hash(g + 2)) - 0.5;
                    double len = Math.Sqrt(dx * dx + dz * dz);
                    if (len < 0.05)
                    {
                        continue;
                    }
                    double r = crater.Radius * (0.1 + 0.35 * Landforms.Unit(Noise.Hash(g + 3)));
                    double x = crater.X + dx / len * r, z = crater.Z + dz / len * r;
                    if (Surface(field, distance, column, x, z, out double y))
                    {
                        placer.Add(x, y, z, 0, 1, 0, Pick(VentOres, g));
                    }
                }
            }
        }

        private sealed class Placer
        {
            public readonly List<Deposit> Result = new List<Deposit>();
            private readonly Field field;
            private readonly FarDistance distance;
            private readonly Field.Column scratch = new Field.Column(), column = new Field.Column();
            private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();

            public Placer(Field field, FarDistance distance)
            {
                this.field = field;
                this.distance = distance;
            }

            public void Cluster(double x, double y, double z, double nx, double ny, double nz, bool wall, bool tube, int[,] table, uint h)
            {
                int ore = Pick(table, h);
                if (!Add(x, y, z, nx, ny, nz, ore))
                {
                    return;
                }
                int size = (int)(Noise.Lerp(SizeTop, SizeDeep, Depth(y)) * (0.6 + 0.8 * Landforms.Unit(Noise.Hash(h ^ SeedSize))) + 0.5);
                size = size > MaxClusterSize ? MaxClusterSize : size;
                double radius = (ClusterBase + ClusterGrow * size) * (tube ? TubeSpread : 1);
                double ax = 1, ay = 0, az = 0, bx = 0, by = 0, bz = 1;
                if (wall)
                {
                    double len = Math.Sqrt(nx * nx + nz * nz);
                    if (len > 1e-6)
                    {
                        ax = -nz / len;
                        az = nx / len;
                    }
                    bx = ny * az - nz * ay;
                    by = nz * ax - nx * az;
                    bz = nx * ay - ny * ax;
                }
                for (int m = 1; m < size; m++)
                {
                    for (int attempt = 0; attempt < MemberTries; attempt++)
                    {
                        uint g = Noise.Hash(m, attempt, h ^ SeedMember);
                        double u = 2 * Landforms.Unit(Noise.Hash(g + 1)) - 1, v = 2 * Landforms.Unit(Noise.Hash(g + 2)) - 1;
                        double rr = u * u + v * v;
                        if (rr > 1 || rr < 0.06)
                        {
                            continue;
                        }
                        double px = x + radius * (u * ax + v * bx), py = y + radius * (u * ay + v * by), pz = z + radius * (u * az + v * bz);
                        double mx, my, mz;
                        if (wall ? !OnWall(ref px, ref py, ref pz, out mx, out my, out mz) : !OnGround(px, pz, out py, out mx, out my, out mz))
                        {
                            continue;
                        }
                        double dx = px - x, dy = py - y, dz = pz - z;
                        if (dx * dx + dy * dy + dz * dz > 2.25 * radius * radius || tube && !field.InCave(scratch, px, py, pz))
                        {
                            continue;
                        }
                        if (Add(px, py, pz, mx, my, mz, Landforms.Unit(Noise.Hash(g + 3)) < MixedOre ? Pick(table, g) : ore))
                        {
                            break;
                        }
                    }
                }
            }

            public bool Add(double x, double y, double z, double nx, double ny, double nz, int resource)
            {
                long cx = (long)Math.Floor(x / Spacing), cy = (long)Math.Floor(y / Spacing), cz = (long)Math.Floor(z / Spacing);
                for (long i = cx - 1; i <= cx + 1; i++)
                {
                    for (long j = cy - 1; j <= cy + 1; j++)
                    {
                        for (long k = cz - 1; k <= cz + 1; k++)
                        {
                            if (!cells.TryGetValue(Key(i, j, k), out List<int> near))
                            {
                                continue;
                            }
                            foreach (int index in near)
                            {
                                var d = Result[index];
                                double dx = d.X - x, dy = d.Y - y, dz = d.Z - z;
                                if (dx * dx + dy * dy + dz * dz < Spacing * Spacing)
                                {
                                    return false;
                                }
                            }
                        }
                    }
                }
                long key = Key(cx, cy, cz);
                if (!cells.TryGetValue(key, out List<int> own))
                {
                    own = new List<int>();
                    cells[key] = own;
                }
                own.Add(Result.Count);
                Result.Add(new Deposit(x, y, z, nx, ny, nz, resource));
                return true;
            }

            private static long Key(long x, long y, long z) => (x + 0x100000) << 42 | (y + 0x100000) << 21 | (z + 0x100000);

            private bool OnWall(ref double x, ref double y, ref double z, out double nx, out double ny, out double nz)
            {
                nx = 0; ny = 1; nz = 0;
                for (int i = 0; i < Projections; i++)
                {
                    double f = Value(field, scratch, x, y, z);
                    double gx = (Value(field, scratch, x + 1, y, z) - Value(field, scratch, x - 1, y, z)) / 2;
                    double gy = (Value(field, scratch, x, y + 1, z) - Value(field, scratch, x, y - 1, z)) / 2;
                    double gz = (Value(field, scratch, x, y, z + 1) - Value(field, scratch, x, y, z - 1)) / 2;
                    double g2 = gx * gx + gy * gy + gz * gz;
                    if (g2 < 1e-6)
                    {
                        return false;
                    }
                    x -= f * gx / g2;
                    y -= f * gy / g2;
                    z -= f * gz / g2;
                }
                bool inBox = x > World.MinX && x < World.MinX + World.SizeX && z > World.MinZ && z < World.MinZ + World.SizeZ;
                if (!inBox || Math.Abs(Value(field, scratch, x, y, z)) > 0.5 || y < World.MinY + 60)
                {
                    return false;
                }
                if (y >= field.Model.Cut[Column(x, z)] - CutClearance)
                {
                    return false;
                }
                Normal(field, scratch, x, y, z, out nx, out ny, out nz);
                return ny >= Overhang;
            }

            private bool OnGround(double x, double z, out double y, out double nx, out double ny, out double nz)
            {
                nx = 0; ny = 1; nz = 0;
                return Surface(field, distance, column, x, z, out y) && GroundNormal(field, distance, column, x, z, out nx, out ny, out nz)
                    && ny >= MemberSteep;
            }
        }

        private static bool Surface(Field field, FarDistance distance, Field.Column column, double x, double z, out double y)
        {
            bool inBox = x > World.MinX && x < World.MinX + World.SizeX && z > World.MinZ && z < World.MinZ + World.SizeZ;
            if (!inBox)
            {
                y = FarField.SurfaceHeight(field, column, distance.At(x, z), x, z, true);
                return true;
            }
            int col = Column(x, z);
            if (field.Model.Island[col])
            {
                y = 0;
                return false;
            }
            y = FarField.SurfaceHeight(field, column, field.Model.Base[col], x, z, true);
            return y < field.Model.Cut[col] - CutClearance;
        }

        private static bool GroundNormal(Field field, FarDistance distance, Field.Column column, double x, double z,
            out double nx, out double ny, out double nz)
        {
            nx = 0; ny = 1; nz = 0;
            if (!Surface(field, distance, column, x + 2, z, out double east) || !Surface(field, distance, column, x - 2, z, out double west)
                || !Surface(field, distance, column, x, z + 2, out double north) || !Surface(field, distance, column, x, z - 2, out double south))
            {
                return false;
            }
            double gx = (east - west) / 4, gz = (north - south) / 4, len = Math.Sqrt(gx * gx + 1 + gz * gz);
            nx = -gx / len;
            ny = 1 / len;
            nz = -gz / len;
            return true;
        }

        private static double SeedRate(double x, double y, double z, List<Landforms.Site> craters, bool tube)
        {
            double province = Noise.Lerp(Barren, Rich, Noise.SmoothStep(ProvinceFrom, ProvinceFull,
                Noise.Fbm(x / ProvinceSize, y / ProvinceSize, z / ProvinceSize, 2, SeedProvince)));
            if (tube)
            {
                province = Math.Max(province, 1) * TubeBoost;
            }
            else if (NearVents(x, z, craters))
            {
                province = Math.Max(province, 1) * VentBoost;
            }
            return Noise.Lerp(SeedsTop, SeedsDeep, Depth(y)) * province * 1e-6;
        }

        private static double Depth(double y)
        {
            double t = (DepthFrom - y) / (DepthFrom - DepthFull);
            return t < 0 ? 0 : t > 1 ? 1 : t;
        }

        private static bool NearVents(double x, double z, List<Landforms.Site> craters)
        {
            foreach (var c in craters)
            {
                double dx = x - c.X, dz = z - c.Z, reach = c.Radius + VentReach;
                if (dx * dx + dz * dz < reach * reach)
                {
                    return true;
                }
            }
            return false;
        }

        private static int[,] Table(double x, double y, double z, List<Landforms.Site> craters)
        {
            if (NearVents(x, z, craters))
            {
                return VentOres;
            }
            double seamount = Landforms.SeamountsAt(x, z);
            if (seamount > Field.FloorAt(x, z) && seamount > Landforms.MoundsAt(x, z) && Math.Abs(y - seamount) < 15)
            {
                return CrustOres;
            }
            double band = y + BandWarp * Noise.Gradient(x / BandWarpSize, z / BandWarpSize, SeedBand);
            return band > UpperBottom ? Upper : band > MiddleBottom ? Middle : Deep;
        }

        private static int Pick(int[,] table, uint h)
        {
            int total = 0;
            for (int i = 0; i < table.GetLength(0); i++)
            {
                total += table[i, 1];
            }
            int roll = (int)(Noise.Hash(h ^ SeedPick) % (uint)total);
            for (int i = 0; i < table.GetLength(0); i++)
            {
                roll -= table[i, 1];
                if (roll < 0)
                {
                    return table[i, 0];
                }
            }
            return table[0, 0];
        }

        private static void Normal(Field field, Field.Column scratch, double x, double y, double z, out double nx, out double ny, out double nz)
        {
            double gx = Value(field, scratch, x + 1, y, z) - Value(field, scratch, x - 1, y, z);
            double gy = Value(field, scratch, x, y + 1, z) - Value(field, scratch, x, y - 1, z);
            double gz = Value(field, scratch, x, y, z + 1) - Value(field, scratch, x, y, z - 1);
            double len = Math.Sqrt(gx * gx + gy * gy + gz * gz);
            if (len < 1e-9)
            {
                nx = 0; ny = 1; nz = 0;
                return;
            }
            nx = -gx / len; ny = -gy / len; nz = -gz / len;
        }

        private static double Value(Field field, Field.Column scratch, double x, double y, double z) =>
            field.Evaluate(scratch, field.Model.Base[Column(x, z)], x, y, z);

        private static int Column(double x, double z)
        {
            int cx = (int)Math.Floor(x) - World.MinX, cz = (int)Math.Floor(z) - World.MinZ;
            cx = cx < 0 ? 0 : cx >= World.SizeX ? World.SizeX - 1 : cx;
            cz = cz < 0 ? 0 : cz >= World.SizeZ ? World.SizeZ - 1 : cz;
            return cx * IslandModel.Width + cz;
        }
    }
}
