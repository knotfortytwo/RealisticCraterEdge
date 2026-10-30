using System.Collections.Concurrent;
using System.IO;

namespace CraterEdge.Generation
{
    public sealed class VanillaTerrain
    {
        private readonly string dir;
        private readonly bool fullWidth;
        private readonly ConcurrentDictionary<int, Batch> batches = new ConcurrentDictionary<int, Batch>();

        public VanillaTerrain(string compiledOctreesCacheDir, bool fullWidth = false)
        {
            dir = compiledOctreesCacheDir;
            this.fullWidth = fullWidth;
        }

        private static int Key(int bx, int by, int bz) => (bx * World.BatchesY + by) * World.BatchesZ + bz;

        public Batch GetBatch(int bx, int by, int bz)
        {
            return batches.GetOrAdd(Key(bx, by, bz), _ =>
            {
                string path = Path.Combine(dir, Batch.FileName(bx, by, bz));
                return File.Exists(path) ? Batch.Read(bx, by, bz, File.ReadAllBytes(path), fullWidth) : null;
            });
        }

        public Batch Load(int bx, int by, int bz)
        {
            if (batches.TryGetValue(Key(bx, by, bz), out var cached))
            {
                return cached;
            }
            string path = Path.Combine(dir, Batch.FileName(bx, by, bz));
            return File.Exists(path) ? Batch.Read(bx, by, bz, File.ReadAllBytes(path), fullWidth) : null;
        }

        public byte[] ReadRaw(int bx, int by, int bz)
        {
            string path = Path.Combine(dir, Batch.FileName(bx, by, bz));
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        public void Sample(int vx, int vy, int vz, out byte type, out byte density)
        {
            type = 0;
            density = 0;
            if (vx < 0 || vy < 0 || vz < 0 || vx >= World.SizeX || vy >= World.SizeY || vz >= World.SizeZ)
            {
                return;
            }
            int ox = vx / Octree.Size, oy = vy / Octree.Size, oz = vz / Octree.Size;
            var batch = GetBatch(ox / Batch.OctreesPerSide, oy / Batch.OctreesPerSide, oz / Batch.OctreesPerSide);
            var tree = batch?.Octrees[Batch.LocalIndex(ox % Batch.OctreesPerSide, oy % Batch.OctreesPerSide, oz % Batch.OctreesPerSide)];
            tree?.Sample(vx % Octree.Size, vy % Octree.Size, vz % Octree.Size, out type, out density);
        }

        public void Clear() => batches.Clear();
    }
}
