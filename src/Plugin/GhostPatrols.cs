using System.Collections;
using System.Collections.Generic;
using CraterEdge.Generation;
using HarmonyLib;
using UnityEngine;
using UWE;

namespace RealisticCraterEdge
{
    internal sealed class GhostPatrols : MonoBehaviour
    {
        private const string GhostId = "54701bfc-bb1a-4a84-8f79-ba4f76691bef";
        private const float UpdateInterval = 1;
        private const float SpawnRange = 450, SpawnClear = 180, DespawnRange = 650;
        private const float DetectRange = 90, TerritoryMargin = 150, AggressionPerCheck = 0.5f;
        private const float Cruise = 9, RoundPriority = 0.5f;
        private const float RespawnDays = 5, DayLength = 1200;

        private sealed class Territory
        {
            public int Index;
            public Vector3 Centre;
            public float Radius, Length;
            public Vector3[] Route;
            public GameObject Ghost;
        }

        private readonly List<Territory> territories = new List<Territory>();
        private IPrefabRequest request;
        private GameObject prefab;
        private Transform holder;
        private float nextUpdate;

        public void Init(Features features)
        {
            var all = features.All;
            for (int i = 0; i < all.Count; i++)
            {
                var f = all[i];
                if (f.Kind != Features.Kind.Patrol || i + f.Variant >= all.Count)
                {
                    continue;
                }
                var route = new Vector3[f.Variant];
                float length = 0;
                for (int w = 0; w < route.Length; w++)
                {
                    var p = all[i + 1 + w];
                    route[w] = new Vector3(p.X, p.Y, p.Z);
                }
                for (int w = 0; w < route.Length; w++)
                {
                    length += Vector3.Distance(route[w], route[(w + 1) % route.Length]);
                }
                if (route.Length > 1)
                {
                    territories.Add(new Territory { Index = territories.Count, Centre = new Vector3(f.X, f.Y, f.Z), Radius = f.Radius, Route = route, Length = length });
                }
            }
            holder = new GameObject("templates").transform;
            holder.SetParent(transform, false);
            holder.gameObject.SetActive(false);
            StartCoroutine(Load());
        }

        private IEnumerator Load()
        {
            request = PrefabDatabase.GetPrefabAsync(GhostId);
            yield return request;
            if (request.TryGetPrefab(out GameObject found) && found != null)
            {
                prefab = found;
            }
        }

        private void Update()
        {
            if (prefab == null || Time.time < nextUpdate || Player.main == null || DayNightCycle.main == null)
            {
                return;
            }
            nextUpdate = Time.time + UpdateInterval;
            Player player = Player.main;
            Vector3 eye = player.transform.position;
            float now = DayNightCycle.main.timePassedAsFloat;
            foreach (var t in territories)
            {
                float dx = eye.x - t.Centre.x, dz = eye.z - t.Centre.z;
                float edge = Mathf.Sqrt(dx * dx + dz * dz) - t.Radius;
                if (t.Ghost == null)
                {
                    if (edge < SpawnRange && !Resting(t, now))
                    {
                        Spawn(t, eye, now);
                    }
                    continue;
                }
                if (edge > DespawnRange)
                {
                    Destroy(t.Ghost);
                    t.Ghost = null;
                    continue;
                }
                var live = t.Ghost.GetComponent<LiveMixin>();
                if (live != null && !live.IsAlive())
                {
                    if (Plugin.Patrols != null && !Plugin.Patrols.Killed.ContainsKey(t.Index))
                    {
                        Plugin.Patrols.Killed[t.Index] = now;
                    }
                    continue;
                }
                if (edge < TerritoryMargin && (eye - t.Ghost.transform.position).sqrMagnitude < DetectRange * DetectRange && player.CanBeAttacked())
                {
                    t.Ghost.GetComponent<Creature>().Aggression.Add(AggressionPerCheck);
                    t.Ghost.GetComponent<LastTarget>().SetTarget(player.gameObject);
                }
            }
        }

        private static bool Resting(Territory t, float now)
        {
            if (Plugin.Patrols == null || !Plugin.Patrols.Killed.TryGetValue(t.Index, out float when))
            {
                return false;
            }
            if (now - when < RespawnDays * DayLength && now >= when)
            {
                return true;
            }
            Plugin.Patrols.Killed.Remove(t.Index);
            return false;
        }

        private void Spawn(Territory t, Vector3 eye, float now)
        {
            const int tries = 24;
            float along = now * Cruise % t.Length;
            for (int attempt = 0; attempt < tries; attempt++, along = (along + t.Length / tries) % t.Length)
            {
                Vector3 at = PointAlong(t, along, out int next);
                if ((at - eye).sqrMagnitude < SpawnClear * SpawnClear)
                {
                    continue;
                }
                GameObject go = Instantiate(prefab, holder);
                FeatureSpawner.Strip(go);
                FeatureSpawner.Tag(go, prefab);
                var death = go.GetComponent<CreatureDeath>();
                if (death != null)
                {
                    death.respawn = false;
                }
                var round = go.AddComponent<GhostRound>();
                round.evaluatePriority = RoundPriority;
                round.Route = t.Route;
                round.Next = next;
                round.Speed = Cruise;
                Vector3 heading = t.Route[next] - at;
                go.transform.SetPositionAndRotation(at, heading.sqrMagnitude > 1 ? Quaternion.LookRotation(heading) : Quaternion.identity);
                go.transform.SetParent(transform, true);
                t.Ghost = go;
                return;
            }
        }

        private static Vector3 PointAlong(Territory t, float along, out int next)
        {
            for (int w = 0; w < t.Route.Length; w++)
            {
                Vector3 a = t.Route[w], b = t.Route[(w + 1) % t.Route.Length];
                float leg = Vector3.Distance(a, b);
                if (along <= leg || w == t.Route.Length - 1)
                {
                    next = (w + 1) % t.Route.Length;
                    return Vector3.Lerp(a, b, leg > 0 ? Mathf.Clamp01(along / leg) : 0);
                }
                along -= leg;
            }
            next = 0;
            return t.Route[0];
        }

        private void OnDestroy()
        {
            request?.Release();
            request = null;
        }
    }

    internal sealed class GhostRound : CreatureAction
    {
        private const float Reach = 40, SwimInterval = 1;

        internal Vector3[] Route;
        internal int Next;
        internal float Speed;
        private float nextSwim;

        public override float Evaluate(Creature creature, float time) => Route == null ? 0 : GetEvaluatePriority();

        public override void StartPerform(Creature creature, float time) => nextSwim = time;

        public override void Perform(Creature creature, float time, float deltaTime)
        {
            Vector3 target = Route[Next];
            if ((transform.position - target).sqrMagnitude < Reach * Reach)
            {
                Next = (Next + 1) % Route.Length;
                target = Route[Next];
            }
            creature.leashPosition = target;
            if (time >= nextSwim)
            {
                nextSwim = time + SwimInterval;
                swimBehaviour.SwimTo(target, Speed);
            }
        }
    }

    [HarmonyPatch(typeof(VoidGhostLeviathansSpawner), "UpdateSpawn")]
    internal static class VoidGhostSpawnerPatch
    {
        private static bool Prefix() => false;
    }
}
