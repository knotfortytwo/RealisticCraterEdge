using System;
using System.IO;
using System.Threading.Tasks;

namespace CraterEdge.Generation
{
    public sealed class WideMap
    {
        public const int Size = FarField.CoarseWidth;
        public const double Half = FarField.Extent, Cell = 2.0 * Half / Size;
        private const int FileVersion = 1;
        private const double Bright = 255, ShadeMin = 0.45, Dim = 0.45, TopAt = -1100, DeepAt = -3000;
        private static readonly double[] Light = Normalized(-0.5, 0.75, 0.45);

        private readonly byte[] greys;

        private WideMap(byte[] greys)
        {
            this.greys = greys;
        }

        public byte[] Greys() => (byte[])greys.Clone();

        public static WideMap Build(Field field, FarField far, int threads)
        {
            var heights = new double[Size * Size];
            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads) };
            Parallel.For(0, Size, options, () => new Field.Column(), (row, state, column) =>
            {
                double z = Half - (row + 0.5) * Cell;
                for (int i = 0; i < Size; i++)
                {
                    double x = -Half + (i + 0.5) * Cell;
                    bool inBox = x >= World.MinX && x < World.MinX + World.SizeX && z >= World.MinZ && z < World.MinZ + World.SizeZ;
                    double h;
                    if (!inBox)
                    {
                        h = far.HeightAt(x, z, false);
                    }
                    else
                    {
                        int col = Column(x, z);
                        h = field.Model.Island[col] ? double.NaN : FarField.SurfaceHeight(field, column, field.Model.Base[col], x, z, false);
                    }
                    heights[row * Size + i] = h;
                }
                return column;
            }, _ => { });

            var result = new byte[Size * Size];
            for (int row = 0; row < Size; row++)
            {
                for (int i = 0; i < Size; i++)
                {
                    double h = heights[row * Size + i];
                    if (double.IsNaN(h))
                    {
                        continue;
                    }
                    double gx = (At(heights, i + 1, row, h) - At(heights, i - 1, row, h)) / (2 * Cell);
                    double gz = (At(heights, i, row - 1, h) - At(heights, i, row + 1, h)) / (2 * Cell);
                    double len = Math.Sqrt(gx * gx + 1 + gz * gz);
                    double light = (-gx * Light[0] + Light[1] - gz * Light[2]) / len;
                    double shade = ShadeMin + (1 - ShadeMin) * (light < 0 ? 0 : light);
                    double grey = Bright * shade * (1 - Dim * Noise.SmoothStep(TopAt, DeepAt, h));
                    result[row * Size + i] = (byte)(grey < 1 ? 1 : grey > 255 ? 255 : Math.Round(grey));
                }
            }
            return new WideMap(result);
        }

        private static double At(double[] heights, int i, int row, double own)
        {
            if (i < 0 || i >= Size || row < 0 || row >= Size)
            {
                return own;
            }
            double h = heights[row * Size + i];
            return double.IsNaN(h) ? own : h;
        }

        public byte[] Write()
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(FileVersion);
                w.Write(Field.Version);
                w.Write(Size);
                w.Write(greys);
                w.Flush();
                return stream.ToArray();
            }
        }

        public static WideMap Read(byte[] data)
        {
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                if (r.ReadInt32() != FileVersion || r.ReadInt32() != Field.Version || r.ReadInt32() != Size)
                {
                    throw new InvalidDataException("wide map from another version");
                }
                byte[] greys = r.ReadBytes(Size * Size);
                if (greys.Length != Size * Size)
                {
                    throw new InvalidDataException("truncated wide map");
                }
                return new WideMap(greys);
            }
        }

        private static double[] Normalized(double x, double y, double z)
        {
            double len = Math.Sqrt(x * x + y * y + z * z);
            return new[] { x / len, y / len, z / len };
        }

        private static int Column(double x, double z)
        {
            int cx = (int)Math.Floor(x) - World.MinX, cz = (int)Math.Floor(z) - World.MinZ;
            cx = cx < 0 ? 0 : cx >= World.SizeX ? World.SizeX - 1 : cx;
            cz = cz < 0 ? 0 : cz >= World.SizeZ ? World.SizeZ - 1 : cz;
            return cx * IslandModel.Width + cz;
        }
    }
}
