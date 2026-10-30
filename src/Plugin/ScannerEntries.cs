using System;
using HarmonyLib;

namespace RealisticCraterEdge
{
    [HarmonyPatch(typeof(ResourceTrackerDatabase))]
    internal static class ScannerEntries
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ResourceTrackerDatabase.OnProtoSerialize))]
        private static void KeepOutOfSave(ResourceTrackerDatabase __instance) => Drop(__instance);

        [HarmonyPrefix]
        [HarmonyPatch(nameof(ResourceTrackerDatabase.OnProtoDeserialize))]
        private static void DropFromSave(ResourceTrackerDatabase __instance) => Drop(__instance);

        [HarmonyPostfix]
        [HarmonyPatch("Start")]
        private static void TrackAgain() => FeatureSpawner.Retrack();

        private static void Drop(ResourceTrackerDatabase database) =>
            database.savedResources?.RemoveAll(r => r?.uniqueId != null && r.uniqueId.StartsWith(FeatureSpawner.TrackerPrefix, StringComparison.Ordinal));
    }
}
