using System;
using System.IO;
using System.Threading;
using BepInEx;
using CraterEdge.Generation;
using HarmonyLib;
using UnityEngine;
using ThreadPriority = System.Threading.ThreadPriority;

namespace RealisticCraterEdge
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("com.snmodding.nautilus")]
    [BepInDependency(TerrainPatcherGuid, "1.3.1")]
    [BepInDependency(MapModOverlay.MapModGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.snmods.realisticcrateredge";
        public const string Name = "Realistic Crater Edge";
        public const string Version = "0.3.0";
        public const string TerrainPatcherGuid = "Esper89.TerrainPatcher";

        internal static DepositSave Deposits;

        internal static PatrolSave Patrols;

        private void Awake()
        {
            Terrain.EdgesPatched = EnsureEdgesPatch();
            Deposits = Nautilus.Handlers.SaveDataHandler.RegisterSaveDataCache<DepositSave>();
            Patrols = Nautilus.Handlers.SaveDataHandler.RegisterSaveDataCache<PatrolSave>();
            string data = Directory.GetParent(Paths.ManagedPath).FullName;
            string vanilla = Path.Combine(data, "StreamingAssets", "SNUnmanagedData", "Build18", "CompiledOctreesCache");
            string cacheRoot = Path.Combine(Paths.CachePath, "RealisticCraterEdge");
            var worker = new Thread(() => Terrain.Prepare(vanilla, cacheRoot))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "RealisticCraterEdge",
            };
            worker.Start();
            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(MapModOverlay.MapModGuid))
            {
                MapModWideMap.Patch(harmony);
            }
        }

        private bool EnsureEdgesPatch()
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Info.Location), TerrainPatches.EdgesFile);
                byte[] bytes = TerrainPatches.ZeroOctrees(TerrainPatches.EdgeBatches());
                if (File.Exists(path) && System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(path), bytes))
                {
                    return true;
                }
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception)
            {
            }
            return false;
        }

        private FarFieldRenderer farField;
        private FeatureSpawner featureSpawner;
        private GhostPatrols ghostPatrols;

        private void Update()
        {
            MapModOverlay.Update();
            if (LargeWorld.main == null || Player.main == null || MainCamera.camera == null)
            {
                return;
            }
            Vector3 eye = MainCamera.camera.transform.position;
            Terrain.Cache?.Prefetch(eye.x, eye.y, eye.z);
            if (farField == null && Terrain.Cache?.FarField != null && LargeWorld.main.land != null && LargeWorld.main.land.types != null)
            {
                farField = new GameObject("RealisticCraterEdge far field").AddComponent<FarFieldRenderer>();
                farField.Init(Terrain.Cache.FarField, LargeWorld.main.land);
            }
            if (featureSpawner == null && Terrain.Cache?.Features != null)
            {
                featureSpawner = new GameObject("RealisticCraterEdge features").AddComponent<FeatureSpawner>();
                featureSpawner.Init(Terrain.Cache.Features);
            }
            if (ghostPatrols == null && Terrain.Cache?.Features != null)
            {
                ghostPatrols = new GameObject("RealisticCraterEdge ghost territories").AddComponent<GhostPatrols>();
                ghostPatrols.Init(Terrain.Cache.Features);
            }
        }
    }

    internal static class Terrain
    {
        private const int FillThreads = 2;

        public static bool EdgesPatched = true;
        private static readonly TimeSpan ModelWait = TimeSpan.FromSeconds(90);

        private static readonly ManualResetEvent opened = new ManualResetEvent(false);
        private static TerrainCache cache;

        public static TerrainCache Cache => opened.WaitOne(0) ? cache : null;

        public static void Prepare(string vanillaDir, string cacheRoot)
        {
            try
            {
                cache = TerrainCache.Open(vanillaDir, cacheRoot);
            }
            catch (Exception)
            {
                cache = null;
            }
            finally
            {
                opened.Set();
            }
            try
            {
                cache?.Fill(FillThreads, ThreadPriority.BelowNormal);
            }
            catch (Exception)
            {
            }
        }

        private static int terrainPatcherDone;

        public static string PathFor(Int3 batch)
        {
            if (Interlocked.Exchange(ref terrainPatcherDone, 1) == 0)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    opened.WaitOne();
                    try
                    {
                        cache?.TerrainPatcherDone();
                    }
                    catch (Exception)
                    {
                    }
                });
            }
            if (batch.y < 0 || batch.y > TerrainCache.HighestChangedRow
                || !EdgesPatched && TerrainPatches.IsEdge(batch.x, batch.y, batch.z))
            {
                return null;
            }
            opened.WaitOne();
            try
            {
                return cache?.PathFor(batch.x, batch.y, batch.z, ModelWait);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    [HarmonyPatch(typeof(WorldStreaming.BatchOctreesStreamer), nameof(WorldStreaming.BatchOctreesStreamer.GetPath))]
    internal static class BatchPathPatch
    {
        private static void Postfix(Int3 batchId, ref string __result)
        {
            string generated = Terrain.PathFor(batchId);
            if (generated != null)
            {
                __result = generated;
            }
        }
    }
}
