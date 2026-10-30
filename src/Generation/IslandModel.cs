using System;
using System.Collections.Generic;

namespace CraterEdge.Generation
{
    public sealed class IslandModel
    {
        public const int Width = World.SizeX;
        public const int CleanLayerAbove = 2;

        public readonly bool[] Island;
        public readonly short[] Cut;
        public readonly float[] Base;
        public int IslandColumns { get; private set; }
        public int RefinedColumns { get; private set; }

        private IslandModel(int n)
        {
            Island = new bool[n];
            Cut = new short[n];
            Base = new float[n];
        }

        public static bool IsCropPlane(int y) => (y - World.MinY) % Batch.Size == 0;

        public static IslandModel Build(short[] lowestRock, VanillaTerrain vanilla)
        {
            int n = Width * Width;
            var model = new IslandModel(n);
            var aligned = new bool[n];
            for (int i = 0; i < n; i++)
            {
                aligned[i] = lowestRock[i] != EdgeInputs.NoRock && IsCropPlane(lowestRock[i]);
            }
            int count = LargestComponent(aligned, model.Island);
            model.IslandColumns = count;

            int[] nearest = DistanceField.NearestTarget(model.Island, Width, Width);
            for (int i = 0; i < n; i++)
            {
                model.Cut[i] = model.Island[i] ? lowestRock[i] : nearest[i] >= 0 ? lowestRock[nearest[i]] : short.MinValue;
            }

            float[] signed = DistanceField.Signed(model.Island, Width, Width);
            int refined = 0;
            for (int i = 0; i < n; i++)
            {
                float b = signed[i];
                if (Math.Abs(b) <= 1.5f && model.Cut[i] != short.MinValue)
                {
                    int vx = i / Width, vz = i % Width;
                    int vy = model.Cut[i] - World.MinY + CleanLayerAbove;
                    vanilla.Sample(vx, vy, vz, out byte type, out byte density);
                    float decoded = density != Density.Far ? Density.Decode(density) : 0;
                    if (density != Density.Far && (decoded >= 0) == model.Island[i])
                    {
                        b = decoded;
                        refined++;
                    }
                }
                model.Base[i] = b;
            }
            model.RefinedColumns = refined;
            return model;
        }

        private static int LargestComponent(bool[] cells, bool[] result)
        {
            int n = cells.Length;
            var label = new int[n];
            var queue = new int[n];
            int best = 0, bestLabel = 0, next = 0;
            for (int start = 0; start < n; start++)
            {
                if (!cells[start] || label[start] != 0)
                {
                    continue;
                }
                next++;
                int head = 0, tail = 0, size = 0;
                queue[tail++] = start;
                label[start] = next;
                while (head < tail)
                {
                    int c = queue[head++];
                    size++;
                    int cx = c / Width, cz = c % Width;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int x = cx + dx, z = cz + dz;
                            if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= Width || z >= Width)
                            {
                                continue;
                            }
                            int j = x * Width + z;
                            if (cells[j] && label[j] == 0)
                            {
                                label[j] = next;
                                queue[tail++] = j;
                            }
                        }
                    }
                }
                if (size > best)
                {
                    best = size;
                    bestLabel = next;
                }
            }
            for (int i = 0; i < n; i++)
            {
                result[i] = label[i] == bestLabel && bestLabel != 0;
            }
            return best;
        }
    }
}
