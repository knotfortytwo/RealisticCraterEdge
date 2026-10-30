using System.Collections.Generic;
using System.Threading.Tasks;
using CraterEdge.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealisticCraterEdge
{
    internal sealed class FarFieldRenderer : MonoBehaviour
    {
        private const int TileSize = 128;
        private const float Range = 768;
        private const float Sink = 4;
        private const float UpdateInterval = 0.25f;
        private const float MinWeight = 0.01f;

        private struct Detail
        {
            public int Step;
            public float Skirt;
            public double Blend;
            public bool Bumps;
        }

        private static readonly Detail Coarse = new Detail { Step = 8, Skirt = 16, Blend = 24, Bumps = false };

        private sealed class Tile
        {
            public GameObject Object;
            public readonly List<GameObject> Layers = new List<GameObject>();
            public readonly List<Mesh> Meshes = new List<Mesh>();
        }

        private sealed class TileData
        {
            public Vector3[] Vertices, Normals;
            public Vector4[] Tangents;
            public float[] Weights;
            public List<int> Triangles;
        }

        private FarField data;
        private Voxeland land;
        private int renderLayer;
        private readonly Dictionary<long, Tile> tiles = new Dictionary<long, Tile>();
        private float nextUpdate;
        private Task<TileData> pending;
        private long pendingKey;

        public void Init(FarField farField, Voxeland land)
        {
            data = farField;
            this.land = land;
            var chunk = FindObjectOfType<WorldStreaming.ClipmapChunk>();
            renderLayer = chunk != null ? chunk.gameObject.layer : 0;
        }

        private void Update()
        {
            if (data == null || land == null || land.types == null || Time.time < nextUpdate)
            {
                return;
            }
            nextUpdate = Time.time + UpdateInterval;
            Camera cam = MainCamera.camera;
            if (cam == null)
            {
                return;
            }
            Vector3 eye = cam.transform.position;
            var wanted = new Dictionary<long, float>();
            int reach = Mathf.CeilToInt(Range / TileSize) + 1;
            int ctx = Mathf.FloorToInt(eye.x / TileSize), ctz = Mathf.FloorToInt(eye.z / TileSize);
            for (int tx = ctx - reach; tx <= ctx + reach; tx++)
            {
                for (int tz = ctz - reach; tz <= ctz + reach; tz++)
                {
                    if (!IsOutsideBox(tx, tz) || Mathf.Abs(tx * TileSize) > FarField.Extent || Mathf.Abs(tz * TileSize) > FarField.Extent)
                    {
                        continue;
                    }
                    float distance = DistanceToTile(eye, tx, tz);
                    if (distance > Range)
                    {
                        continue;
                    }
                    wanted[Key(tx, tz)] = distance;
                }
            }

            var drop = new List<long>();
            foreach (var kv in tiles)
            {
                if (!wanted.ContainsKey(kv.Key))
                {
                    drop.Add(kv.Key);
                }
            }
            foreach (long key in drop)
            {
                ClearLayers(tiles[key]);
                Destroy(tiles[key].Object);
                tiles.Remove(key);
            }

            if (pending != null && pending.IsCompleted)
            {
                if (pending.Status == TaskStatus.RanToCompletion && wanted.ContainsKey(pendingKey))
                {
                    Apply(pendingKey, pending.Result);
                }
                pending = null;
            }

            long build = 0;
            float buildDistance = float.MaxValue;
            foreach (var kv in wanted)
            {
                if (!tiles.ContainsKey(kv.Key) && kv.Value < buildDistance)
                {
                    build = kv.Key;
                    buildDistance = kv.Value;
                }
            }
            if (pending == null && buildDistance < float.MaxValue)
            {
                BeginTile(build);
            }
        }

        private void OnDestroy()
        {
            foreach (var tile in tiles.Values)
            {
                foreach (var mesh in tile.Meshes)
                {
                    Destroy(mesh);
                }
            }
            tiles.Clear();
        }

        private static long Key(int tx, int tz) => ((long)tx << 32) ^ (uint)tz;

        private static bool IsOutsideBox(int tx, int tz)
        {
            int x0 = tx * TileSize, z0 = tz * TileSize;
            return x0 >= World.MinX + World.SizeX || x0 + TileSize <= World.MinX
                || z0 >= World.MinZ + World.SizeZ || z0 + TileSize <= World.MinZ;
        }

        private float DistanceToTile(Vector3 eye, int tx, int tz)
        {
            float x0 = tx * TileSize, z0 = tz * TileSize;
            float dx = Mathf.Max(x0 - eye.x, 0, eye.x - (x0 + TileSize));
            float dz = Mathf.Max(z0 - eye.z, 0, eye.z - (z0 + TileSize));
            float h = (float)data.HeightAt(x0 + TileSize / 2f, z0 + TileSize / 2f);
            float dy = Mathf.Max(0, eye.y - h - 64);
            return Mathf.Sqrt(dx * dx + dz * dz + dy * dy);
        }

        private VoxelandBlockType TypeFor(byte id)
        {
            VoxelandBlockType[] types = land.types;
            VoxelandBlockType type = id < types.Length ? types[id] : null;
            return type != null && type.opaqueMaterial != null ? type : VoxelandChunk.GetFallbackBlockType(types);
        }

        private void ClearLayers(Tile tile)
        {
            foreach (var layer in tile.Layers)
            {
                Destroy(layer);
            }
            foreach (var mesh in tile.Meshes)
            {
                Destroy(mesh);
            }
            tile.Layers.Clear();
            tile.Meshes.Clear();
        }

        private void BeginTile(long key)
        {
            int tx = (int)(key >> 32), tz = (int)key;
            FarField far = data;
            pendingKey = key;
            pending = Task.Run(() => Compute(far, tx * TileSize, tz * TileSize, Coarse));
        }

        private static TileData Compute(FarField data, float x0, float z0, Detail detail)
        {
            int n = TileSize / detail.Step;
            int w = n + 3;
            var h = new float[w * w];
            for (int i = 0; i < w; i++)
            {
                for (int k = 0; k < w; k++)
                {
                    h[i * w + k] = (float)data.HeightAt(x0 + (i - 1) * detail.Step, z0 + (k - 1) * detail.Step, detail.Bumps) - Sink;
                }
            }
            int grid = (n + 1) * (n + 1), skirt = 4 * (n + 1), slots = Palette.Types.Length;
            var vertices = new Vector3[grid + skirt];
            var normals = new Vector3[grid + skirt];
            var tangents = new Vector4[grid + skirt];
            var weights = new float[(grid + skirt) * slots];
            var vertexWeights = new double[slots];
            for (int i = 0; i <= n; i++)
            {
                for (int k = 0; k <= n; k++)
                {
                    float y = h[(i + 1) * w + k + 1];
                    float dx = (h[(i + 2) * w + k + 1] - h[i * w + k + 1]) / (2f * detail.Step);
                    float dz = (h[(i + 1) * w + k + 2] - h[(i + 1) * w + k]) / (2f * detail.Step);
                    int v = i * (n + 1) + k;
                    vertices[v] = new Vector3(i * detail.Step, y, k * detail.Step);
                    Vector3 normal = new Vector3(-dx, 1, -dz).normalized;
                    normals[v] = normal;
                    Vector3 tangent = Vector3.Cross(normal, Vector3.Cross(normal, Vector3.right)).normalized;
                    tangents[v] = new Vector4(tangent.x, tangent.y, tangent.z, -1f);
                    Palette.Weigh(x0 + i * detail.Step, y + Sink, z0 + k * detail.Step, double.PositiveInfinity, double.PositiveInfinity,
                        detail.Blend, vertexWeights);
                    for (int slot = 0; slot < slots; slot++)
                    {
                        weights[v * slots + slot] = (float)vertexWeights[slot];
                    }
                }
            }
            var triangles = new List<int>(n * n * 6 + skirt * 6);
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    int v00 = i * (n + 1) + k, v10 = v00 + n + 1, v01 = v00 + 1, v11 = v10 + 1;
                    triangles.Add(v00); triangles.Add(v01); triangles.Add(v10);
                    triangles.Add(v10); triangles.Add(v01); triangles.Add(v11);
                }
            }
            int s = grid;
            var arrays = (vertices, normals, tangents, weights);
            AddSkirt(arrays, triangles, n, ref s, detail.Skirt, i => i * (n + 1), true);
            AddSkirt(arrays, triangles, n, ref s, detail.Skirt, i => i * (n + 1) + n, false);
            AddSkirt(arrays, triangles, n, ref s, detail.Skirt, k => k, false);
            AddSkirt(arrays, triangles, n, ref s, detail.Skirt, k => n * (n + 1) + k, true);
            return new TileData { Vertices = vertices, Normals = normals, Tangents = tangents, Weights = weights, Triangles = triangles };
        }

        private void Apply(long key, TileData tileData)
        {
            int tx = (int)(key >> 32), tz = (int)key;
            if (!tiles.TryGetValue(key, out Tile tile))
            {
                tile = new Tile { Object = new GameObject($"far field {tx},{tz}") { layer = renderLayer } };
                tile.Object.transform.SetParent(transform, false);
                tile.Object.transform.position = new Vector3(tx * TileSize, 0, tz * TileSize);
                tiles[key] = tile;
            }
            ClearLayers(tile);
            Vector3[] vertices = tileData.Vertices;
            float[] weights = tileData.Weights;
            List<int> triangles = tileData.Triangles;
            int slots = Palette.Types.Length;

            var used = new List<int>();
            for (int slot = 0; slot < slots; slot++)
            {
                for (int v = 0; v < vertices.Length; v++)
                {
                    if (weights[v * slots + slot] >= MinWeight)
                    {
                        used.Add(slot);
                        break;
                    }
                }
            }
            used.Sort((a, b) =>
            {
                VoxelandBlockType ta = TypeFor(Palette.Types[a]), tb = TypeFor(Palette.Types[b]);
                return ta.layer != tb.layer ? ta.layer - tb.layer : Palette.Types[a] - Palette.Types[b];
            });

            var sofar = new float[vertices.Length];
            for (int li = 0; li < used.Count; li++)
            {
                VoxelandBlockType type = TypeFor(Palette.Types[used[li]]);
                var uvs = new Vector2[vertices.Length];
                for (int v = 0; v < vertices.Length; v++)
                {
                    float weight = weights[v * slots + used[li]];
                    sofar[v] += weight;
                    float opacity = li == 0 ? 1 : sofar[v] > 0 ? weight / sofar[v] : 0;
                    uvs[v] = new Vector2(opacity, type.cachedGloss);
                }
                List<int> layerTriangles = triangles;
                if (li > 0)
                {
                    layerTriangles = new List<int>();
                    for (int t = 0; t < triangles.Count; t += 3)
                    {
                        if (uvs[triangles[t]].x > 0 || uvs[triangles[t + 1]].x > 0 || uvs[triangles[t + 2]].x > 0)
                        {
                            layerTriangles.Add(triangles[t]); layerTriangles.Add(triangles[t + 1]); layerTriangles.Add(triangles[t + 2]);
                        }
                    }
                }
                var mesh = new Mesh { name = "far field" };
                mesh.vertices = vertices;
                mesh.normals = tileData.Normals;
                mesh.tangents = tileData.Tangents;
                mesh.uv = uvs;
                mesh.SetTriangles(layerTriangles, 0);
                mesh.RecalculateBounds();

                var layer = new GameObject(type.name) { layer = renderLayer };
                layer.transform.SetParent(tile.Object.transform, false);
                layer.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = layer.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = li == 0 ? type.opaqueMaterial : type.material;
                renderer.sortingOrder = li;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                tile.Layers.Add(layer);
                tile.Meshes.Add(mesh);
            }
        }

        private static void AddSkirt((Vector3[] vertices, Vector3[] normals, Vector4[] tangents, float[] weights) a,
            List<int> triangles, int n, ref int next, float depth, System.Func<int, int> edge, bool flip)
        {
            int start = next, slots = Palette.Types.Length;
            for (int j = 0; j <= n; j++)
            {
                int top = edge(j);
                a.vertices[next] = a.vertices[top] - new Vector3(0, depth, 0);
                a.normals[next] = a.normals[top];
                a.tangents[next] = a.tangents[top];
                System.Array.Copy(a.weights, top * slots, a.weights, next * slots, slots);
                next++;
            }
            for (int j = 0; j < n; j++)
            {
                int ta = edge(j), tb = edge(j + 1), c = start + j, d = start + j + 1;
                if (flip)
                {
                    triangles.Add(ta); triangles.Add(tb); triangles.Add(c);
                    triangles.Add(c); triangles.Add(tb); triangles.Add(d);
                }
                else
                {
                    triangles.Add(ta); triangles.Add(c); triangles.Add(tb);
                    triangles.Add(tb); triangles.Add(c); triangles.Add(d);
                }
            }
        }
    }
}
