using System;

namespace CraterEdge.Generation
{
    public sealed class Octree
    {
        public const int Size = 32;
        public const int Voxels = Size * Size * Size;

        private const int Levels = 5;
        private const int PyramidNodes = 4681;
        private static readonly int[] LevelStart = { -1, 0, 4096, 4608, 4672, 4680 };

        public byte[] Nodes;

        public int NodeCount => Nodes.Length / 4;

        public Octree(byte[] nodes)
        {
            Nodes = nodes;
        }

        public bool SameAs(Octree other) => ((ReadOnlySpan<byte>)Nodes).SequenceEqual(other.Nodes);

        public bool IsUniform(out byte type, out byte density)
        {
            type = Nodes[0];
            density = Nodes[1];
            return NodeCount == 1;
        }

        public void Sample(int x, int y, int z, out byte type, out byte density)
        {
            int node = 0, half = Size / 2;
            while (true)
            {
                int at = node * 4;
                int child = Nodes[at + 2] | (Nodes[at + 3] << 8);
                if (child == 0 || half == 0)
                {
                    type = Nodes[at];
                    density = Nodes[at + 1];
                    return;
                }
                int i = (x >= half ? 4 : 0) + (y >= half ? 2 : 0) + (z >= half ? 1 : 0);
                if (x >= half) x -= half;
                if (y >= half) y -= half;
                if (z >= half) z -= half;
                node = child + i;
                half >>= 1;
            }
        }

        public void Rasterize(Span<Voxel> grid) => Rasterize(0, 0, Voxels, grid);

        private void Rasterize(int node, int start, int cells, Span<Voxel> grid)
        {
            int at = node * 4;
            int child = Nodes[at + 2] | (Nodes[at + 3] << 8);
            if (child == 0 || cells == 1)
            {
                grid.Slice(start, cells).Fill(new Voxel(Nodes[at], Nodes[at + 1]));
                return;
            }
            int step = cells / 8;
            for (int i = 0; i < 8; i++)
            {
                Rasterize(child + i, start + i * step, step, grid);
            }
        }

        public static Octree Build(ReadOnlySpan<Voxel> grid)
        {
            Span<Voxel> values = stackalloc Voxel[PyramidNodes];
            Span<bool> leaves = stackalloc bool[PyramidNodes];
            Span<int> counts = stackalloc int[8];
            for (int level = 1; level <= Levels; level++)
            {
                int start = LevelStart[level], below = LevelStart[level - 1], nodes = 1 << (3 * (Levels - level));
                for (int j = 0; j < nodes; j++)
                {
                    ReadOnlySpan<Voxel> children = level == 1 ? grid.Slice(8 * j, 8) : (ReadOnlySpan<Voxel>)values.Slice(below + 8 * j, 8);
                    bool allLeaves = level == 1 || AllSet(leaves.Slice(below + 8 * j, 8));
                    if (allLeaves && CanCollapse(children, out int keep))
                    {
                        values[start + j] = children[keep];
                        leaves[start + j] = true;
                    }
                    else
                    {
                        values[start + j] = Downsample(children, counts);
                        leaves[start + j] = false;
                    }
                }
            }
            var bytes = new byte[Count(Levels, 0, leaves) * 4];
            int next = 1;
            Write(Levels, 0, 0, grid, values, leaves, bytes, ref next);
            return new Octree(bytes);
        }

        private static bool AllSet(ReadOnlySpan<bool> flags)
        {
            for (int i = 0; i < flags.Length; i++)
            {
                if (!flags[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static int Count(int level, int j, ReadOnlySpan<bool> leaves)
        {
            if (level == 0 || leaves[LevelStart[level] + j])
            {
                return 1;
            }
            int n = 1;
            for (int i = 0; i < 8; i++)
            {
                n += Count(level - 1, 8 * j + i, leaves);
            }
            return n;
        }

        private static void Write(int level, int j, int id, ReadOnlySpan<Voxel> grid, ReadOnlySpan<Voxel> values,
            ReadOnlySpan<bool> leaves, byte[] bytes, ref int next)
        {
            Voxel v = level == 0 ? grid[j] : values[LevelStart[level] + j];
            int at = id * 4;
            bytes[at] = v.Type;
            bytes[at + 1] = v.Density;
            if (level == 0 || leaves[LevelStart[level] + j])
            {
                return;
            }
            int first = next;
            bytes[at + 2] = (byte)(first & 0xFF);
            bytes[at + 3] = (byte)(first >> 8);
            next += 8;
            for (int i = 0; i < 8; i++)
            {
                Write(level - 1, 8 * j + i, first + i, grid, values, leaves, bytes, ref next);
            }
        }

        private static bool CanCollapse(ReadOnlySpan<Voxel> children, out int keep)
        {
            keep = -1;
            bool above = false, below = false;
            for (int k = 0; k < 8; k++)
            {
                bool b = children[k].IsBelowSurface;
                above |= !b;
                below |= b;
                if (above && below)
                {
                    return false;
                }
            }
            int near = -1;
            for (int k = 0; k < 8; k++)
            {
                if (children[k].IsNearSurface)
                {
                    near = k;
                    break;
                }
            }
            if (near < 0)
            {
                keep = 0;
                return true;
            }
            keep = near;
            for (int l = 0; l < 8; l++)
            {
                if (children[l].IsNearSurface && children[l].Bits != children[near].Bits)
                {
                    return false;
                }
            }
            return true;
        }

        private static Voxel Downsample(ReadOnlySpan<Voxel> children, Span<int> counts)
        {
            int sum = 0, near = 0;
            for (int k = 0; k < 8; k++)
            {
                if (children[k].IsNearSurface)
                {
                    sum += children[k].Density;
                    near++;
                }
            }
            if (near == 0)
            {
                return new Voxel(children[0].Type, 0);
            }
            return new Voxel(MostCommonChildType(children, counts), (byte)(sum / near));
        }

        private static byte MostCommonChildType(ReadOnlySpan<Voxel> children, Span<int> counts)
        {
            counts.Clear();
            for (int i = 0; i < 8; i++)
            {
                if (!children[i].IsNearAndBelowSurface)
                {
                    continue;
                }
                for (int j = 0; j <= i; j++)
                {
                    if (children[i].Type == children[j].Type)
                    {
                        counts[j]++;
                        break;
                    }
                }
            }
            int best = 0;
            byte type = children[0].Type;
            for (int k = 1; k < 8; k++)
            {
                if (counts[k] > best || (counts[k] == best && children[k].Type > type))
                {
                    best = counts[k];
                    type = children[k].Type;
                }
            }
            return type;
        }
    }
}
