using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CraterEdge.Generation
{
    public sealed class TerrainCache
    {
        public const int HighestChangedRow = 15;
        private const int Layout = 1;
        private const string RuntimeFolder = "runtime";
        private const int Rows = HighestChangedRow + 1;
        private const int PrefetchRadius = 3;
        private const int PrefetchRows = 3;
        private const double SurfaceMargin = 40;
        private const string Marker = "complete";
        private const string FarFieldFile = "farfield.bin", FarDistanceFile = "fardistance.bin", FeaturesFile = "features.bin",
            MapOverlayFile = "mapoverlay.bin", WideMapFile = "widemap.bin";

        public FarField FarField { get; private set; }

        public Features Features { get; private set; }

        public MapOverlay MapOverlay { get; private set; }

        public WideMap WideMap { get; private set; }

        private readonly string vanillaDir;
        public string Directory { get; }
        private string RuntimeDirectory => Path.Combine(Directory, RuntimeFolder);
        private readonly VanillaTerrain baselines;
        private readonly ConcurrentDictionary<int, bool> decided = new ConcurrentDictionary<int, bool>();
        private readonly HashSet<int> loaded = new HashSet<int>();
        private volatile bool terrainPatcherDone;
        private readonly object cleanUp = new object();
        private readonly object[] stripes = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();
        private readonly ManualResetEvent modelReady = new ManualResetEvent(false);
        private volatile BatchBuilder builder;
        private volatile bool complete, failed;

        public bool IsComplete => complete;

        public static int Key(int x, int y, int z) =>
            ((x - World.FarMinBatch) * Rows + y) * World.FarBatches + z - World.FarMinBatch;

        private static (int x, int y, int z) Unkey(int key) =>
            (key / (Rows * World.FarBatches) + World.FarMinBatch, key / World.FarBatches % Rows, key % World.FarBatches + World.FarMinBatch);

        private TerrainCache(string vanillaDir, string directory)
        {
            this.vanillaDir = vanillaDir;
            Directory = directory;
            baselines = new VanillaTerrain(vanillaDir);
        }

        public static TerrainCache Open(string vanillaDir, string cacheRoot)
        {
            string dir = Path.Combine(cacheRoot, $"v{Field.Version}p{Palette.Version}t{Layout}-{Fingerprint(vanillaDir)}");
            bool older = System.IO.Directory.Exists(cacheRoot) && System.IO.Directory.GetDirectories(cacheRoot)
                .Any(d => !string.Equals(Path.GetFullPath(d), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase));
            System.IO.Directory.CreateDirectory(dir);
            var cache = new TerrainCache(vanillaDir, dir);
            foreach (string file in System.IO.Directory.GetFiles(dir, "*.optoctreepatch"))
            {
                if (TerrainPatches.TryParseBatchFile(Path.GetFileName(file), out int x, out int y, out int z)
                    && y >= 0 && y < Rows && World.InFarRange(x, z))
                {
                    cache.decided[Key(x, y, z)] = true;
                    if (!older)
                    {
                        cache.loaded.Add(Key(x, y, z));
                    }
                }
            }
            if (System.IO.Directory.Exists(cache.RuntimeDirectory))
            {
                System.IO.Directory.Delete(cache.RuntimeDirectory, true);
            }
            System.IO.Directory.CreateDirectory(cache.RuntimeDirectory);
            cache.complete = File.Exists(Path.Combine(dir, Marker));
            cache.FarField = cache.ReadProduct(FarFieldFile, FarField.Read);
            cache.Features = cache.ReadProduct(FeaturesFile, Features.Read);
            cache.MapOverlay = cache.ReadProduct(MapOverlayFile, MapOverlay.Read);
            cache.WideMap = cache.ReadProduct(WideMapFile, WideMap.Read);
            return cache;
        }

        private T ReadProduct<T>(string name, Func<byte[], T> read) where T : class
        {
            string path = Path.Combine(Directory, name);
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                return read(File.ReadAllBytes(path));
            }
            catch (Exception)
            {
                File.Delete(path);
                return null;
            }
        }

        private void WriteProduct(string name, byte[] bytes) => WriteAtomic(Path.Combine(Directory, name), bytes);

        private static void WriteAtomic(string path, byte[] bytes)
        {
            File.WriteAllBytes(path + ".tmp", bytes);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(path + ".tmp", path);
        }

        public void Fill(int threads, ThreadPriority priority)
        {
            IslandModel model;
            Field field;
            FarDistance distance;
            try
            {
                var inputs = EdgeInputs.Measure(vanillaDir, Array.Empty<float>());
                var vanilla = new VanillaTerrain(vanillaDir);
                model = IslandModel.Build(inputs.LowestRock, vanilla);
                inputs = null;
                vanilla.Clear();
                field = new Field(model);
                distance = ReadProduct(FarDistanceFile, FarDistance.Read);
                if (distance == null)
                {
                    distance = FarDistance.Build(model);
                    WriteProduct(FarDistanceFile, distance.Write());
                }
                builder = new BatchBuilder(field, vanilla, distance);
            }
            catch (Exception)
            {
                failed = true;
                return;
            }
            finally
            {
                modelReady.Set();
            }

            if (FarField == null)
            {
                var far = FarField.Build(field, model, distance, threads);
                WriteProduct(FarFieldFile, far.Write());
                FarField = far;
            }
            for (int i = 0; i < Math.Max(1, threads); i++)
            {
                new Thread(PrefetchLoop) { IsBackground = true, Priority = priority, Name = "RealisticCraterEdge prefetch" }.Start();
            }
            if (Features == null)
            {
                var features = Features.Build(field, FarField, distance);
                WriteProduct(FeaturesFile, features.Write());
                Features = features;
            }
            if (MapOverlay == null)
            {
                var overlay = MapOverlay.Build(field, threads);
                WriteProduct(MapOverlayFile, overlay.Write());
                MapOverlay = overlay;
            }
            if (WideMap == null)
            {
                var wide = WideMap.Build(field, FarField, threads);
                WriteProduct(WideMapFile, wide.Write());
                WideMap = wide;
            }
            if (complete)
            {
                CleanUp();
                return;
            }

            int maxRow = Math.Min(builder.MaxRow(), HighestChangedRow);
            var queue = new ConcurrentQueue<int>();
            for (int y = maxRow; y >= 0; y--)
            {
                for (int x = 0; x < World.BatchesX; x++)
                {
                    for (int z = 0; z < World.BatchesZ; z++)
                    {
                        queue.Enqueue(Key(x, y, z));
                    }
                }
            }
            var workers = Enumerable.Range(0, Math.Max(1, threads)).Select(_ => new Thread(() =>
            {
                while (queue.TryDequeue(out int key))
                {
                    var (x, y, z) = Unkey(key);
                    try
                    {
                        Ensure(x, y, z);
                    }
                    catch (Exception)
                    {
                        failed = true;
                    }
                }
            }) { IsBackground = true, Priority = priority, Name = "RealisticCraterEdge fill" }).ToList();
            workers.ForEach(t => t.Start());
            workers.ForEach(t => t.Join());
            if (failed)
            {
                return;
            }

            File.WriteAllText(Path.Combine(Directory, Marker), $"generator v{Field.Version}, palette v{Palette.Version}\n");
            complete = true;
            CleanUp();
        }

        public void TerrainPatcherDone()
        {
            terrainPatcherDone = true;
            CleanUp();
        }

        private void CleanUp()
        {
            if (!complete || !terrainPatcherDone)
            {
                return;
            }
            lock (cleanUp)
            {
                string root = Path.GetDirectoryName(Directory);
                foreach (string old in System.IO.Directory.GetDirectories(root))
                {
                    if (!string.Equals(Path.GetFullPath(old), Path.GetFullPath(Directory), StringComparison.OrdinalIgnoreCase))
                    {
                        System.IO.Directory.Delete(old, true);
                    }
                }
            }
        }

        public string PathFor(int x, int y, int z, TimeSpan modelWait)
        {
            if (y < 0 || y > HighestChangedRow || !World.InFarRange(x, z))
            {
                return null;
            }
            int key = Key(x, y, z);
            if (loaded.Contains(key))
            {
                return null;
            }
            if (decided.TryGetValue(key, out bool generated))
            {
                return generated ? RuntimePath(x, y, z) : null;
            }
            if ((complete && World.InBox(x, z)) || !modelReady.WaitOne(modelWait) || failed)
            {
                return null;
            }
            return Ensure(x, y, z) ? RuntimePath(x, y, z) : null;
        }

        private string PatchPath(int x, int y, int z) => Path.Combine(Directory, TerrainPatches.BatchFile(x, y, z));

        private string RuntimeFile(int x, int y, int z) => Path.Combine(RuntimeDirectory, Batch.FileName(x, y, z));

        private string RuntimePath(int x, int y, int z)
        {
            string path = RuntimeFile(x, y, z);
            if (File.Exists(path))
            {
                return path;
            }
            lock (stripes[Key(x, y, z) & 63])
            {
                if (!File.Exists(path))
                {
                    var baseline = World.InBox(x, z) ? baselines.Load(x, y, z) : null;
                    WriteAtomic(path, TerrainPatches.Apply(File.ReadAllBytes(PatchPath(x, y, z)), baseline).Write());
                }
            }
            return path;
        }

        private bool Ensure(int x, int y, int z)
        {
            int key = Key(x, y, z);
            if (decided.TryGetValue(key, out bool generated))
            {
                return generated;
            }
            lock (stripes[key & 63])
            {
                if (decided.TryGetValue(key, out generated))
                {
                    return generated;
                }
                var b = builder;
                if (b == null)
                {
                    return false;
                }
                var batch = b.Build(x, y, z, out Batch baseline);
                if (batch != null)
                {
                    WriteAtomic(PatchPath(x, y, z), TerrainPatches.ForBatch(batch, baseline));
                    WriteAtomic(RuntimeFile(x, y, z), batch.Write());
                }
                decided[key] = batch != null;
                return batch != null;
            }
        }

        private sealed class PrefetchJob
        {
            public readonly int X, Y, Z;
            public int Cursor;
            private int[] keys;

            public PrefetchJob(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public int[] Keys(FarField far)
            {
                lock (this)
                {
                    return keys ?? (keys = Plan(far));
                }
            }

            private int[] Plan(FarField far)
            {
                var wanted = new List<(int key, int order)>();
                for (int dx = -PrefetchRadius; dx <= PrefetchRadius; dx++)
                {
                    for (int dz = -PrefetchRadius; dz <= PrefetchRadius; dz++)
                    {
                        int bx = X + dx, bz = Z + dz;
                        if (World.InBox(bx, bz) || !World.InFarRange(bx, bz))
                        {
                            continue;
                        }
                        double low = double.MaxValue, high = double.MinValue;
                        double x0 = World.MinX + bx * Batch.Size, z0 = World.MinZ + bz * Batch.Size;
                        for (int i = 0; i <= 8; i++)
                        {
                            for (int k = 0; k <= 8; k++)
                            {
                                double h = far.HeightAt(x0 + i * Batch.Size / 8.0, z0 + k * Batch.Size / 8.0, false);
                                if (!double.IsNaN(h))
                                {
                                    low = Math.Min(low, h);
                                    high = Math.Max(high, h);
                                }
                            }
                        }
                        if (low > high)
                        {
                            continue;
                        }
                        int r0 = Math.Max(Y - PrefetchRows, (int)Math.Floor((low - SurfaceMargin - World.MinY) / Batch.Size));
                        int r1 = Math.Min(Y + PrefetchRows, (int)Math.Floor((high + SurfaceMargin - World.MinY) / Batch.Size));
                        for (int row = Math.Max(0, r0); row <= Math.Min(HighestChangedRow, r1); row++)
                        {
                            wanted.Add((Key(bx, row, bz), Math.Max(Math.Abs(dx), Math.Abs(dz)) * 64 + Math.Abs(row - Y)));
                        }
                    }
                }
                return wanted.OrderBy(w => w.order).ThenBy(w => w.key).Select(w => w.key).ToArray();
            }
        }

        private volatile PrefetchJob prefetchJob;
        private readonly ManualResetEventSlim prefetchWake = new ManualResetEventSlim(false);

        public void Prefetch(double x, double y, double z)
        {
            int bx = FloorDiv((int)Math.Floor(x) - World.MinX, Batch.Size);
            int by = FloorDiv((int)Math.Floor(y) - World.MinY, Batch.Size);
            int bz = FloorDiv((int)Math.Floor(z) - World.MinZ, Batch.Size);
            var job = prefetchJob;
            if (job != null && job.X == bx && job.Y == by && job.Z == bz)
            {
                return;
            }
            prefetchJob = new PrefetchJob(bx, by, bz);
            prefetchWake.Set();
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        private void PrefetchLoop()
        {
            while (true)
            {
                var job = prefetchJob;
                var far = FarField;
                if (job != null && far != null && !failed)
                {
                    int[] keys = job.Keys(far);
                    int i = Interlocked.Increment(ref job.Cursor) - 1;
                    if (i < keys.Length)
                    {
                        var (x, y, z) = Unkey(keys[i]);
                        try
                        {
                            Ensure(x, y, z);
                        }
                        catch (Exception)
                        {
                        }
                        continue;
                    }
                }
                prefetchWake.Reset();
                if (prefetchJob == job)
                {
                    prefetchWake.Wait();
                }
            }
        }

        public static string Fingerprint(string vanillaDir)
        {
            var files = System.IO.Directory.GetFiles(vanillaDir, "compiled-batch-*.optoctrees")
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.Name, StringComparer.Ordinal);
            using (var sha = SHA256.Create())
            {
                var text = new StringBuilder();
                foreach (var f in files)
                {
                    text.Append(f.Name).Append(':').Append(f.Length).Append('\n');
                }
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                return BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
