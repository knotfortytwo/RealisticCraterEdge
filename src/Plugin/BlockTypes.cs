using System;
using System.Collections.Generic;
using CraterEdge.Generation;
using HarmonyLib;
using UnityEngine;

namespace RealisticCraterEdge
{
    [HarmonyPatch(typeof(Voxeland), nameof(Voxeland.LoadPaletteStatic))]
    internal static class BlockTypes
    {
        private const int Template = 163;
        private const float Gloss = 0.125f;

        private readonly struct Surface
        {
            public readonly Texture Albedo, Normal;
            public readonly float Scale;

            public Surface(VoxelandBlockType[] palette, int type, string albedo, string normal, float scale)
            {
                Material m = palette[type].material;
                Albedo = m.GetTexture(albedo);
                Normal = m.GetTexture(normal);
                Scale = scale;
            }
        }

        private static readonly Dictionary<byte, VoxelandBlockType> made = new Dictionary<byte, VoxelandBlockType>();

        private static void Postfix(VoxelandBlockType[] __result)
        {
            try
            {
                Add(__result);
            }
            catch (Exception)
            {
            }
        }

        private static void Add(VoxelandBlockType[] palette)
        {
            if (palette == null || palette.Length <= Template || !HasMaterial(palette, 18, 25, 80, 163, 188, 212, 218, 222))
            {
                return;
            }
            if (made.Count == 0)
            {
                var rock = new Surface(palette, 163, "_SideTexture", "_SideBumpMap", 0.14f);
                var sand = new Surface(palette, 163, "_CapTexture", "_CapBumpMap", 0.14f);
                var silt = new Surface(palette, 188, "_MainTex", "_BumpMap", 0.18f);
                var darkRock = new Surface(palette, 80, "_SideTexture", "_SideBumpMap", 0.14f);
                var basalt = new Surface(palette, 25, "_MainTex", "_BumpMap", 0.13f);
                var cobbles = new Surface(palette, 212, "_CapTexture", "_CapBumpMap", 0.2f);
                var layered = new Surface(palette, 218, "_SideTexture", "_SideBumpMap", 0.1f);
                var crust = new Surface(palette, 222, "_SideTexture", "_SideBumpMap", 0.2f);
                var crustCap = new Surface(palette, 222, "_CapTexture", "_CapBumpMap", 0.2f);
                Make(palette, Palette.RockSand, "RockSand", rock, sand);
                Make(palette, Palette.RockSilt, "RockSilt", rock, silt);
                Make(palette, Palette.DarkRockSilt, "DarkRockSilt", darkRock, silt);
                Make(palette, Palette.BasaltSilt, "BasaltSilt", basalt, silt);
                Make(palette, Palette.Scree, "Scree", darkRock, cobbles);
                Make(palette, Palette.Ooze, "Ooze", silt, sand);
                Make(palette, Palette.Strata, "Strata", layered, silt);
                Make(palette, Palette.VentCrust, "VentCrust", crust, crustCap);
            }
            foreach (var kv in made)
            {
                if (kv.Key < palette.Length && palette[kv.Key] == null)
                {
                    palette[kv.Key] = kv.Value;
                }
            }
        }

        private static bool HasMaterial(VoxelandBlockType[] palette, params int[] types)
        {
            foreach (int t in types)
            {
                if (palette[t] == null || palette[t].material == null)
                {
                    return false;
                }
            }
            return true;
        }

        private static void Make(VoxelandBlockType[] palette, byte id, string name, Surface side, Surface cap)
        {
            var material = new Material(palette[Template].material) { name = "CraterEdge" + name };
            material.SetTexture("_SideTexture", side.Albedo);
            material.SetTexture("_SideBumpMap", side.Normal);
            material.SetFloat("_SideScale", side.Scale);
            material.SetTexture("_CapTexture", cap.Albedo);
            material.SetTexture("_CapBumpMap", cap.Normal);
            material.SetFloat("_CapScale", cap.Scale);
            material.SetFloat("_Gloss", Gloss);
            made[id] = new VoxelandBlockType { name = "CraterEdge" + name, filled = true, material = material };
        }
    }
}
