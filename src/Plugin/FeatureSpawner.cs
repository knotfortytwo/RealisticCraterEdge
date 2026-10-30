using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using CraterEdge.Generation;
using Nautilus.Json;
using UnityEngine;
using UWE;

namespace RealisticCraterEdge
{
    internal sealed class FeatureSpawner : MonoBehaviour
    {
        private const float SpawnRange = 220, DespawnRange = 280, UpdateInterval = 0.5f;
        private const int SpawnsPerUpdate = 3, DiscSegments = 48;
        private const float DepositSink = 0.6f, ChunkHealth = 200;
        internal const string TrackerPrefix = "RealisticCraterEdge.";
        private static readonly Dictionary<string, string> DrillableIds = new Dictionary<string, string>
        {
            ["Copper"] = "601ee500-1744-4697-8279-59ef35160edb",
            ["Silver"] = "026d91e2-430b-4c6d-8bd4-b51e270d5eed",
            ["Gold"] = "a05fe1c9-ae0d-43db-a12c-865992808cb2",
            ["Lead"] = "1efa1a20-3a39-4f56-ace0-154211d6af12",
            ["Titanium"] = "9f855246-76c4-438b-8e4d-9cd6d7ce4224",
            ["Quartz"] = "b3db72b6-f0cf-4234-be74-d98bd4c49797",
            ["Salt"] = "793b4079-ef3b-43da-9fc7-3ec5cbc3ae19",
            ["Lithium"] = "846c3df6-ffbf-4206-b591-72f5ba11ed40",
            ["Magnetite"] = "f67c158c-3b83-473c-ad52-93fd2eeef66b",
            ["Nickel"] = "9c8f56e6-3380-42e4-a758-e8d733b5ddec",
            ["Sulphur"] = "697beac5-e39a-4809-854d-9163da9f997e",
            ["Kyanite"] = "4f441e53-7a9a-44dc-83a4-b1791dc88ffd",
            ["KyaniteLarge"] = "853a9c5b-aba3-4d6b-a547-34553aa73fa9",
            ["Diamond"] = "e7c097ac-e7be-4808-aaaa-70178d96f68b",
            ["Ruby"] = "109bbd29-c445-4ad8-a4bf-be7bc6d421d6",
            ["Uraninite"] = "fb5de2b6-1fe8-44fc-a555-dc0a09dc292a",
        };
        private const float PlaneHalf = 5;
        private const string GeyserId = "ce0b4131-86e2-444b-a507-45f7b824a286";
        private const string BrineId = "d931cce0-b6b3-4f70-aa08-e1ed5ef12b29";
        private static readonly string[] VentIds =
        {
            "18c78a26-806b-4ffd-bfd5-53eb7975decf",
            "c89a7a04-ea4a-438b-8e11-574e90952289",
            "5bbd405c-ca10-4da8-832b-87558c42f4dc",
        };

        private static FeatureSpawner live;

        private Features features;
        private readonly Dictionary<int, GameObject> spawned = new Dictionary<int, GameObject>();
        private readonly List<IPrefabRequest> requests = new List<IPrefabRequest>();
        private readonly List<GameObject> ventPrefabs = new List<GameObject>();
        private GameObject geyserPrefab, brinePrefab, warningTemplate, eruptionTemplate;
        private readonly GameObject[] drillablePrefabs = new GameObject[Deposits.Resources.Length];
        private readonly TechType[] drillableTech = new TechType[Deposits.Resources.Length];
        private readonly HashSet<int> minedOut = new HashSet<int>();
        private Transform templates;
        private Mesh disc;
        private float brineSurface;
        private bool ready;
        private float nextUpdate;

        public void Init(Features generated)
        {
            live = this;
            features = generated;
            if (Plugin.Deposits != null)
            {
                Plugin.Deposits.OnStartedSaving += OnSaving;
            }
            StartCoroutine(Load());
        }

        private IEnumerator Load()
        {
            templates = new GameObject("templates").transform;
            templates.SetParent(transform, false);
            templates.gameObject.SetActive(false);
            yield return Fetch(GeyserId, p => geyserPrefab = p);
            yield return Fetch(BrineId, p => brinePrefab = p);
            foreach (string id in VentIds)
            {
                yield return Fetch(id, p => ventPrefabs.Add(p));
            }
            if (geyserPrefab != null && geyserPrefab.GetComponent<Geyser>() is Geyser geyser)
            {
                warningTemplate = Template(geyser.warningSmokeParticles);
                eruptionTemplate = Template(geyser.eruptionParticles);
            }
            if (brinePrefab != null)
            {
                brineSurface = SurfaceHeight(brinePrefab);
            }
            for (int i = 0; i < Deposits.Resources.Length; i++)
            {
                int resource = i;
                if (DrillableIds.TryGetValue(Deposits.Resources[i], out string id))
                {
                    yield return Fetch(id, p => drillablePrefabs[resource] = p);
                }
                drillableTech[i] = drillablePrefabs[i] != null ? TechOf(drillablePrefabs[i]) : TechType.None;
            }
            Track();
            disc = Disc();
            ready = true;
        }

        internal static void Retrack()
        {
            if (live != null && live.ready)
            {
                live.Track();
            }
        }

        private void Track()
        {
            for (int i = 0; i < features.All.Count; i++)
            {
                var f = features.All[i];
                if (f.Kind != Features.Kind.Drillable || f.Variant >= drillableTech.Length)
                {
                    continue;
                }
                if (Saved(f) is float[] health && MinedOut(health))
                {
                    minedOut.Add(i);
                    continue;
                }
                ResourceTrackerDatabase.Register(TrackerPrefix + Key(f), new Vector3(f.X, f.Y, f.Z), drillableTech[f.Variant]);
            }
        }

        private static TechType TechOf(GameObject prefab)
        {
            var tracker = prefab.GetComponent<ResourceTracker>();
            return tracker != null && tracker.overrideTechType != TechType.None ? tracker.overrideTechType : CraftData.GetTechType(prefab);
        }

        private static string Key(Features.Feature f) =>
            string.Format(CultureInfo.InvariantCulture, "{0:F1},{1:F1},{2:F1}", f.X, f.Y, f.Z);

        private static float[] Saved(Features.Feature f) =>
            Plugin.Deposits != null && Plugin.Deposits.Health.TryGetValue(Key(f), out float[] health) ? health : null;

        private static bool MinedOut(float[] health)
        {
            foreach (float h in health)
            {
                if (h > 0)
                {
                    return false;
                }
            }
            return true;
        }

        private void Remember(int index, GameObject go)
        {
            if (Plugin.Deposits == null || go == null || !go.TryGetComponent(out Drillable drillable) || drillable.health == null)
            {
                return;
            }
            foreach (float h in drillable.health)
            {
                if (h < ChunkHealth)
                {
                    Plugin.Deposits.Health[Key(features.All[index])] = (float[])drillable.health.Clone();
                    return;
                }
            }
        }

        private void OnMinedOut(int index)
        {
            var f = features.All[index];
            minedOut.Add(index);
            if (Plugin.Deposits != null)
            {
                Plugin.Deposits.Health[Key(f)] = new float[] { 0 };
            }
            ResourceTrackerDatabase.Unregister(TrackerPrefix + Key(f), drillableTech[f.Variant]);
        }

        private void OnSaving(object sender, JsonFileEventArgs e)
        {
            foreach (var kv in spawned)
            {
                if (features.All[kv.Key].Kind == Features.Kind.Drillable && !minedOut.Contains(kv.Key))
                {
                    Remember(kv.Key, kv.Value);
                }
            }
        }

        private IEnumerator Fetch(string classId, System.Action<GameObject> found)
        {
            IPrefabRequest request = PrefabDatabase.GetPrefabAsync(classId);
            requests.Add(request);
            yield return request;
            if (request.TryGetPrefab(out GameObject prefab) && prefab != null)
            {
                found(prefab);
            }
        }

        private void Update()
        {
            if (!ready || Time.time < nextUpdate)
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
            int spawns = 0;
            for (int i = 0; i < features.All.Count; i++)
            {
                var f = features.All[i];
                if (f.Kind == Features.Kind.Patrol || f.Kind == Features.Kind.Waypoint)
                {
                    continue;
                }
                float dx = eye.x - f.X, dy = eye.y - f.Level, dz = eye.z - f.Z;
                float d2 = dx * dx + dy * dy + dz * dz;
                if (spawned.TryGetValue(i, out GameObject go))
                {
                    if (go == null || d2 > DespawnRange * DespawnRange)
                    {
                        if (go != null)
                        {
                            if (f.Kind == Features.Kind.Drillable && !minedOut.Contains(i))
                            {
                                Remember(i, go);
                            }
                            Destroy(go);
                        }
                        spawned.Remove(i);
                    }
                }
                else if (d2 < SpawnRange * SpawnRange && spawns < SpawnsPerUpdate && !minedOut.Contains(i))
                {
                    Spawn(i);
                    spawns++;
                }
            }
        }

        private void OnDestroy()
        {
            if (live == this)
            {
                live = null;
            }
            if (Plugin.Deposits != null)
            {
                Plugin.Deposits.OnStartedSaving -= OnSaving;
            }
            foreach (var request in requests)
            {
                request.Release();
            }
            requests.Clear();
            if (disc != null)
            {
                Destroy(disc);
            }
        }

        private void Spawn(int index)
        {
            var f = features.All[index];
            var position = new Vector3(f.X, f.Y, f.Z);
            var turn = Quaternion.Euler(0, index * 137.5f % 360, 0);
            GameObject go = null;
            switch (f.Kind)
            {
                case Features.Kind.Geyser:
                    if (geyserPrefab == null || warningTemplate == null || eruptionTemplate == null)
                    {
                        return;
                    }
                    go = Asleep(geyserPrefab);
                    var geyser = go.GetComponent<Geyser>();
                    geyser.warningSmokeParticles = warningTemplate;
                    geyser.eruptionParticles = eruptionTemplate;
                    go.AddComponent<GeyserPush>();
                    break;
                case Features.Kind.Vent:
                    if (ventPrefabs.Count == 0)
                    {
                        return;
                    }
                    go = Asleep(ventPrefabs[index % ventPrefabs.Count]);
                    break;
                case Features.Kind.Brine:
                    if (brinePrefab == null)
                    {
                        return;
                    }
                    go = Asleep(brinePrefab);
                    position = new Vector3(f.X, f.Level - brineSurface, f.Z);
                    turn = Quaternion.identity;
                    go.transform.localScale = new Vector3(f.Radius / PlaneHalf, 1, f.Radius / PlaneHalf);
                    foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh != null && filter.sharedMesh.name == "Plane")
                        {
                            filter.sharedMesh = disc;
                        }
                    }
                    break;
                case Features.Kind.Drillable:
                    GameObject prefab = f.Variant < drillablePrefabs.Length ? drillablePrefabs[f.Variant] : null;
                    if (prefab == null)
                    {
                        return;
                    }
                    float[] saved = Saved(f);
                    if (saved != null && MinedOut(saved))
                    {
                        minedOut.Add(index);
                        return;
                    }
                    go = Asleep(prefab);
                    var up = new Vector3(f.NX, f.NY, f.NZ);
                    position -= up * DepositSink;
                    turn = Quaternion.FromToRotation(Vector3.up, up) * turn;
                    var drillable = go.GetComponent<Drillable>();
                    if (drillable != null)
                    {
                        if (saved != null)
                        {
                            drillable.health = (float[])saved.Clone();
                        }
                        drillable.onDrilled += _ => OnMinedOut(index);
                    }
                    break;
            }
            if (go != null)
            {
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation(position, turn);
                go.SetActive(true);
                spawned[index] = go;
            }
        }

        private GameObject Asleep(GameObject prefab)
        {
            GameObject go = Instantiate(prefab, templates);
            Strip(go);
            Tag(go, prefab);
            return go;
        }

        internal static void Tag(GameObject go, GameObject prefab)
        {
            TechType type = CraftData.GetTechType(prefab);
            if (type != TechType.None && !go.TryGetComponent(out TechTag _))
            {
                go.AddComponent<TechTag>().type = type;
            }
        }

        private GameObject Template(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }
            GameObject copy = Instantiate(prefab, templates);
            Strip(copy);
            return copy;
        }

        internal static void Strip(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<ResourceTracker>(true))
            {
                DestroyImmediate(c);
            }
            foreach (var c in go.GetComponentsInChildren<EcoTarget>(true))
            {
                DestroyImmediate(c);
            }
            foreach (var c in go.GetComponentsInChildren<LargeWorldEntity>(true))
            {
                DestroyImmediate(c);
            }
            foreach (var c in go.GetComponentsInChildren<PrefabIdentifier>(true))
            {
                DestroyImmediate(c);
            }
        }

        private static float SurfaceHeight(GameObject lake)
        {
            foreach (var filter in lake.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null && filter.sharedMesh.name == "Plane" && filter.transform.up.y > 0)
                {
                    return lake.transform.InverseTransformPoint(filter.transform.position).y;
                }
            }
            return 0;
        }

        private static Mesh Disc()
        {
            var vertices = new Vector3[DiscSegments + 1];
            var normals = new Vector3[DiscSegments + 1];
            var uvs = new Vector2[DiscSegments + 1];
            var triangles = new int[DiscSegments * 3];
            normals[0] = Vector3.up;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < DiscSegments; i++)
            {
                float a = i * 2 * Mathf.PI / DiscSegments;
                float x = Mathf.Cos(a) * PlaneHalf, z = Mathf.Sin(a) * PlaneHalf;
                vertices[i + 1] = new Vector3(x, 0, z);
                normals[i + 1] = Vector3.up;
                uvs[i + 1] = new Vector2(0.5f + x / (2 * PlaneHalf), 0.5f + z / (2 * PlaneHalf));
                triangles[3 * i] = 0;
                triangles[3 * i + 1] = 1 + (i + 1) % DiscSegments;
                triangles[3 * i + 2] = 1 + i;
            }
            var mesh = new Mesh { name = "brine disc", vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
