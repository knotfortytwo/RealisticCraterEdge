using System;

namespace CraterEdge.Generation
{
    public static class OctreeQueries
    {
        public static void RasterizeLayer(this Octree tree, int localY, byte[] types, byte[] densities)
        {
            Layer(tree.Nodes, 0, 0, 0, 0, Octree.Size, localY, types, densities);
        }

        private static void Layer(byte[] n, int node, int x0, int y0, int z0, int size, int ly, byte[] types, byte[] densities)
        {
            int at = node * 4;
            int child = n[at + 2] | (n[at + 3] << 8);
            if (child == 0 || size == 1)
            {
                for (int x = x0; x < x0 + size; x++)
                {
                    for (int z = z0; z < z0 + size; z++)
                    {
                        types[x * Octree.Size + z] = n[at];
                        densities[x * Octree.Size + z] = n[at + 1];
                    }
                }
                return;
            }
            int half = size / 2;
            int hy = ly >= y0 + half ? 1 : 0;
            for (int hx = 0; hx < 2; hx++)
            {
                for (int hz = 0; hz < 2; hz++)
                {
                    Layer(n, child + (hx << 2) + (hy << 1) + hz, x0 + hx * half, y0 + hy * half, z0 + hz * half, half, ly, types, densities);
                }
            }
        }

        public static void LowestSolid(this Octree tree, int[] lowest)
        {
            Lowest(tree.Nodes, 0, 0, 0, 0, Octree.Size, lowest);
        }

        private static void Lowest(byte[] n, int node, int x0, int y0, int z0, int size, int[] lowest)
        {
            int at = node * 4;
            int child = n[at + 2] | (n[at + 3] << 8);
            if (child == 0 || size == 1)
            {
                if (!Density.IsSolid(n[at], n[at + 1]))
                {
                    return;
                }
                for (int x = x0; x < x0 + size; x++)
                {
                    for (int z = z0; z < z0 + size; z++)
                    {
                        int i = x * Octree.Size + z;
                        if (y0 < lowest[i])
                        {
                            lowest[i] = y0;
                        }
                    }
                }
                return;
            }
            int half = size / 2;
            for (int i = 0; i < 8; i++)
            {
                Lowest(n, child + i, x0 + ((i >> 2) & 1) * half, y0 + ((i >> 1) & 1) * half, z0 + (i & 1) * half, half, lowest);
            }
        }
    }

    public static class Density
    {
        public const byte Far = 0;
        public const byte Surface = 126;

        public static bool IsSolid(byte type, byte density) => density == 0 ? type > 0 : density >= Surface;

        public static float Decode(byte d) => (d - 126f) / 126f;

        public static byte Encode(float dist)
        {
            if (Math.Abs(dist) > 1f)
            {
                return Far;
            }
            return (byte)((int)Math.Floor(dist * 126f) + 126);
        }
    }
}
