using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CraterEdge.Generation
{
    public sealed class Hologram
    {
        public const int ChunkSize = 128, Detail = 4, Cells = ChunkSize / Detail;
        public const int ChunksX = World.SizeX / ChunkSize, ChunksY = World.SizeY / ChunkSize, ChunksZ = World.SizeZ / ChunkSize;
        private const int FileVersion = 1;
        private const int Points = Cells + 3;
        private const double CutMargin = 2;
        private const int Block = 4, CoarsePoints = (Points - 1) / Block + 2;
        private const int FarStep = 8;
        private const float Quantum = 1024, Offset = 2;

        public sealed class Chunk
        {
            public readonly int X, Y, Z;
            public readonly float[] Vertices;
            public readonly int[] Triangles;

            public Chunk(int x, int y, int z, float[] vertices, int[] triangles)
            {
                X = x; Y = y; Z = z; Vertices = vertices; Triangles = triangles;
            }
        }

        private readonly byte[] data;
        private readonly SortedDictionary<int, int> offsets = new SortedDictionary<int, int>();
        private readonly bool[] replaced = new bool[ChunksX * ChunksY * ChunksZ];

        private Hologram(byte[] data)
        {
            this.data = data;
        }

        public static int Key(int x, int y, int z) => (x * ChunksY + y) * ChunksZ + z;

        public IEnumerable<Chunk> Chunks => offsets.Values.Select(Decode);

        public int Count => offsets.Count;

        public Chunk Inside(int x, int y, int z) =>
            x >= 0 && x < ChunksX && y >= 0 && y < ChunksY && z >= 0 && z < ChunksZ && offsets.TryGetValue(Key(x, y, z), out int at) ? Decode(at) : null;

        public bool Replaces(int x, int y, int z) =>
            x >= 0 && x < ChunksX && y >= 0 && y < ChunksY && z >= 0 && z < ChunksZ && replaced[Key(x, y, z)];

        public static Chunk Outside(FarField far, int cx, int cy, int cz)
        {
            double x0 = World.MinX + (double)cx * ChunkSize, y0 = World.MinY + (double)cy * ChunkSize, z0 = World.MinZ + (double)cz * ChunkSize;
            if (x0 + ChunkSize > World.MinX && x0 < World.MinX + World.SizeX && z0 + ChunkSize > World.MinZ && z0 < World.MinZ + World.SizeZ
                || Math.Abs(x0 + ChunkSize / 2.0) > FarField.Extent || Math.Abs(z0 + ChunkSize / 2.0) > FarField.Extent)
            {
                return null;
            }
            const int n = ChunkSize / FarStep + 1;
            var heights = new double[n * n];
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    heights[i * n + k] = far.HeightAt(x0 + i * FarStep, z0 + k * FarStep, false);
                }
            }
            var vertices = new List<float>();
            var triangles = new List<int>();
            var index = new int[n * n];
            for (int i = 0; i < index.Length; i++)
            {
                index[i] = -1;
            }
            for (int i = 0; i + 1 < n; i++)
            {
                for (int k = 0; k + 1 < n; k++)
                {
                    double a = heights[i * n + k], b = heights[(i + 1) * n + k], c = heights[(i + 1) * n + k + 1], d = heights[i * n + k + 1];
                    double mean = (a + b + c + d) / 4;
                    if (double.IsNaN(mean) || mean < y0 || mean >= y0 + ChunkSize)
                    {
                        continue;
                    }
                    int va = FarVertex(i, k), vb = FarVertex(i + 1, k), vc = FarVertex(i + 1, k + 1), vd = FarVertex(i, k + 1);
                    triangles.Add(va); triangles.Add(vd); triangles.Add(vc);
                    triangles.Add(va); triangles.Add(vc); triangles.Add(vb);
                }
            }
            return triangles.Count == 0 ? null : new Chunk(cx, cy, cz, vertices.ToArray(), triangles.ToArray());

            int FarVertex(int i, int k)
            {
                int at = i * n + k;
                if (index[at] < 0)
                {
                    index[at] = vertices.Count / 3;
                    vertices.Add((float)(i * FarStep / (double)Detail));
                    vertices.Add((float)((heights[at] - y0) / Detail));
                    vertices.Add((float)(k * FarStep / (double)Detail));
                }
                return index[at];
            }
        }

        public static Hologram Build(Field field, int threads)
        {
            var chunks = new Chunk[ChunksX * ChunksY * ChunksZ];
            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads) };
            Parallel.For(0, chunks.Length, options, () => new Mesher(field), (i, state, mesher) =>
            {
                chunks[i] = mesher.Mesh(i / (ChunksY * ChunksZ), i / ChunksZ % ChunksY, i % ChunksZ);
                return mesher;
            }, _ => { });
            var replaced = new bool[chunks.Length];
            for (int cx = 0; cx < ChunksX; cx++)
            {
                for (int cz = 0; cz < ChunksZ; cz++)
                {
                    int lowestCut = int.MaxValue;
                    for (int x = cx * ChunkSize; x < (cx + 1) * ChunkSize; x++)
                    {
                        for (int z = cz * ChunkSize; z < (cz + 1) * ChunkSize; z++)
                        {
                            lowestCut = Math.Min(lowestCut, (int)field.Model.Cut[x * IslandModel.Width + z]);
                        }
                    }
                    for (int cy = 0; cy < ChunksY; cy++)
                    {
                        replaced[Key(cx, cy, cz)] = World.MinY + (cy + 1) * ChunkSize <= lowestCut;
                    }
                }
            }
            return Read(Encode(chunks.Where(c => c != null).ToList(), replaced));
        }

        private sealed class Mesher
        {
            private const byte Unsampled = 0, Valid = 1, Invalid = 2;

            private readonly Field field;
            private readonly Field.Column[] columns = new Field.Column[Points * Points];
            private readonly bool[] prepared = new bool[Points * Points];
            private readonly double[] bases = new double[Points * Points], cuts = new double[Points * Points];
            private readonly bool[] inBox = new bool[Points * Points];
            private readonly float[] values = new float[Points * Points * Points];
            private readonly byte[] states = new byte[Points * Points * Points];
            private readonly int[] cellVertex = new int[(Points - 1) * (Points - 1) * (Points - 1)];
            private readonly double[] coarse = new double[CoarsePoints * CoarsePoints * CoarsePoints];
            private readonly bool[] candidate = new bool[(CoarsePoints - 1) * (CoarsePoints - 1) * (CoarsePoints - 1)];
            private readonly Field.Column scratch = new Field.Column();
            private readonly List<float> vertices = new List<float>();
            private readonly List<int> triangles = new List<int>();
            private double x0, y0, z0;

            public Mesher(Field field)
            {
                this.field = field;
                for (int i = 0; i < columns.Length; i++)
                {
                    columns[i] = new Field.Column();
                }
            }

            public Chunk Mesh(int cx, int cy, int cz)
            {
                x0 = World.MinX + cx * ChunkSize;
                y0 = World.MinY + cy * ChunkSize;
                z0 = World.MinZ + cz * ChunkSize;

                double lowest = y0 - Detail, highestCut = double.MinValue;
                for (int px = 0; px < Points; px++)
                {
                    for (int pz = 0; pz < Points; pz++)
                    {
                        int c = px * Points + pz;
                        double x = X(px), z = Z(pz);
                        inBox[c] = x >= World.MinX && x < World.MinX + World.SizeX && z >= World.MinZ && z < World.MinZ + World.SizeZ;
                        prepared[c] = false;
                        if (inBox[c])
                        {
                            int col = Column(x, z);
                            bases[c] = field.Model.Base[col];
                            cuts[c] = field.Model.Cut[col];
                            highestCut = Math.Max(highestCut, cuts[c]);
                        }
                    }
                }
                if (lowest >= highestCut - CutMargin)
                {
                    return null;
                }
                double centreX = x0 + ChunkSize / 2.0, centreY = y0 + ChunkSize / 2.0, centreZ = z0 + ChunkSize / 2.0;
                double reach = Field.Lipschitz * (ChunkSize * Math.Sqrt(3) / 2 + 2 * Detail);
                if (Math.Abs(field.Evaluate(scratch, field.Model.Base[Column(centreX, centreZ)], centreX, centreY, centreZ)) > reach)
                {
                    return null;
                }

                for (int mx = 0; mx < CoarsePoints; mx++)
                {
                    for (int mz = 0; mz < CoarsePoints; mz++)
                    {
                        double x = X(mx * Block), z = Z(mz * Block);
                        field.Prepare(scratch, x, z);
                        double baseDistance = field.Model.Base[Column(x, z)];
                        for (int my = 0; my < CoarsePoints; my++)
                        {
                            coarse[(mx * CoarsePoints + my) * CoarsePoints + mz] = field.Evaluate(scratch, baseDistance, Y(my * Block));
                        }
                    }
                }
                double bound = Field.Lipschitz * Block * Detail * Math.Sqrt(3);
                bool any = false;
                const int cb = CoarsePoints - 1;
                for (int bx = 0; bx < cb; bx++)
                {
                    for (int by = 0; by < cb; by++)
                    {
                        for (int bz = 0; bz < cb; bz++)
                        {
                            bool near = true;
                            for (int corner = 0; corner < 8 && near; corner++)
                            {
                                double v = coarse[((bx + (corner & 1)) * CoarsePoints + by + (corner >> 1 & 1)) * CoarsePoints + bz + (corner >> 2)];
                                near = Math.Abs(v) <= bound;
                            }
                            candidate[(bx * cb + by) * cb + bz] = near;
                            any |= near;
                        }
                    }
                }
                if (!any)
                {
                    return null;
                }

                Array.Clear(states, 0, states.Length);
                for (int bx = 0; bx < cb; bx++)
                {
                    for (int by = 0; by < cb; by++)
                    {
                        for (int bz = 0; bz < cb; bz++)
                        {
                            if (!candidate[(bx * cb + by) * cb + bz])
                            {
                                continue;
                            }
                            for (int px = bx * Block; px <= bx * Block + Block && px < Points; px++)
                            {
                                for (int pz = bz * Block; pz <= bz * Block + Block && pz < Points; pz++)
                                {
                                    for (int py = by * Block; py <= by * Block + Block && py < Points; py++)
                                    {
                                        Sample(px, py, pz);
                                    }
                                }
                            }
                        }
                    }
                }

                for (int i = 0; i < cellVertex.Length; i++)
                {
                    cellVertex[i] = -1;
                }
                vertices.Clear();
                triangles.Clear();
                for (int px = 1; px <= Cells; px++)
                {
                    for (int py = 1; py <= Cells; py++)
                    {
                        for (int pz = 1; pz <= Cells; pz++)
                        {
                            Edge(px, py, pz, 0);
                            Edge(px, py, pz, 1);
                            Edge(px, py, pz, 2);
                        }
                    }
                }
                return triangles.Count == 0 ? null : new Chunk(cx, cy, cz, vertices.ToArray(), triangles.ToArray());
            }

            private double X(int p) => x0 + (p - 1) * Detail;
            private double Y(int p) => y0 + (p - 1) * Detail;
            private double Z(int p) => z0 + (p - 1) * Detail;

            private static int Point(int px, int py, int pz) => (px * Points + py) * Points + pz;

            private void Sample(int px, int py, int pz)
            {
                int at = Point(px, py, pz);
                if (states[at] != Unsampled)
                {
                    return;
                }
                int c = px * Points + pz;
                double y = Y(py);
                if (!inBox[c] || y >= cuts[c] - CutMargin || y < World.MinY)
                {
                    states[at] = Invalid;
                    return;
                }
                if (!prepared[c])
                {
                    field.Prepare(columns[c], X(px), Z(pz));
                    prepared[c] = true;
                }
                values[at] = (float)field.Evaluate(columns[c], bases[c], y);
                states[at] = Valid;
            }

            private void Edge(int px, int py, int pz, int axis)
            {
                int qx = px + (axis == 0 ? 1 : 0), qy = py + (axis == 1 ? 1 : 0), qz = pz + (axis == 2 ? 1 : 0);
                int a = Point(px, py, pz), b = Point(qx, qy, qz);
                if (states[a] != Valid || states[b] != Valid || values[a] >= 0 == values[b] >= 0)
                {
                    return;
                }
                int v0, v1, v2, v3;
                switch (axis)
                {
                    case 0:
                        v0 = Vertex(px, py - 1, pz - 1); v1 = Vertex(px, py, pz - 1); v2 = Vertex(px, py, pz); v3 = Vertex(px, py - 1, pz);
                        break;
                    case 1:
                        v0 = Vertex(px - 1, py, pz - 1); v1 = Vertex(px - 1, py, pz); v2 = Vertex(px, py, pz); v3 = Vertex(px, py, pz - 1);
                        break;
                    default:
                        v0 = Vertex(px - 1, py - 1, pz); v1 = Vertex(px, py - 1, pz); v2 = Vertex(px, py, pz); v3 = Vertex(px - 1, py, pz);
                        break;
                }
                if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0)
                {
                    return;
                }
                if (values[a] >= 0)
                {
                    triangles.Add(v0); triangles.Add(v1); triangles.Add(v2);
                    triangles.Add(v0); triangles.Add(v2); triangles.Add(v3);
                }
                else
                {
                    triangles.Add(v0); triangles.Add(v2); triangles.Add(v1);
                    triangles.Add(v0); triangles.Add(v3); triangles.Add(v2);
                }
            }

            private int Vertex(int cx, int cy, int cz)
            {
                int cell = (cx * (Points - 1) + cy) * (Points - 1) + cz;
                if (cellVertex[cell] != -1)
                {
                    return cellVertex[cell];
                }
                cellVertex[cell] = -2;
                double sx = 0, sy = 0, sz = 0;
                int crossings = 0;
                for (int corner = 0; corner < 8; corner++)
                {
                    if (states[Point(cx + (corner & 1), cy + (corner >> 1 & 1), cz + (corner >> 2))] != Valid)
                    {
                        return -2;
                    }
                }
                for (int edge = 0; edge < 12; edge++)
                {
                    int axis = edge / 4, j = edge % 4, ax, ay, az;
                    switch (axis)
                    {
                        case 0: ax = 0; ay = j & 1; az = j >> 1; break;
                        case 1: ax = j & 1; ay = 0; az = j >> 1; break;
                        default: ax = j & 1; ay = j >> 1; az = 0; break;
                    }
                    int bx = ax + (axis == 0 ? 1 : 0), by = ay + (axis == 1 ? 1 : 0), bz = az + (axis == 2 ? 1 : 0);
                    float va = values[Point(cx + ax, cy + ay, cz + az)], vb = values[Point(cx + bx, cy + by, cz + bz)];
                    if (va >= 0 == vb >= 0)
                    {
                        continue;
                    }
                    double t = va / (double)(va - vb);
                    sx += ax + (bx - ax) * t;
                    sy += ay + (by - ay) * t;
                    sz += az + (bz - az) * t;
                    crossings++;
                }
                if (crossings == 0)
                {
                    return -2;
                }
                int index = vertices.Count / 3;
                vertices.Add((float)(cx - 1 + sx / crossings));
                vertices.Add((float)(cy - 1 + sy / crossings));
                vertices.Add((float)(cz - 1 + sz / crossings));
                cellVertex[cell] = index;
                return index;
            }
        }

        private static int Column(double x, double z)
        {
            int cx = (int)Math.Floor(x) - World.MinX, cz = (int)Math.Floor(z) - World.MinZ;
            cx = cx < 0 ? 0 : cx >= World.SizeX ? World.SizeX - 1 : cx;
            cz = cz < 0 ? 0 : cz >= World.SizeZ ? World.SizeZ - 1 : cz;
            return cx * IslandModel.Width + cz;
        }

        public byte[] Write() => data;

        private static byte[] Encode(List<Chunk> chunks, bool[] replaced)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(FileVersion);
                w.Write(Field.Version);
                for (int i = 0; i < replaced.Length; i += 8)
                {
                    int bits = 0;
                    for (int b = 0; b < 8 && i + b < replaced.Length; b++)
                    {
                        bits |= replaced[i + b] ? 1 << b : 0;
                    }
                    w.Write((byte)bits);
                }
                w.Write(chunks.Count);
                foreach (var chunk in chunks)
                {
                    w.Write((byte)chunk.X);
                    w.Write((byte)chunk.Y);
                    w.Write((byte)chunk.Z);
                    w.Write(chunk.Vertices.Length / 3);
                    w.Write(chunk.Triangles.Length);
                    foreach (float v in chunk.Vertices)
                    {
                        w.Write((ushort)Math.Round((v + Offset) * Quantum));
                    }
                    foreach (int t in chunk.Triangles)
                    {
                        w.Write((ushort)t);
                    }
                }
                w.Flush();
                return stream.ToArray();
            }
        }

        public static Hologram Read(byte[] data)
        {
            var hologram = new Hologram(data);
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                if (r.ReadInt32() != FileVersion || r.ReadInt32() != Field.Version)
                {
                    throw new InvalidDataException("hologram from another version");
                }
                for (int i = 0; i < hologram.replaced.Length; i += 8)
                {
                    int bits = r.ReadByte();
                    for (int b = 0; b < 8 && i + b < hologram.replaced.Length; b++)
                    {
                        hologram.replaced[i + b] = (bits >> b & 1) != 0;
                    }
                }
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    int at = (int)r.BaseStream.Position;
                    int x = r.ReadByte(), y = r.ReadByte(), z = r.ReadByte();
                    int vertices = r.ReadInt32(), triangles = r.ReadInt32();
                    if (vertices < 0 || triangles < 0)
                    {
                        throw new InvalidDataException("bad hologram chunk");
                    }
                    r.BaseStream.Position += 6L * vertices + 2L * triangles;
                    if (r.BaseStream.Position > data.Length)
                    {
                        throw new InvalidDataException("truncated hologram");
                    }
                    hologram.offsets[Key(x, y, z)] = at;
                }
            }
            return hologram;
        }

        private Chunk Decode(int at)
        {
            int x = data[at], y = data[at + 1], z = data[at + 2];
            int vertexCount = BitConverter.ToInt32(data, at + 3), triangleCount = BitConverter.ToInt32(data, at + 7);
            var vertices = new float[vertexCount * 3];
            var triangles = new int[triangleCount];
            int p = at + 11;
            for (int v = 0; v < vertices.Length; v++, p += 2)
            {
                vertices[v] = BitConverter.ToUInt16(data, p) / Quantum - Offset;
            }
            for (int t = 0; t < triangles.Length; t++, p += 2)
            {
                triangles[t] = BitConverter.ToUInt16(data, p);
            }
            return new Chunk(x, y, z, vertices, triangles);
        }
    }
}
