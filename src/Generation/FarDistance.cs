using System;
using System.IO;

namespace CraterEdge.Generation
{
    public sealed class FarDistance
    {
        public const int Band = FarField.StripOutside;
        private const int Padded = World.SizeX + 2 * Band;
        private const int FileVersion = 1;

        private readonly float[] ring = new float[2 * Band * Padded + 2 * World.SizeX * Band];

        public readonly float[] Coarse = new float[FarField.CoarseWidth * FarField.CoarseWidth];

        public static FarDistance Build(IslandModel model)
        {
            var d = new FarDistance();
            var padded = new bool[Padded * Padded];
            for (int x = 0; x < World.SizeX; x++)
            {
                Array.Copy(model.Island, x * IslandModel.Width, padded, (x + Band) * Padded + Band, IslandModel.Width);
            }
            float[] exact = DistanceField.Signed(padded, Padded, Padded);
            padded = null;
            for (int px = 0; px < Padded; px++)
            {
                for (int pz = 0; pz < Padded; pz++)
                {
                    int i = RingIndex(px, pz);
                    if (i >= 0)
                    {
                        d.ring[i] = exact[px * Padded + pz];
                    }
                }
            }
            exact = null;

            const int w = FarField.CoarseWidth, cell = FarField.CoarseCell;
            var island = new bool[w * w];
            for (int cx = 0; cx < w; cx++)
            {
                for (int cz = 0; cz < w; cz++)
                {
                    int x = -FarField.Extent + cx * cell + cell / 2, z = -FarField.Extent + cz * cell + cell / 2;
                    if (x >= World.MinX && x < World.MinX + World.SizeX && z >= World.MinZ && z < World.MinZ + World.SizeZ)
                    {
                        island[cx * w + cz] = model.Island[(x - World.MinX) * IslandModel.Width + z - World.MinZ];
                    }
                }
            }
            float[] coarse = DistanceField.Signed(island, w, w);
            for (int i = 0; i < coarse.Length; i++)
            {
                d.Coarse[i] = coarse[i] * cell;
            }
            return d;
        }

        private static int RingIndex(int px, int pz)
        {
            if (px < Band)
            {
                return px * Padded + pz;
            }
            if (px >= Band + World.SizeX)
            {
                return Band * Padded + (px - Band - World.SizeX) * Padded + pz;
            }
            int row = px - Band, offset = 2 * Band * Padded;
            if (pz < Band)
            {
                return offset + row * Band + pz;
            }
            if (pz >= Band + World.SizeZ)
            {
                return offset + World.SizeX * Band + row * Band + pz - Band - World.SizeZ;
            }
            return -1;
        }

        public double Exact(double x, double z)
        {
            int px = (int)Math.Floor(x) - World.MinX + Band, pz = (int)Math.Floor(z) - World.MinZ + Band;
            px = px < 0 ? 0 : px >= Padded ? Padded - 1 : px;
            pz = pz < 0 ? 0 : pz >= Padded ? Padded - 1 : pz;
            int i = RingIndex(px, pz);
            return i >= 0 ? ring[i] : double.NaN;
        }

        public double At(double x, double z)
        {
            double outside = Math.Max(Math.Max(World.MinX - x, x - (World.MinX + World.SizeX)),
                                      Math.Max(World.MinZ - z, z - (World.MinZ + World.SizeZ)));
            double coarse = CoarseAt(x, z);
            if (outside > Band - FarField.StripCell)
            {
                return coarse;
            }
            double blend = Noise.SmoothStep(Band - 32, Band - FarField.StripCell, outside);
            return Noise.Lerp(Exact(x, z), coarse, blend);
        }

        private double CoarseAt(double x, double z)
        {
            const int w = FarField.CoarseWidth;
            double gx = (x + FarField.Extent) / FarField.CoarseCell - 0.5, gz = (z + FarField.Extent) / FarField.CoarseCell - 0.5;
            int ix = (int)Math.Floor(gx), iz = (int)Math.Floor(gz);
            double fx = gx - ix, fz = gz - iz;
            double At(int a, int b)
            {
                a = a < 0 ? 0 : a >= w ? w - 1 : a;
                b = b < 0 ? 0 : b >= w ? w - 1 : b;
                return Coarse[a * w + b];
            }
            return Noise.Lerp(Noise.Lerp(At(ix, iz), At(ix + 1, iz), fx), Noise.Lerp(At(ix, iz + 1), At(ix + 1, iz + 1), fx), fz);
        }

        public byte[] Write()
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(FileVersion);
                w.Write(Field.Version);
                foreach (float v in ring) w.Write(v);
                foreach (float v in Coarse) w.Write(v);
                w.Flush();
                return stream.ToArray();
            }
        }

        public static FarDistance Read(byte[] data)
        {
            var d = new FarDistance();
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                if (r.ReadInt32() != FileVersion || r.ReadInt32() != Field.Version)
                {
                    throw new InvalidDataException("far distances from another version");
                }
                for (int i = 0; i < d.ring.Length; i++) d.ring[i] = r.ReadSingle();
                for (int i = 0; i < d.Coarse.Length; i++) d.Coarse[i] = r.ReadSingle();
            }
            return d;
        }
    }
}
