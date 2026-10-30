using System;
using System.IO;
using System.Threading.Tasks;

namespace CraterEdge.Generation
{
    public sealed class FarField
    {
        public const int Extent = 8192;
        public const int CoarseCell = 8, CoarseWidth = 2 * Extent / CoarseCell;
        public const int StripCell = 2, StripInside = 16, StripOutside = 128;
        public const int StripDepth = (StripInside + StripOutside) / StripCell;
        public const int StripLength = (World.SizeX + 2 * StripOutside) / StripCell;
        private const int FileVersion = 1;

        public readonly float[] Coarse = new float[CoarseWidth * CoarseWidth];
        public readonly float[][] Strips = { new float[StripLength * StripDepth], new float[StripLength * StripDepth],
                                             new float[StripLength * StripDepth], new float[StripLength * StripDepth] };

        public static double SurfaceHeight(Field field, Field.Column column, double baseDistance, double x, double z, bool fineDetail)
        {
            field.Prepare(column, x, z, fineDetail);
            double low = Field.PlainDepth - 80, high = column.Anchor + 1;
            for (int i = 0; i < 24; i++)
            {
                double mid = 0.5 * (low + high);
                if (field.Evaluate(column, baseDistance, mid) >= 0)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }
            return 0.5 * (low + high);
        }

        public static FarField Build(Field field, IslandModel model, FarDistance distance, int threads)
        {
            var far = new FarField();
            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads) };

            for (int side = 0; side < 4; side++)
            {
                var strip = far.Strips[side];
                int s = side;
                Parallel.For(0, StripLength, options, () => new Field.Column(), (along, state, column) =>
                {
                    for (int across = 0; across < StripDepth; across++)
                    {
                        StripPoint(s, along, across, out double x, out double z);
                        int cx = (int)Math.Floor(x) - World.MinX, cz = (int)Math.Floor(z) - World.MinZ;
                        double baseDistance = cx >= 0 && cz >= 0 && cx < World.SizeX && cz < World.SizeZ
                            ? model.Base[cx * IslandModel.Width + cz]
                            : distance.Exact(x, z);
                        strip[along * StripDepth + across] = (float)SurfaceHeight(field, column, baseDistance, x, z, true);
                    }
                    return column;
                }, _ => { });
            }

            Parallel.For(0, CoarseWidth, options, () => new Field.Column(), (cx, state, column) =>
            {
                for (int cz = 0; cz < CoarseWidth; cz++)
                {
                    double x = -Extent + cx * CoarseCell + CoarseCell / 2.0, z = -Extent + cz * CoarseCell + CoarseCell / 2.0;
                    bool inBox = x > World.MinX && x < World.MinX + World.SizeX && z > World.MinZ && z < World.MinZ + World.SizeZ;
                    far.Coarse[cx * CoarseWidth + cz] = inBox
                        ? float.NaN
                        : (float)SurfaceHeight(field, column, distance.Coarse[cx * CoarseWidth + cz], x, z, false);
                }
                return column;
            }, _ => { });
            return far;
        }

        public static void StripPoint(int side, int along, int across, out double x, out double z)
        {
            double a = World.MinX - StripOutside + (along + 0.5) * StripCell;
            double inward = StripInside - (across + 0.5) * StripCell;
            switch (side)
            {
                case 0: x = World.MinX + inward; z = a; break;
                case 1: x = World.MinX + World.SizeX - inward; z = a; break;
                case 2: x = a; z = World.MinZ + inward; break;
                default: x = a; z = World.MinZ + World.SizeZ - inward; break;
            }
        }

        public double HeightAt(double x, double z, bool fineDetail = true)
        {
            double coarse = CoarseAt(x, z);
            coarse += fineDetail && !double.IsNaN(coarse) ? Field.FineDetail(x, z, coarse) : 0;
            double outside = Math.Max(Math.Max(World.MinX - x, x - (World.MinX + World.SizeX)),
                                      Math.Max(World.MinZ - z, z - (World.MinZ + World.SizeZ)));
            if (outside > StripOutside - StripCell)
            {
                return coarse;
            }
            double strip = StripAt(x, z);
            double blend = Noise.SmoothStep(StripOutside - 32, StripOutside - StripCell, outside);
            return double.IsNaN(coarse) ? strip : Noise.Lerp(strip, coarse, blend);
        }

        private double CoarseAt(double x, double z)
        {
            double gx = (x + Extent) / CoarseCell - 0.5, gz = (z + Extent) / CoarseCell - 0.5;
            int ix = (int)Math.Floor(gx), iz = (int)Math.Floor(gz);
            double fx = gx - ix, fz = gz - iz;
            double r0 = CatmullRow(ix, iz - 1, fx), r1 = CatmullRow(ix, iz, fx), r2 = CatmullRow(ix, iz + 1, fx), r3 = CatmullRow(ix, iz + 2, fx);
            double bicubic = CatmullRom(r0, r1, r2, r3, fz);
            if (!double.IsNaN(bicubic))
            {
                return bicubic;
            }
            double h00 = CoarseCellAt(ix, iz), h10 = CoarseCellAt(ix + 1, iz), h01 = CoarseCellAt(ix, iz + 1), h11 = CoarseCellAt(ix + 1, iz + 1);
            double sum = 0, weight = 0;
            Accumulate(h00, (1 - fx) * (1 - fz), ref sum, ref weight);
            Accumulate(h10, fx * (1 - fz), ref sum, ref weight);
            Accumulate(h01, (1 - fx) * fz, ref sum, ref weight);
            Accumulate(h11, fx * fz, ref sum, ref weight);
            return weight > 0 ? sum / weight : double.NaN;
        }

        private double CatmullRow(int ix, int iz, double f) =>
            CatmullRom(CoarseCellAt(ix - 1, iz), CoarseCellAt(ix, iz), CoarseCellAt(ix + 1, iz), CoarseCellAt(ix + 2, iz), f);

        private static double CatmullRom(double p0, double p1, double p2, double p3, double t) =>
            0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t);

        private static void Accumulate(double h, double w, ref double sum, ref double weight)
        {
            if (!double.IsNaN(h) && w > 0)
            {
                sum += h * w;
                weight += w;
            }
        }

        private double CoarseCellAt(int ix, int iz)
        {
            ix = ix < 0 ? 0 : ix >= CoarseWidth ? CoarseWidth - 1 : ix;
            iz = iz < 0 ? 0 : iz >= CoarseWidth ? CoarseWidth - 1 : iz;
            return Coarse[ix * CoarseWidth + iz];
        }

        private double StripAt(double x, double z)
        {
            double dx0 = x - World.MinX, dx1 = World.MinX + World.SizeX - x, dz0 = z - World.MinZ, dz1 = World.MinZ + World.SizeZ - z;
            int side = 0;
            double best = dx0;
            if (dx1 < best) { best = dx1; side = 1; }
            if (dz0 < best) { best = dz0; side = 2; }
            if (dz1 < best) { side = 3; }
            double along = (side < 2 ? z : x) - (World.MinX - StripOutside);
            double inward = side switch { 0 => dx0, 1 => dx1, 2 => dz0, _ => dz1 };
            double ga = along / StripCell - 0.5, gc = (StripInside - inward) / StripCell - 0.5;
            int ia = (int)Math.Floor(ga), ic = (int)Math.Floor(gc);
            double fa = ga - ia, fc = gc - ic;
            var s = Strips[side];
            double At(int a, int c)
            {
                a = a < 0 ? 0 : a >= StripLength ? StripLength - 1 : a;
                c = c < 0 ? 0 : c >= StripDepth ? StripDepth - 1 : c;
                return s[a * StripDepth + c];
            }
            return Noise.Lerp(Noise.Lerp(At(ia, ic), At(ia + 1, ic), fa), Noise.Lerp(At(ia, ic + 1), At(ia + 1, ic + 1), fa), fc);
        }

        public byte[] Write()
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(FileVersion);
                w.Write(Field.Version);
                foreach (float h in Coarse) w.Write(h);
                foreach (var strip in Strips)
                {
                    foreach (float h in strip) w.Write(h);
                }
                w.Flush();
                return stream.ToArray();
            }
        }

        public static FarField Read(byte[] data)
        {
            var far = new FarField();
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                if (r.ReadInt32() != FileVersion || r.ReadInt32() != Field.Version)
                {
                    throw new InvalidDataException("far field from another version");
                }
                for (int i = 0; i < far.Coarse.Length; i++) far.Coarse[i] = r.ReadSingle();
                foreach (var strip in far.Strips)
                {
                    for (int i = 0; i < strip.Length; i++) strip[i] = r.ReadSingle();
                }
            }
            return far;
        }
    }
}
