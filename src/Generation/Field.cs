using System;
using System.Collections.Generic;

namespace CraterEdge.Generation
{
    public sealed class Field
    {
        public const int Version = 5;

        public const double PlainDepth = -3000;
        public const double AnchorTop = -1120;
        public const double Reach = 4000;
        public const double NoiseFade = 100;
        public const double GullyAmplitude = 12, GullyWidth = 70, GullyLength = 420;
        public const double TextureAmplitude = 1.5, TextureSize = 18;
        public const double SpurVariation = 0.3, SpurSize = 500, BayVariation = 0.15, BaySize = 2200;
        public const byte RockType = 18;

        private static readonly double[] BenchAt = { -1380, -1700, -2050, -2400 };
        private const double BenchWidth = 70, BenchRiser = 26, BenchWarp = 45, BenchWarpSize = 900, BenchSize = 700,
            BenchFrom = -0.1, BenchFull = 0.4, BenchBelowAnchor = 120, BenchBelowAnchorFull = 220;

        private const double HillHeight = 90, HillLength = 2600, HillWidth = 1100, HillCos = 0.8191520443, HillSin = 0.5735764364;

        private const double CanyonTop = 150, CanyonTopFull = 350, CanyonFoot = 250;

        private const double HummockAmplitude = 8, HummockSize = 120, DetailAmplitude = 1.5, DetailSize = 10;
        private const double VentSurfaceTolerance = 4;
        private const double CaveWall = 3;

        private const uint SeedGully = 0x5a17c0deU, SeedTexture = 0x1b873593U, SeedSpur = 0xcc9e2d51U,
            SeedPlain = 0x85ebca6bU, SeedBay = 0xe6546b64U, SeedBench = 0x4cf5ad43U, SeedHills = 0x2545f491U,
            SeedHummocks = 0x7a646e4dU, SeedDetail = 0x3c6ef372U;

        private const int AnchorCell = 8, AnchorWidth = World.SizeX / AnchorCell;
        private readonly float[] anchor;
        private readonly IslandModel model;
        private readonly Caves caves;

        public sealed class Column
        {
            public double X, Z;
            internal double Anchor, ProfilePlain, Reach, Ground, Relief, CanyonProfile, CanyonIncision;
            internal readonly double[] BenchY = new double[BenchAt.Length], BenchWidth = new double[BenchAt.Length];
            internal readonly List<int> Caves = new List<int>();
        }

        public Field(IslandModel model)
        {
            this.model = model;
            anchor = new float[AnchorWidth * AnchorWidth];
            for (int cx = 0; cx < AnchorWidth; cx++)
            {
                for (int cz = 0; cz < AnchorWidth; cz++)
                {
                    int column = (cx * AnchorCell + AnchorCell / 2) * IslandModel.Width + cz * AnchorCell + AnchorCell / 2;
                    anchor[cx * AnchorWidth + cz] = (float)Math.Min(model.Cut[column], AnchorTop);
                }
            }
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlur(anchor, AnchorWidth, 8);
            }
            caves = Caves.Build(this, model);
        }

        public IslandModel Model => model;

        public Caves Caves => caves;

        public bool InCave(Column scratch, double x, double y, double z)
        {
            if (caves == null)
            {
                return false;
            }
            caves.Near(x, z, scratch.Caves);
            return scratch.Caves.Count > 0 && caves.Distance(x, y, z, scratch.Caves) < CaveWall;
        }

        public int CutAt(int column) => model.Cut[column];

        public double AnchorAt(double x, double z)
        {
            double gx = (x - World.MinX) / AnchorCell - 0.5, gz = (z - World.MinZ) / AnchorCell - 0.5;
            int ix = (int)Math.Floor(gx), iz = (int)Math.Floor(gz);
            double fx = gx - ix, fz = gz - iz;
            double a00 = AnchorGrid(ix, iz), a10 = AnchorGrid(ix + 1, iz), a01 = AnchorGrid(ix, iz + 1), a11 = AnchorGrid(ix + 1, iz + 1);
            return Noise.Lerp(Noise.Lerp(a00, a10, fx), Noise.Lerp(a01, a11, fx), fz);
        }

        private double AnchorGrid(int ix, int iz)
        {
            ix = ix < 0 ? 0 : ix >= AnchorWidth ? AnchorWidth - 1 : ix;
            iz = iz < 0 ? 0 : iz >= AnchorWidth ? AnchorWidth - 1 : iz;
            return anchor[ix * AnchorWidth + iz];
        }

        public static double PlainAt(double x, double z) =>
            PlainDepth + 8 * Noise.Gradient(x / 180, z / 180, SeedPlain) + 3 * Noise.Gradient(x / 40, z / 40, SeedPlain + 1);

        public static double FloorAt(double x, double z) => PlainAt(x, z) + Hills(x, z);

        private static double Hills(double x, double z)
        {
            double u = (x * HillCos + z * HillSin) / HillLength, v = (z * HillCos - x * HillSin) / HillWidth;
            double n = Noise.Gradient(u, v, SeedHills) + 0.35 * Noise.Gradient(u * 2.3, v * 2.3, SeedHills + 1);
            return HillHeight * Noise.SmoothStep(-0.05, 0.85, n);
        }

        public static double Hummocks(double x, double z) =>
            HummockAmplitude * (Noise.Gradient(x / HummockSize, z / HummockSize, SeedHummocks)
                                + 0.5 * Noise.Gradient(x * 2 / HummockSize, z * 2 / HummockSize, SeedHummocks + 1)) / 1.5;

        public static double Bumps(double x, double z) =>
            DetailAmplitude * (Noise.Gradient(x / DetailSize, z / DetailSize, SeedDetail)
                               + 0.5 * Noise.Gradient(x * 2.2 / DetailSize, z * 2.2 / DetailSize, SeedDetail + 1)) / 1.5;

        public static double FineDetail(double x, double z, double surface)
        {
            double detail = Bumps(x, z);
            if (Landforms.Vents(x, z, out double vents, out _, out double ground)
                && Math.Abs(surface - (ground - vents + Hummocks(x, z))) < VentSurfaceTolerance)
            {
                detail += vents;
            }
            return detail;
        }

        public static double ReachAt(double x, double z) =>
            Reach * (1 + SpurVariation * Noise.Gradient(x / SpurSize, z / SpurSize, SeedSpur)
                       + BayVariation * Noise.Gradient(x / BaySize, z / BaySize, SeedBay));

        public static double GullyNoise(double x, double y, double z) =>
            Noise.Fbm(x / GullyWidth, y / GullyLength, z / GullyWidth, 2, SeedGully);

        public void Prepare(Column c, double x, double z, bool fineDetail = true)
        {
            c.X = x;
            c.Z = z;
            c.Anchor = AnchorAt(x, z);
            c.ProfilePlain = PlainAt(x, z);
            c.Reach = ReachAt(x, z);
            double warp = BenchWarp * Noise.Gradient(x / BenchWarpSize, z / BenchWarpSize, SeedBench);
            for (int k = 0; k < BenchAt.Length; k++)
            {
                double y = BenchAt[k] + warp;
                c.BenchY[k] = y;
                c.BenchWidth[k] = BenchWidth
                    * Noise.SmoothStep(BenchFrom, BenchFull, Noise.Gradient(x / BenchSize, z / BenchSize, SeedBench + 1 + (uint)k))
                    * Noise.SmoothStep(BenchBelowAnchor, BenchBelowAnchorFull, c.Anchor - y);
            }
            c.Relief = Hummocks(x, z) + (fineDetail ? Bumps(x, z) : 0);
            double floor = c.ProfilePlain + Hills(x, z);
            double vents = fineDetail && Landforms.Vents(x, z, out double v, out _, out _) ? v : 0;
            c.Ground = Math.Max(floor, Math.Max(Landforms.MoundsAt(x, z), Landforms.SeamountsAt(x, z)))
                       + Landforms.BasinAt(x, z) + c.Relief + vents;
            c.CanyonProfile = Landforms.CanyonAt(x, z, out c.CanyonIncision);
            if (caves != null)
            {
                caves.Near(x, z, c.Caves);
            }
            else
            {
                c.Caves.Clear();
            }
        }

        public double Evaluate(Column c, double baseDistance, double y)
        {
            double f = EvaluateSurface(c, baseDistance, y);
            return c.Caves.Count > 0 ? Math.Min(f, caves.Distance(c.X, y, c.Z, c.Caves)) : f;
        }

        public double EvaluateSurface(Column c, double baseDistance, double y)
        {
            double x = c.X, z = c.Z, a = c.Anchor;
            double span = a - c.ProfilePlain;
            double t = (a - y) / span;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            double st = Math.Sqrt(t);
            double offset = c.Reach * t * t * st;
            double slope = c.Reach * 2.5 * t * st / span;
            double profileSlope = slope;
            for (int k = 0; k < BenchAt.Length; k++)
            {
                double w = c.BenchWidth[k];
                double u = (c.BenchY[k] + BenchRiser - y) / (2 * BenchRiser);
                if (w <= 0 || u <= 0)
                {
                    continue;
                }
                if (u >= 1)
                {
                    offset += w;
                    continue;
                }
                offset += w * u * u * (3 - 2 * u);
                slope += w * 6 * u * (1 - u) / (2 * BenchRiser);
            }
            double fade = Noise.SmoothStep(0, NoiseFade, a - y);
            double noise = 0;
            if (fade > 0)
            {
                noise = fade * (GullyAmplitude * GullyNoise(x, y, z)
                              + TextureAmplitude * Noise.Fbm(x / TextureSize, y / TextureSize, z / TextureSize, 2, SeedTexture));
            }
            double canyon = 0;
            if (c.CanyonProfile > 0)
            {
                canyon = c.CanyonProfile * Noise.SmoothStep(CanyonTop, CanyonTopFull, a - y)
                         * Noise.SmoothStep(0, CanyonFoot, y - c.ProfilePlain)
                         * (Landforms.CanyonNotch + profileSlope * c.CanyonIncision);
            }
            double flank = (baseDistance + offset + noise + slope * c.Relief - canyon) / Math.Sqrt(1 + slope * slope);
            return Math.Max(flank, c.Ground - y);
        }

        public double Evaluate(Column scratch, double baseDistance, double x, double y, double z, bool fineDetail = true)
        {
            Prepare(scratch, x, z, fineDetail);
            return Evaluate(scratch, baseDistance, y);
        }

        public const double Lipschitz = 3.5;

        private static void BoxBlur(float[] a, int w, int r)
        {
            var tmp = new float[a.Length];
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < w; z++)
                {
                    double s = 0;
                    int c = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int zz = z + k;
                        if (zz < 0 || zz >= w) continue;
                        s += a[x * w + zz];
                        c++;
                    }
                    tmp[x * w + z] = (float)(s / c);
                }
            }
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < w; z++)
                {
                    double s = 0;
                    int c = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int xx = x + k;
                        if (xx < 0 || xx >= w) continue;
                        s += tmp[xx * w + z];
                        c++;
                    }
                    a[x * w + z] = (float)(s / c);
                }
            }
        }
    }
}
