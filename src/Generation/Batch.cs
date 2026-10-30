using System;
using System.IO;

namespace CraterEdge.Generation
{
    public sealed class Batch
    {
        public const int Version = 4;
        public const int OctreesPerSide = 5;
        public const int Size = OctreesPerSide * Octree.Size;

        public readonly int X, Y, Z;
        public readonly Octree[] Octrees = new Octree[OctreesPerSide * OctreesPerSide * OctreesPerSide];

        public Batch(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static int LocalIndex(int x, int y, int z) => (x * OctreesPerSide + y) * OctreesPerSide + z;

        public (int x, int y, int z) OctreeCoords(int local) =>
            (X * OctreesPerSide + local / 25, Y * OctreesPerSide + local / 5 % 5, Z * OctreesPerSide + local % 5);

        public static bool InWorld(int ox, int oy, int oz) =>
            ox >= 0 && oy >= 0 && oz >= 0 && ox < World.OctreesX && oy < World.OctreesY && oz < World.OctreesZ;

        public static string FileName(int x, int y, int z) => $"compiled-batch-{x}-{y}-{z}.optoctrees";

        public static Batch Read(int x, int y, int z, byte[] data, bool fullWidth = false)
        {
            var batch = new Batch(x, y, z);
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                int version = reader.ReadInt32();
                if (version < Version)
                {
                    throw new InvalidDataException($"batch {x},{y},{z}: version {version}");
                }
                for (int local = 0; local < batch.Octrees.Length; local++)
                {
                    var (ox, oy, oz) = batch.OctreeCoords(local);
                    if (!fullWidth && !InWorld(ox, oy, oz))
                    {
                        continue;
                    }
                    int count = reader.ReadUInt16();
                    batch.Octrees[local] = new Octree(reader.ReadBytes(count * 4));
                }
                if (reader.BaseStream.Position != data.Length)
                {
                    throw new InvalidDataException($"batch {x},{y},{z}: {data.Length - reader.BaseStream.Position} trailing bytes");
                }
            }
            return batch;
        }

        public byte[] Write(bool fullWidth = true)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Version);
                for (int local = 0; local < Octrees.Length; local++)
                {
                    var (ox, oy, oz) = OctreeCoords(local);
                    if (!fullWidth && !InWorld(ox, oy, oz))
                    {
                        continue;
                    }
                    var tree = Octrees[local] ?? throw new InvalidOperationException($"batch {X},{Y},{Z}: octree {local} missing");
                    writer.Write(checked((ushort)tree.NodeCount));
                    writer.Write(tree.Nodes);
                }
                writer.Flush();
                return stream.ToArray();
            }
        }
    }

    public static class World
    {
        public const int SizeX = 4096, SizeY = 3200, SizeZ = 4096;
        public const int MinX = -2048, MinY = -3040, MinZ = -2048;
        public const int OctreesX = SizeX / Octree.Size, OctreesY = SizeY / Octree.Size, OctreesZ = SizeZ / Octree.Size;
        public const int BatchesX = (OctreesX + Batch.OctreesPerSide - 1) / Batch.OctreesPerSide;
        public const int BatchesY = (OctreesY + Batch.OctreesPerSide - 1) / Batch.OctreesPerSide;
        public const int BatchesZ = (OctreesZ + Batch.OctreesPerSide - 1) / Batch.OctreesPerSide;

        public const int FarMinBatch = -39, FarMaxBatch = 63, FarBatches = FarMaxBatch - FarMinBatch + 1;

        public static bool InBox(int bx, int bz) => bx >= 0 && bz >= 0 && bx < BatchesX && bz < BatchesZ;

        public static bool InFarRange(int bx, int bz) =>
            bx >= FarMinBatch && bz >= FarMinBatch && bx <= FarMaxBatch && bz <= FarMaxBatch;
    }
}
