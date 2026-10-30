using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CraterEdge.Generation
{
    public sealed class MapOverlay
    {
        public const int Size = 2048;
        public const double Half = 2000;
        private const int FileVersion = 1;
        private static readonly double[] TopColour = { 41, 85, 213 }, DeepColour = { 12, 24, 78 };
        private const double TopAt = -1100, DeepAt = -2100, ShadeMin = 0.4;
        private static readonly double[] Light = Normalized(-0.5, 0.75, 0.45);

        private readonly byte[] data;

        private MapOverlay(byte[] data)
        {
            this.data = data;
        }

        public byte[] Write() => data;

        public static MapOverlay Build(Field field, int threads)
        {
            var heights = new double[Size * Size];
            const double cell = 2 * Half / Size;
            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads) };
            Parallel.For(0, Size, options, () => new Field.Column(), (row, state, column) =>
            {
                double z = Half - (row + 0.5) * cell;
                for (int i = 0; i < Size; i++)
                {
                    double x = -Half + (i + 0.5) * cell;
                    int col = Column(x, z);
                    double h = double.NaN;
                    if (!field.Model.Island[col])
                    {
                        h = FarField.SurfaceHeight(field, column, field.Model.Base[col], x, z, true);
                        h = h < field.Model.Cut[col] ? h : double.NaN;
                    }
                    heights[row * Size + i] = h;
                }
                return column;
            }, _ => { });

            var rgb = new byte[Size * Size * 3];
            var opaque = new bool[Size * Size];
            for (int row = 0; row < Size; row++)
            {
                for (int i = 0; i < Size; i++)
                {
                    double h = heights[row * Size + i];
                    if (double.IsNaN(h))
                    {
                        continue;
                    }
                    double gx = (At(heights, i + 1, row, h) - At(heights, i - 1, row, h)) / (2 * cell);
                    double gz = (At(heights, i, row - 1, h) - At(heights, i, row + 1, h)) / (2 * cell);
                    double len = Math.Sqrt(gx * gx + 1 + gz * gz);
                    double light = (-gx * Light[0] + Light[1] - gz * Light[2]) / len;
                    double shade = ShadeMin + (1 - ShadeMin) * (light < 0 ? 0 : light);
                    double t = Noise.SmoothStep(TopAt, DeepAt, h);
                    for (int c = 0; c < 3; c++)
                    {
                        double v = Noise.Lerp(TopColour[c], DeepColour[c], t) * shade;
                        rgb[(row * Size + i) * 3 + c] = (byte)(v < 0 ? 0 : v > 255 ? 255 : Math.Round(v));
                    }
                    opaque[row * Size + i] = true;
                }
            }
            return new MapOverlay(Encode(rgb, opaque));
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

        private static byte[] Encode(byte[] rgb, bool[] opaque)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(FileVersion);
                w.Write(Field.Version);
                w.Write(Size);
                var runs = new List<(int start, int length)>();
                for (int row = 0; row < Size; row++)
                {
                    runs.Clear();
                    for (int i = 0; i < Size;)
                    {
                        if (!opaque[row * Size + i])
                        {
                            i++;
                            continue;
                        }
                        int start = i;
                        while (i < Size && opaque[row * Size + i])
                        {
                            i++;
                        }
                        runs.Add((start, i - start));
                    }
                    w.Write((ushort)runs.Count);
                    foreach (var (start, length) in runs)
                    {
                        w.Write((ushort)start);
                        w.Write((ushort)length);
                        w.Write(rgb, (row * Size + start) * 3, length * 3);
                    }
                }
                w.Flush();
                return stream.ToArray();
            }
        }

        public static MapOverlay Read(byte[] data)
        {
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                if (r.ReadInt32() != FileVersion || r.ReadInt32() != Field.Version || r.ReadInt32() != Size)
                {
                    throw new InvalidDataException("map overlay from another version");
                }
            }
            var overlay = new MapOverlay(data);
            overlay.Rgba();
            return overlay;
        }

        public byte[] Rgba()
        {
            var rgba = new byte[Size * Size * 4];
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                r.BaseStream.Position = 12;
                for (int row = 0; row < Size; row++)
                {
                    int runs = r.ReadUInt16();
                    for (int k = 0; k < runs; k++)
                    {
                        int start = r.ReadUInt16(), length = r.ReadUInt16();
                        if (start + length > Size)
                        {
                            throw new InvalidDataException("bad map overlay run");
                        }
                        byte[] colour = r.ReadBytes(length * 3);
                        if (colour.Length != length * 3)
                        {
                            throw new InvalidDataException("truncated map overlay");
                        }
                        for (int p = 0; p < length; p++)
                        {
                            int at = (row * Size + start + p) * 4;
                            rgba[at] = colour[p * 3];
                            rgba[at + 1] = colour[p * 3 + 1];
                            rgba[at + 2] = colour[p * 3 + 2];
                            rgba[at + 3] = 255;
                        }
                    }
                }
            }
            return rgba;
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
