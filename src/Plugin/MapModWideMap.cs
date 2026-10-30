using System;
using System.Collections;
using System.Reflection;
using CraterEdge.Generation;
using HarmonyLib;
using UnityEngine;

namespace RealisticCraterEdge
{
    internal static class MapModWideMap
    {
        private const string MapId = "crateredge_wide", FogId = "crateredge", MapName = "Crater Edge (wide)";
        private const float Edge = 1940;
        private static readonly int WideSize = (int)(2 * WideMap.Half);
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static FieldInfo worldSize, mapSize, mapScale, scrollView, lastDefaultMap;
        private static FieldInfo mapIdField, fogmapIdField, nameField, mainField, depthField, paletteField, fogmapField, fogIdField;
        private static PropertyInfo instance, currentMap, assets;
        private static MethodInfo switchMap, updateIcons, alignToPlayer, mapIsOpened, allMaps, createFogmap;
        private static Type mapType;
        private static int narrowWorld, narrowMap;
        private static object wide;
        private static Texture2D wideMain, wideDepth;
        private static bool patched, switchedHere;

        internal static bool Missing => patched && wide == null;

        public static void Patch(Harmony harmony)
        {
            try
            {
                Assembly assembly = null;
                foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (loaded.GetName().Name == "SubnauticaMap")
                    {
                        assembly = loaded;
                        break;
                    }
                }
                Type controller = assembly?.GetType("SubnauticaMap.Controller");
                mapType = assembly?.GetType("SubnauticaMap.Map");
                Type fogmap = assembly?.GetType("SubnauticaMap.Fogmap");
                if (controller == null || mapType == null || fogmap == null)
                {
                    return;
                }
                worldSize = controller.GetField("worldSize", Any);
                mapSize = controller.GetField("mapSize", Any);
                mapScale = controller.GetField("mapScale", Any);
                scrollView = controller.GetField("scrollView", Any);
                lastDefaultMap = controller.GetField("lastDefaultMap", Any);
                instance = controller.GetProperty("Instance", Any);
                currentMap = controller.GetProperty("CurrentMap", Any);
                assets = controller.GetProperty("Assets", Any);
                switchMap = controller.GetMethod("SwitchMap", Any);
                updateIcons = controller.GetMethod("UpdateIcons", Any);
                alignToPlayer = controller.GetMethod("AlignToPlayer", Any);
                mapIsOpened = controller.GetMethod("MapIsOpened", Any);
                MethodInfo paintFog = controller.GetMethod("PaintFog", Any);
                MethodInfo onVisible = controller.GetMethod("UpdateOnVisible", Any);
                MethodInfo loadMaps = mapType.GetMethod("LoadMaps", Any);
                allMaps = mapType.GetMethod("All", Any);
                createFogmap = fogmap.GetMethod("Create", Any);
                mapIdField = mapType.GetField("mapId", Any);
                fogmapIdField = mapType.GetField("fogmapId", Any);
                nameField = mapType.GetField("name", Any);
                mainField = mapType.GetField("main", Any);
                depthField = mapType.GetField("depth", Any);
                paletteField = mapType.GetField("palette", Any);
                fogmapField = mapType.GetField("fogmap", Any);
                fogIdField = fogmap.GetField("id", Any);
                object[] needed =
                {
                    worldSize, mapSize, mapScale, scrollView, lastDefaultMap, instance, currentMap, assets, switchMap, updateIcons,
                    alignToPlayer, mapIsOpened, paintFog, onVisible, loadMaps, allMaps, createFogmap, mapIdField, fogmapIdField,
                    nameField, mainField, depthField, paletteField, fogmapField, fogIdField,
                };
                if (Array.IndexOf(needed, null) >= 0 || worldSize.FieldType != typeof(int) || mapSize.FieldType != typeof(int)
                    || mapScale.FieldType != typeof(float))
                {
                    return;
                }
                harmony.Patch(loadMaps, postfix: new HarmonyMethod(typeof(MapModWideMap), nameof(AddMap)));
                harmony.Patch(switchMap, postfix: new HarmonyMethod(typeof(MapModWideMap), nameof(OnSwitch)));
                harmony.Patch(paintFog, prefix: new HarmonyMethod(typeof(MapModWideMap), nameof(BeforePaint)),
                    postfix: new HarmonyMethod(typeof(MapModWideMap), nameof(AfterPaint)));
                harmony.Patch(onVisible, postfix: new HarmonyMethod(typeof(MapModWideMap), nameof(OnVisible)));
                patched = true;
            }
            catch (Exception)
            {
            }
        }

        private static void AddMap()
        {
            wide = null;
            try
            {
                var data = Terrain.Cache?.WideMap;
                object controller = instance.GetValue(null, null);
                if (data == null || controller == null)
                {
                    return;
                }
                var bundle = assets.GetValue(controller, null) as AssetBundle;
                if (wideMain == null && bundle != null)
                {
                    Build(data, bundle.LoadAsset<Texture2D>("world_base"), bundle.LoadAsset<Texture2D>("world_depth"));
                }
                if (wideMain == null)
                {
                    return;
                }
                object map = Activator.CreateInstance(mapType);
                mapIdField.SetValue(map, MapId);
                fogmapIdField.SetValue(map, FogId);
                nameField.SetValue(map, MapName);
                mainField.SetValue(map, wideMain);
                depthField.SetValue(map, wideDepth);
                paletteField.SetValue(map, bundle.LoadAsset<Texture2D>("world_palette"));
                fogmapField.SetValue(map, createFogmap.Invoke(null, new[] { map }));
                ((IList)allMaps.Invoke(null, null)).Add(map);
                wide = map;
            }
            catch (Exception)
            {
                wide = null;
            }
        }

        private static void Build(WideMap data, Texture2D island, Texture2D islandDepth)
        {
            if (island == null || islandDepth == null)
            {
                return;
            }
            const int size = WideMap.Size;
            int span = (int)Math.Round(4000 / WideMap.Cell), start = (size - span) / 2;
            Color32[] relief = ReadBack(island, span, false), depth = ReadBack(islandDepth, span, true);
            byte[] greys = data.Greys();
            byte[] main = new byte[size * size * 3], depthBytes = new byte[size * size];
            for (int row = 0; row < size; row++)
            {
                int texRow = size - 1 - row;
                for (int i = 0; i < size; i++)
                {
                    int at = texRow * size + i;
                    byte g = greys[row * size + i];
                    if (g != 0)
                    {
                        main[at * 3] = main[at * 3 + 1] = main[at * 3 + 2] = g;
                        continue;
                    }
                    int li = i - start, lr = texRow - start;
                    if (li < 0 || li >= span || lr < 0 || lr >= span)
                    {
                        continue;
                    }
                    Color32 c = relief[lr * span + li];
                    main[at * 3] = c.r;
                    main[at * 3 + 1] = c.g;
                    main[at * 3 + 2] = c.b;
                    depthBytes[at] = depth[lr * span + li].r;
                }
            }
            wideMain = new Texture2D(size, size, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, name = "RealisticCraterEdge wide map" };
            wideMain.LoadRawTextureData(main);
            wideMain.Compress(false);
            wideMain.Apply(false, true);
            wideDepth = new Texture2D(size, size, TextureFormat.R8, false, true) { wrapMode = TextureWrapMode.Clamp, name = "RealisticCraterEdge wide depth" };
            wideDepth.LoadRawTextureData(depthBytes);
            wideDepth.Apply(false, true);
        }

        private static Color32[] ReadBack(Texture2D texture, int span, bool linear)
        {
            var rt = RenderTexture.GetTemporary(span, span, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            var read = new Texture2D(span, span, TextureFormat.RGBA32, false, linear);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, span, span), 0, 0);
                read.Apply(false);
                return read.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(read);
            }
        }

        private static void OnSwitch(object __instance, object map)
        {
            try
            {
                Remember(__instance);
                int size = map != null && map == wide ? WideSize : narrowWorld;
                worldSize.SetValue(__instance, size);
                if (scrollView.GetValue(__instance) is Component scroll)
                {
                    var content = scroll.GetType().GetProperty("content")?.GetValue(scroll, null) as RectTransform;
                    if (content != null && content.sizeDelta.x > 0)
                    {
                        mapScale.SetValue(__instance, size / content.sizeDelta.x);
                    }
                }
                if ((bool)mapIsOpened.Invoke(__instance, null))
                {
                    updateIcons.Invoke(__instance, null);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void BeforePaint(object __instance, object fogmap, out int[] __state)
        {
            __state = null;
            try
            {
                Remember(__instance);
                __state = new[] { (int)worldSize.GetValue(__instance), (int)mapSize.GetValue(__instance) };
                bool ours = fogmap != null && (string)fogIdField.GetValue(fogmap) == FogId;
                worldSize.SetValue(__instance, ours ? WideSize : narrowWorld);
                mapSize.SetValue(__instance, ours ? WideSize * narrowMap / narrowWorld : narrowMap);
            }
            catch (Exception)
            {
                __state = null;
            }
        }

        private static void AfterPaint(object __instance, int[] __state)
        {
            if (__state == null)
            {
                return;
            }
            try
            {
                worldSize.SetValue(__instance, __state[0]);
                mapSize.SetValue(__instance, __state[1]);
            }
            catch (Exception)
            {
            }
        }

        private static void OnVisible(object __instance)
        {
            try
            {
                if (wide == null || Player.main == null)
                {
                    return;
                }
                Vector3 p = Player.main.transform.position;
                bool beyond = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) > Edge;
                object current = currentMap.GetValue(__instance, null);
                if (beyond && current != wide)
                {
                    switchMap.Invoke(__instance, new[] { wide });
                    alignToPlayer.Invoke(__instance, null);
                    switchedHere = true;
                }
                else if (!beyond && current == wide && switchedHere)
                {
                    object back = lastDefaultMap.GetValue(__instance);
                    if (back != null)
                    {
                        switchMap.Invoke(__instance, new[] { back });
                        alignToPlayer.Invoke(__instance, null);
                    }
                    switchedHere = false;
                }
            }
            catch (Exception)
            {
            }
        }

        private static void Remember(object controller)
        {
            if (narrowWorld != 0)
            {
                return;
            }
            narrowWorld = (int)worldSize.GetValue(controller);
            narrowMap = (int)mapSize.GetValue(controller);
        }
    }
}
