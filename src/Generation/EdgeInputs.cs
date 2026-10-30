using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CraterEdge.Generation
{
    public sealed class EdgeInputs
    {
        public const int Width = World.SizeX;
        public const short NoRock = short.MaxValue;

        public readonly short[] LowestRock = new short[Width * Width];
        public readonly Dictionary<float, (byte[] types, byte[] densities)> Layers = new Dictionary<float, (byte[], byte[])>();

        private static readonly Regex BatchName = new Regex(@"compiled-batch-(\d+)-(\d+)-(\d+)\.optoctrees$");

        public static EdgeInputs Measure(string cacheDir, IEnumerable<float> layerHeights)
        {
            var inputs = new EdgeInputs();
            for (int i = 0; i < inputs.LowestRock.Length; i++)
            {
                inputs.LowestRock[i] = NoRock;
            }
            var wanted = new List<(float y, int row, int local)>();
            foreach (float y in layerHeights)
            {
                int voxel = (int)Math.Floor(y - World.MinY);
                wanted.Add((y, voxel / Octree.Size, voxel % Octree.Size));
                inputs.Layers[y] = (new byte[Width * Width], new byte[Width * Width]);
            }
            var locks = new object[World.BatchesX * World.BatchesZ];
            for (int i = 0; i < locks.Length; i++)
            {
                locks[i] = new object();
            }
            Parallel.ForEach(Directory.GetFiles(cacheDir, "compiled-batch-*.optoctrees"), path =>
            {
                var m = BatchName.Match(path);
                int bx = int.Parse(m.Groups[1].Value), by = int.Parse(m.Groups[2].Value), bz = int.Parse(m.Groups[3].Value);
                var batch = Batch.Read(bx, by, bz, File.ReadAllBytes(path));
                var lowest = new int[Octree.Size * Octree.Size];
                var types = new byte[Octree.Size * Octree.Size];
                var densities = new byte[Octree.Size * Octree.Size];
                for (int local = 0; local < batch.Octrees.Length; local++)
                {
                    var tree = batch.Octrees[local];
                    if (tree == null)
                    {
                        continue;
                    }
                    var (ox, oy, oz) = batch.OctreeCoords(local);
                    for (int i = 0; i < lowest.Length; i++)
                    {
                        lowest[i] = int.MaxValue;
                    }
                    tree.LowestSolid(lowest);
                    lock (locks[bx * World.BatchesZ + bz])
                    {
                        for (int x = 0; x < Octree.Size; x++)
                        {
                            for (int z = 0; z < Octree.Size; z++)
                            {
                                int l = lowest[x * Octree.Size + z];
                                if (l == int.MaxValue)
                                {
                                    continue;
                                }
                                int column = (ox * Octree.Size + x) * Width + oz * Octree.Size + z;
                                short y = (short)(World.MinY + oy * Octree.Size + l);
                                if (y < inputs.LowestRock[column])
                                {
                                    inputs.LowestRock[column] = y;
                                }
                            }
                        }
                    }
                    foreach (var (y, row, localY) in wanted)
                    {
                        if (oy != row)
                        {
                            continue;
                        }
                        tree.RasterizeLayer(localY, types, densities);
                        var (lt, ld) = inputs.Layers[y];
                        for (int x = 0; x < Octree.Size; x++)
                        {
                            int column = (ox * Octree.Size + x) * Width + oz * Octree.Size;
                            Array.Copy(types, x * Octree.Size, lt, column, Octree.Size);
                            Array.Copy(densities, x * Octree.Size, ld, column, Octree.Size);
                        }
                    }
                }
            });
            return inputs;
        }

        public bool[] SolidMask(float y)
        {
            var (types, densities) = Layers[y];
            var mask = new bool[types.Length];
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = Density.IsSolid(types[i], densities[i]);
            }
            return mask;
        }
    }
}
