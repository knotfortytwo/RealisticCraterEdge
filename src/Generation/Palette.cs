using System;

namespace CraterEdge.Generation
{
    public static class Palette
    {
        public const int Version = 4;

        public const byte Rock = 18;
        public const byte RockSand = 95;
        public const byte RockSilt = 96;
        public const byte DarkRockSilt = 107;
        public const byte BasaltSilt = 108;
        public const byte Scree = 117;
        public const byte Ooze = 118;
        public const byte Strata = 119;
        public const byte VentCrust = 121;

        public static readonly byte[] Custom = { RockSand, RockSilt, DarkRockSilt, BasaltSilt, Scree, Ooze, Strata, VentCrust };

        public static readonly byte[] Types = { Rock, RockSand, RockSilt, DarkRockSilt, BasaltSilt, Scree, Ooze, Strata, VentCrust };
        private const int SlotRock = 0, SlotRockSand = 1, SlotRockSilt = 2, SlotDarkRockSilt = 3, SlotBasaltSilt = 4,
            SlotScree = 5, SlotOoze = 6, SlotStrata = 7, SlotVentCrust = 8;

        private const double CrustFrom = 0.05, CrustFull = 0.7, CrustAbove = 12, CrustPatchSize = 9;

        private const double VanillaBand = 60, VanillaBlend = 80;
        private const double SiltAt = -1400, SiltBlend = 160;
        private const double DarkAt = -1650, DarkBlend = 200;
        private const double BasaltAt = -2250, BasaltBlend = 260;
        private const double WarpLarge = 60, WarpLargeSize = 600, WarpSmall = 25, WarpSmallSize = 120, WarpSmallHeight = 80;
        private static readonly double[] StrataAt = { -1800, -2500 };
        private const double StrataHalf = 10, StrataWobble = 6, StrataWobbleSize = 90, StrataSize = 350, StrataCoverage = 0.1,
            StrataAboveFloor = 300;
        private const double GullyScreeFrom = 0.18, GullyScreeFull = 0.4, GullyScreeLowest = 400, GullyScreeLow = 800;
        private const double TalusTop = 260, TalusFull = 120, TalusCoverage = 0.35;
        private const double PlainTop = 90, PlainFull = 30, SiltPatches = 0.3, SiltPatchSize = 90;
        private const double PatchSize = 40;
        private const double PatchPerMetre = 0.064, CoveragePerMetre = 0.0034, MaxSoftness = 0.5;

        private const uint SeedWarp = 0x2c1b3c6dU, SeedVanilla = 0x297a2d39U, SeedSilt = 0x6c078965U, SeedDark = 0x9e3779b1U,
            SeedBasalt = 0x7f4a7c15U, SeedStrata = 0x94d049bbU, SeedScree = 0xbf58476dU, SeedTalus = 0x165667b1U,
            SeedPlain = 0xd3a2646cU, SeedSiltPatch = 0xfd7046c5U, SeedCrust = 0x6a09e667U;

        [ThreadStatic]
        private static double[] scratch;

        public static byte TypeAt(double x, double y, double z, double belowCut, double belowAnchor)
        {
            double[] weights = scratch ?? (scratch = new double[Types.Length]);
            Weigh(x, y, z, belowCut, belowAnchor, 0, weights);
            int best = 0;
            for (int i = 1; i < weights.Length; i++)
            {
                if (weights[i] > weights[best])
                {
                    best = i;
                }
            }
            return Types[best];
        }

        public static void Weigh(double x, double y, double z, double belowCut, double belowAnchor, double blend, double[] weights)
        {
            Array.Clear(weights, 0, weights.Length);
            double soft = Math.Min(MaxSoftness, blend * PatchPerMetre / 2);
            double rest = 1;

            if (belowCut < VanillaBand + VanillaBlend / 2)
            {
                rest = Take(weights, SlotRock, rest, 1 - Past(belowCut - VanillaBand, VanillaBlend, x, y, z, SeedVanilla, soft));
                if (rest <= 0)
                {
                    return;
                }
            }

            if (Landforms.Vents(x, z, out _, out double crust, out double ground) && crust > CrustFrom && y < ground + CrustAbove)
            {
                double share = Noise.SmoothStep(CrustFrom, CrustFull, crust);
                rest = Take(weights, SlotVentCrust, rest, Step(Ramp(share, soft) - Patch(x, y, z, SeedCrust, CrustPatchSize), soft));
                if (rest <= 0)
                {
                    return;
                }
            }

            double aboveFloor = y - Field.FloorAt(x, z);
            if (aboveFloor < PlainTop)
            {
                double floor = Step(Ramp(Noise.SmoothStep(PlainTop, PlainFull, aboveFloor), soft) - Patch(x, y, z, SeedPlain), soft);
                if (floor > 0)
                {
                    double silt = Step(SiltPatches - Patch(x, y, z, SeedSiltPatch, SiltPatchSize), soft);
                    weights[SlotDarkRockSilt] += rest * floor * silt;
                    weights[SlotOoze] += rest * floor * (1 - silt);
                    rest *= 1 - floor;
                    if (rest <= 0)
                    {
                        return;
                    }
                }
            }

            double fade = Noise.SmoothStep(0, Field.NoiseFade, belowAnchor) * Noise.SmoothStep(GullyScreeLowest, GullyScreeLow, aboveFloor);
            if (fade > 0)
            {
                double gully = -fade * Field.GullyNoise(x, y, z);
                if (gully > GullyScreeFrom)
                {
                    double share = Noise.SmoothStep(GullyScreeFrom, GullyScreeFull, gully);
                    rest = Take(weights, SlotScree, rest, Step(Ramp(share, soft) - Patch(x, y, z, SeedScree), soft));
                    if (rest <= 0)
                    {
                        return;
                    }
                }
            }

            double yw = y + WarpLarge * Noise.Gradient(x / WarpLargeSize, z / WarpLargeSize, SeedWarp)
                          + WarpSmall * Noise.Gradient(x / WarpSmallSize, y / WarpSmallHeight, z / WarpSmallSize, SeedWarp + 1);
            if (aboveFloor > StrataAboveFloor)
            {
                double softMetres = blend / 2;
                for (int k = 0; k < StrataAt.Length; k++)
                {
                    double off = Math.Abs(yw - StrataAt[k]);
                    if (off >= StrataHalf + StrataWobble + softMetres)
                    {
                        continue;
                    }
                    uint seed = SeedStrata + (uint)(2 * k);
                    double half = StrataHalf + StrataWobble * Noise.Gradient(x / StrataWobbleSize, z / StrataWobbleSize, seed);
                    double band = Step(half - off, softMetres);
                    if (band > 0)
                    {
                        double coverage = Noise.Gradient(x / StrataSize, z / StrataSize, seed + 1);
                        band *= Step(coverage - StrataCoverage, Math.Min(MaxSoftness, blend * CoveragePerMetre / 2));
                        rest = Take(weights, SlotStrata, rest, band);
                        if (rest <= 0)
                        {
                            return;
                        }
                    }
                }
            }

            if (aboveFloor < TalusTop)
            {
                double share = Noise.SmoothStep(TalusTop, TalusFull, aboveFloor);
                double threshold = -soft + (TalusCoverage + soft) * share;
                rest = Take(weights, SlotScree, rest, Step(threshold - Patch(x, y, z, SeedTalus), soft));
                if (rest <= 0)
                {
                    return;
                }
            }

            rest = Take(weights, SlotBasaltSilt, rest, Past(BasaltAt - yw, BasaltBlend, x, y, z, SeedBasalt, soft));
            if (rest <= 0)
            {
                return;
            }
            rest = Take(weights, SlotDarkRockSilt, rest, Past(DarkAt - yw, DarkBlend, x, y, z, SeedDark, soft));
            if (rest <= 0)
            {
                return;
            }
            double siltZone = Past(SiltAt - yw, SiltBlend, x, y, z, SeedSilt, soft);
            weights[SlotRockSilt] += rest * siltZone;
            weights[SlotRockSand] += rest * (1 - siltZone);
        }

        private static double Take(double[] weights, int slot, double rest, double share)
        {
            weights[slot] += rest * share;
            return rest * (1 - share);
        }

        private static double Past(double past, double blend, double x, double y, double z, uint seed, double soft)
        {
            if (past <= -blend / 2)
            {
                return 0;
            }
            if (past >= blend / 2)
            {
                return 1;
            }
            return Step(Ramp(Noise.SmoothStep(-blend / 2, blend / 2, past), soft) - Patch(x, y, z, seed), soft);
        }

        private static double Step(double d, double soft) => soft <= 0 ? (d > 0 ? 1 : 0) : Noise.SmoothStep(-soft, soft, d);

        private static double Patch(double x, double y, double z, uint seed, double size = PatchSize)
        {
            double p = 0.5 + 1.6 * Noise.Fbm(x / size, y / size, z / size, 2, seed);
            return p < 0 ? 0 : p > 1 ? 1 : p;
        }

        private static double Ramp(double share, double soft) => -(0.05 + soft) + (1.1 + 2 * soft) * share;
    }
}
