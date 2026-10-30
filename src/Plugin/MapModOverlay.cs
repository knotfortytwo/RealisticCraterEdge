using System;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Bootstrap;
using CraterEdge.Generation;
using UnityEngine;

namespace RealisticCraterEdge
{
    internal static class MapModOverlay
    {
        internal const string MapModGuid = "sn.subnauticamap.mod";
        private const string Folder = "RealisticCraterEdge", Image = "crater_edge.png", Stamp = "version.txt";
        private static readonly string[] MapIds = { "default_world", "default_biome" };
        private static bool done;

        public static void Update()
        {
            if (done)
            {
                return;
            }
            var cache = Terrain.Cache;
            if (cache?.MapOverlay == null || cache.WideMap == null)
            {
                return;
            }
            done = true;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(MapModGuid, out var info) || string.IsNullOrEmpty(info.Location))
                {
                    return;
                }
                string dir = Path.Combine(Path.Combine(Path.GetDirectoryName(info.Location), "maps"), Folder);
                string stamp = Path.GetFileName(cache.Directory), stampPath = Path.Combine(dir, Stamp);
                bool current = File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp && File.Exists(Path.Combine(dir, Image));
                if (!current)
                {
                    Directory.CreateDirectory(dir);
                    WriteAtomic(Path.Combine(dir, Image), Png(cache.MapOverlay));
                    foreach (string id in MapIds)
                    {
                        WriteAtomic(Path.Combine(dir, $"crater_edge_{id}.json"),
                            Encoding.UTF8.GetBytes($"{{\n\t\"mapId\": \"{id}\",\n\t\"filepath_overlay\": \"{Image}\"\n}}\n"));
                    }
                    File.WriteAllText(stampPath, stamp);
                }
                if (!current || MapModWideMap.Missing)
                {
                    Reload();
                }
            }
            catch (Exception)
            {
            }
        }

        private static byte[] Png(MapOverlay overlay)
        {
            const int size = MapOverlay.Size, stride = size * 4;
            byte[] rgba = overlay.Rgba(), flipped = new byte[rgba.Length];
            for (int row = 0; row < size; row++)
            {
                Buffer.BlockCopy(rgba, row * stride, flipped, (size - 1 - row) * stride, stride);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                texture.LoadRawTextureData(flipped);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            File.WriteAllBytes(path + ".tmp", bytes);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(path + ".tmp", path);
        }

        private static void Reload()
        {
            Type controller = Type.GetType("SubnauticaMap.Controller, SubnauticaMap");
            object instance = controller?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
            if (instance != null)
            {
                controller.GetMethod("ReloadMaps", BindingFlags.Public | BindingFlags.Instance)?.Invoke(instance, null);
            }
        }
    }
}
