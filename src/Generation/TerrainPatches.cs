using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace CraterEdge.Generation
{
    public static class TerrainPatches
    {
        public const string EdgesFile = "realistic-crater-edge-edges.optoctreepatch";
        public const uint FormatVersion = 0;
        private static readonly Regex BatchName =
            new Regex(@"^realistic-crater-edge-(m?\d+)-(m?\d+)-(m?\d+)\.optoctreepatch$");
        private static readonly Octree Empty = new Octree(new byte[4]);

        public static string BatchFile(int x, int y, int z) => $"realistic-crater-edge-{Id(x)}-{Id(y)}-{Id(z)}.optoctreepatch";

        private static string Id(int v) => v < 0 ? "m" + (-v).ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);

        private static int ParseId(string s) => s[0] == 'm' ? -int.Parse(s.Substring(1), CultureInfo.InvariantCulture) : int.Parse(s, CultureInfo.InvariantCulture);

        public static bool TryParseBatchFile(string fileName, out int x, out int y, out int z)
        {
            var m = BatchName.Match(fileName);
            x = y = z = 0;
            if (!m.Success)
            {
                return false;
            }
            x = ParseId(m.Groups[1].Value);
            y = ParseId(m.Groups[2].Value);
            z = ParseId(m.Groups[3].Value);
            return true;
        }

        public static IEnumerable<(int x, int y, int z)> EdgeBatches()
        {
            const int edge = World.BatchesX - 1;
            for (int y = 0; y <= TerrainCache.HighestChangedRow; y++)
            {
                for (int b = World.FarMinBatch; b <= World.FarMaxBatch; b++)
                {
                    yield return (edge, y, b);
                    if (b != edge)
                    {
                        yield return (b, y, edge);
                    }
                }
            }
        }

        public static bool IsEdge(int x, int y, int z) =>
            (x == World.BatchesX - 1 || z == World.BatchesZ - 1) && y >= 0 && y <= TerrainCache.HighestChangedRow
            && World.InFarRange(x, z);

        public static byte[] ZeroOctrees(IEnumerable<(int x, int y, int z)> batches)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(FormatVersion);
                foreach (var (x, y, z) in batches)
                {
                    w.Write((short)x);
                    w.Write((short)y);
                    w.Write((short)z);
                    w.Write((byte)0);
                }
                w.Flush();
                return stream.ToArray();
            }
        }

        public static byte[] ForBatch(Batch built, Batch baseline)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(FormatVersion);
                w.Write(checked((short)built.X));
                w.Write(checked((short)built.Y));
                w.Write(checked((short)built.Z));
                long countAt = stream.Position;
                w.Write((byte)0);
                byte count = 0;
                for (int local = 0; local < built.Octrees.Length; local++)
                {
                    var tree = built.Octrees[local];
                    if (tree.SameAs(baseline?.Octrees[local] ?? Empty))
                    {
                        continue;
                    }
                    w.Write((byte)local);
                    w.Write(checked((ushort)tree.NodeCount));
                    w.Write(tree.Nodes);
                    count++;
                }
                w.Flush();
                byte[] bytes = stream.ToArray();
                bytes[countAt] = count;
                return bytes;
            }
        }

        public static Batch Apply(byte[] patch, Batch baseline)
        {
            using (var r = new BinaryReader(new MemoryStream(patch)))
            {
                if (r.ReadUInt32() != FormatVersion)
                {
                    throw new InvalidDataException("unknown patch version");
                }
                var batch = new Batch(r.ReadInt16(), r.ReadInt16(), r.ReadInt16());
                for (int local = 0; local < batch.Octrees.Length; local++)
                {
                    batch.Octrees[local] = baseline?.Octrees[local] ?? Empty;
                }
                int count = r.ReadByte();
                for (int i = 0; i < count; i++)
                {
                    int local = r.ReadByte();
                    batch.Octrees[local] = new Octree(r.ReadBytes(r.ReadUInt16() * 4));
                }
                if (r.BaseStream.Position != patch.Length)
                {
                    throw new InvalidDataException("one batch per patch expected");
                }
                return batch;
            }
        }
    }
}
