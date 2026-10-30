using System;
using System.Collections.Generic;

namespace CraterEdge.Generation
{
    public static class Landforms
    {
        private static readonly double[] CanyonX =
        {
            0.9659258262890683, 0.6156614753256583, 0.03489949670250108, -0.6560590289905072, -0.984807753012208,
            -0.90630778703665, -0.3746065934159123, 0.2756373558169994, 0.838670567945424,
        };
        private static readonly double[] CanyonZ =
        {
            0.25881904510252074, 0.788010753606722, 0.9993908270190958, 0.7547095802227721, 0.17364817766693028,
            -0.4226182617406993, -0.9271838545667873, -0.9612616959383188, -0.544639035015027,
        };
        public const double CanyonNotch = 35;
        private const double CanyonStart = 1200, CanyonMeander = 230, CanyonMeanderLength = 900,
            CanyonNarrow = 90, CanyonWide = 260, CanyonWidenFrom = 1800, CanyonWidenTo = 6500,
            CanyonShallow = 60, CanyonDeep = 130, CanyonDeepenFrom = 1800, CanyonDeepenTo = 5000, CanyonFloor = 0.3;

        private const double ConeCell = 1400, ConeChance = 0.22, ConeHeightMin = 70, ConeHeightMax = 260,
            ConeSpreadMin = 3, ConeSpreadMax = 4.5, CraterChance = 0.4, CraterRadius = 0.16;

        private const double CraterDepth = 0.25, CraterFloor = 0.5, CraterLip = 1.35, CraterRimLift = 0.03,
            CalderaDepth = 0.1, CalderaFloor = 0.55, CalderaRimLift = 0.02;

        private const int VentsMin = 3, VentsMax = 5;
        private const double VentInner = 0.15, VentOuter = 0.45, VentHeightMin = 3, VentHeightMax = 7, VentSpreadMin = 2,
            VentSpreadMax = 2.8, VentPit = 1.6, VentPitDepth = 1.5, GeyserMound = 4, GeyserMoundRadius = 16, GeyserThroat = 3.5,
            GeyserThroatDepth = 4, CrustNear = 4, CrustReach = 26, GeyserCrustNear = 8, GeyserCrustReach = 40,
            OwnerTolerance = 1, VentFieldMargin = 60;
        private const double KnollCell = 700, KnollChance = 0.3, KnollHeightMin = 25, KnollHeightMax = 90,
            KnollSpreadMin = 4, KnollSpreadMax = 6;

        private static readonly Seamount[] Seamounts =
        {
            new Seamount(6900, 1700, 820, 2600, true),
            new Seamount(-5800, 5600, 650, 2300, false),
            new Seamount(-7000, -3100, 560, 2000, true),
            new Seamount(2900, -7100, 740, 2500, false),
        };
        private const double RidgeSize = 600, RidgeShare = 0.1, CalderaRadius = 0.12;

        private const double BasinCell = 1500, BasinChance = 0.6, BasinDepthMin = 10, BasinDepthMax = 22,
            BasinRadiusMin = 45, BasinRadiusMax = 110, BasinFloor = 0.55, BasinRim = 2;

        private const uint SeedCanyon = 0x632be59bU, SeedCones = 0x9e3779b9U, SeedKnolls = 0x85ebca77U,
            SeedRidges = 0xc2b2ae3dU, SeedBasins = 0x27d4eb2fU;

        private readonly struct Seamount
        {
            public readonly double X, Z, Height, Radius;
            public readonly bool Caldera;

            public Seamount(double x, double z, double height, double radius, bool caldera)
            {
                X = x;
                Z = z;
                Height = height;
                Radius = radius;
                Caldera = caldera;
            }
        }

        public readonly struct Site
        {
            public readonly double X, Z, Radius, Height;
            public readonly bool Crater;
            public readonly bool Seamount;
            public readonly uint Hash;

            public Site(double x, double z, double radius, double height, bool crater, uint hash, bool seamount = false)
            {
                X = x;
                Z = z;
                Radius = radius;
                Height = height;
                Crater = crater;
                Hash = hash;
                Seamount = seamount;
            }
        }

        public readonly struct Vent
        {
            public readonly double X, Z, Height, Radius, Pit, PitDepth, CrustNear, CrustReach;
            public readonly bool Geyser;

            public Vent(double x, double z, double height, double radius, double pit, double pitDepth, bool geyser)
            {
                X = x;
                Z = z;
                Height = height;
                Radius = radius;
                Pit = pit;
                PitDepth = pitDepth;
                Geyser = geyser;
                CrustNear = geyser ? GeyserCrustNear : Landforms.CrustNear;
                CrustReach = geyser ? GeyserCrustReach : Landforms.CrustReach;
            }
        }

        public static double Unit(uint h) => (h & 0xFFFFFF) / 16777216.0;

        public static double CanyonAt(double x, double z, out double incision)
        {
            double best = 0;
            incision = 0;
            for (int k = 0; k < CanyonX.Length; k++)
            {
                double along = x * CanyonX[k] + z * CanyonZ[k];
                if (along < CanyonStart)
                {
                    continue;
                }
                double across = z * CanyonX[k] - x * CanyonZ[k];
                if (Math.Abs(across) > CanyonMeander + CanyonWide)
                {
                    continue;
                }
                double centre = CanyonMeander * Noise.Gradient(along / CanyonMeanderLength, 3.7 * k + 0.5, SeedCanyon);
                double half = Noise.Lerp(CanyonNarrow, CanyonWide, Noise.SmoothStep(CanyonWidenFrom, CanyonWidenTo, along));
                double profile = Noise.SmoothStep(half, CanyonFloor * half, Math.Abs(across - centre))
                               * Noise.SmoothStep(CanyonStart, CanyonStart + 300, along);
                if (profile > best)
                {
                    best = profile;
                    incision = Noise.Lerp(CanyonShallow, CanyonDeep, Noise.SmoothStep(CanyonDeepenFrom, CanyonDeepenTo, along));
                }
            }
            return best;
        }

        public static double MoundsAt(double x, double z) =>
            Math.Max(Mounds(x, z, ConeCell, ConeChance, ConeHeightMin, ConeHeightMax, ConeSpreadMin, ConeSpreadMax, CraterChance, SeedCones),
                     Mounds(x, z, KnollCell, KnollChance, KnollHeightMin, KnollHeightMax, KnollSpreadMin, KnollSpreadMax, 0, SeedKnolls));

        private static double Mounds(double x, double z, double cell, double chance, double heightMin, double heightMax,
            double spreadMin, double spreadMax, double craterChance, uint seed)
        {
            int cx = (int)Math.Floor(x / cell), cz = (int)Math.Floor(z / cell);
            double best = double.NegativeInfinity;
            for (int i = cx - 1; i <= cx + 1; i++)
            {
                for (int k = cz - 1; k <= cz + 1; k++)
                {
                    if (!Mound(i, k, cell, chance, heightMin, heightMax, spreadMin, spreadMax, craterChance, seed, out Site m))
                    {
                        continue;
                    }
                    double dx = x - m.X, dz = z - m.Z, r2 = (dx * dx + dz * dz) / (m.Radius * m.Radius);
                    if (r2 >= 1)
                    {
                        continue;
                    }
                    double s = 1 - r2, top = Field.PlainDepth + m.Height * s * s;
                    if (m.Crater)
                    {
                        top += m.Height * CraterShape(Math.Sqrt(r2) / CraterRadius, CraterDepth, CraterFloor, CraterRimLift);
                    }
                    best = Math.Max(best, top);
                }
            }
            return best;
        }

        private static bool Mound(int i, int k, double cell, double chance, double heightMin, double heightMax,
            double spreadMin, double spreadMax, double craterChance, uint seed, out Site mound)
        {
            uint h = Noise.Hash(i, k, seed);
            if (Unit(h) >= chance)
            {
                mound = default;
                return false;
            }
            double px = (i + Unit(Noise.Hash(h + 1))) * cell, pz = (k + Unit(Noise.Hash(h + 2))) * cell;
            double height = Noise.Lerp(heightMin, heightMax, Unit(Noise.Hash(h + 3)));
            double radius = height * Noise.Lerp(spreadMin, spreadMax, Unit(Noise.Hash(h + 4)));
            mound = new Site(px, pz, radius, height, Unit(Noise.Hash(h + 5)) < craterChance, h);
            return true;
        }

        public static double SeamountsAt(double x, double z)
        {
            double best = double.NegativeInfinity;
            foreach (var m in Seamounts)
            {
                double dx = x - m.X, dz = z - m.Z, r2 = (dx * dx + dz * dz) / (m.Radius * m.Radius);
                if (r2 >= 1)
                {
                    continue;
                }
                double s = 1 - r2;
                double ridges = 1 - 2 * Math.Abs(Noise.Gradient(x / RidgeSize, z / RidgeSize, SeedRidges));
                double top = Field.PlainDepth + m.Height * s * s * (1 + RidgeShare * ridges);
                if (m.Caldera)
                {
                    top += m.Height * CraterShape(Math.Sqrt(r2) / CalderaRadius, CalderaDepth, CalderaFloor, CalderaRimLift);
                }
                best = Math.Max(best, top);
            }
            return best;
        }

        private static double CraterShape(double rr, double depth, double floor, double rimLift)
        {
            if (rr >= CraterLip)
            {
                return 0;
            }
            double bowl = 1 - Noise.SmoothStep(floor, 1, rr);
            double lip = Noise.SmoothStep(0.8, 1, rr) * (1 - Noise.SmoothStep(1, CraterLip, rr));
            return -depth * bowl + rimLift * lip;
        }

        public static int VentCount(Site crater) => 1 + VentsMin + (int)(Noise.Hash(crater.Hash + 20) % (uint)(VentsMax - VentsMin + 1));

        public static Vent VentOf(Site crater, int i)
        {
            if (i == 0)
            {
                return new Vent(crater.X, crater.Z, GeyserMound, GeyserMoundRadius, GeyserThroat, GeyserThroatDepth, true);
            }
            uint h = Noise.Hash(crater.Hash + 20 + (uint)i);
            double dx = Unit(Noise.Hash(h + 1)) - 0.5, dz = Unit(Noise.Hash(h + 2)) - 0.5;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 0.05)
            {
                dx = 1;
                dz = 0;
                len = 1;
            }
            double r = crater.Radius * Noise.Lerp(VentInner, VentOuter, Unit(Noise.Hash(h + 3)));
            double height = Noise.Lerp(VentHeightMin, VentHeightMax, Unit(Noise.Hash(h + 4)));
            double radius = height * Noise.Lerp(VentSpreadMin, VentSpreadMax, Unit(Noise.Hash(h + 5)));
            return new Vent(crater.X + dx / len * r, crater.Z + dz / len * r, height, radius, VentPit, VentPitDepth, false);
        }

        public static bool Vents(double x, double z, out double relief, out double crust, out double ground)
        {
            relief = 0;
            crust = 0;
            ground = double.NegativeInfinity;
            double mound = 0, pit = 0, moundTop = double.NaN, seamountTop = double.NaN, floorTop = double.NaN;
            bool any = false;
            int cx = (int)Math.Floor(x / ConeCell), cz = (int)Math.Floor(z / ConeCell);
            for (int i = cx - 1; i <= cx + 1; i++)
            {
                for (int k = cz - 1; k <= cz + 1; k++)
                {
                    if (!Mound(i, k, ConeCell, ConeChance, ConeHeightMin, ConeHeightMax, ConeSpreadMin, ConeSpreadMax, CraterChance, SeedCones, out Site m)
                        || !m.Crater)
                    {
                        continue;
                    }
                    var crater = new Site(m.X, m.Z, m.Radius * CraterRadius, m.Height, true, m.Hash);
                    if (!Near(crater, x, z))
                    {
                        continue;
                    }
                    Tops(x, z, ref moundTop, ref seamountTop, ref floorTop);
                    if (moundTop < Math.Max(seamountTop, floorTop) - OwnerTolerance)
                    {
                        continue;
                    }
                    any |= Accumulate(crater, x, z, ref mound, ref pit, ref crust);
                }
            }
            for (int s = 0; s < Seamounts.Length; s++)
            {
                var m = Seamounts[s];
                if (!m.Caldera)
                {
                    continue;
                }
                var caldera = CalderaSite(s);
                if (!Near(caldera, x, z))
                {
                    continue;
                }
                Tops(x, z, ref moundTop, ref seamountTop, ref floorTop);
                if (seamountTop < Math.Max(moundTop, floorTop) - OwnerTolerance)
                {
                    continue;
                }
                any |= Accumulate(caldera, x, z, ref mound, ref pit, ref crust);
            }
            if (!any)
            {
                return false;
            }
            relief = mound - pit;
            ground = Math.Max(Math.Max(moundTop, seamountTop), floorTop) + relief;
            return true;
        }

        private static Site CalderaSite(int s) =>
            new Site(Seamounts[s].X, Seamounts[s].Z, Seamounts[s].Radius * CalderaRadius, Seamounts[s].Height, true,
                     Noise.Hash((uint)s ^ SeedRidges), seamount: true);

        private static bool Near(Site crater, double x, double z)
        {
            double dx = x - crater.X, dz = z - crater.Z, reach = crater.Radius + VentFieldMargin;
            return dx * dx + dz * dz < reach * reach;
        }

        private static void Tops(double x, double z, ref double moundTop, ref double seamountTop, ref double floorTop)
        {
            if (double.IsNaN(moundTop))
            {
                moundTop = MoundsAt(x, z);
                seamountTop = SeamountsAt(x, z);
                floorTop = Field.FloorAt(x, z);
            }
        }

        private static bool Accumulate(Site crater, double x, double z, ref double mound, ref double pit, ref double crust)
        {
            int count = VentCount(crater);
            for (int i = 0; i < count; i++)
            {
                var v = VentOf(crater, i);
                double dx = x - v.X, dz = z - v.Z, d = Math.Sqrt(dx * dx + dz * dz);
                if (d < v.Radius)
                {
                    double t = d / v.Radius;
                    mound = Math.Max(mound, v.Height * (1 - t * t * (3 - 2 * t)));
                }
                if (d < v.Pit)
                {
                    double q = 1 - d * d / (v.Pit * v.Pit);
                    pit = Math.Max(pit, v.PitDepth * q * q);
                }
                crust = Math.Max(crust, 1 - Noise.SmoothStep(v.CrustNear, v.CrustReach, d));
            }
            return true;
        }

        public static double BasinAt(double x, double z)
        {
            int cx = (int)Math.Floor(x / BasinCell), cz = (int)Math.Floor(z / BasinCell);
            for (int i = cx - 1; i <= cx + 1; i++)
            {
                for (int k = cz - 1; k <= cz + 1; k++)
                {
                    if (!BasinCandidate(i, k, out Site b))
                    {
                        continue;
                    }
                    double dx = x - b.X, dz = z - b.Z, r = Math.Sqrt(dx * dx + dz * dz) / b.Radius;
                    if (r >= 1.3 || !BasinAllowed(b))
                    {
                        continue;
                    }
                    double floor = -b.Height * (1 - Noise.SmoothStep(BasinFloor, 1, r));
                    double rim = BasinRim * Noise.SmoothStep(0.85, 1, r) * (1 - Noise.SmoothStep(1, 1.3, r));
                    return floor + rim;
                }
            }
            return 0;
        }

        private static bool BasinCandidate(int i, int k, out Site basin)
        {
            uint h = Noise.Hash(i, k, SeedBasins);
            basin = default;
            if (Unit(h) >= BasinChance)
            {
                return false;
            }
            double px = (i + 0.15 + 0.7 * Unit(Noise.Hash(h + 1))) * BasinCell, pz = (k + 0.15 + 0.7 * Unit(Noise.Hash(h + 2))) * BasinCell;
            double depth = Noise.Lerp(BasinDepthMin, BasinDepthMax, Unit(Noise.Hash(h + 3)));
            double radius = Noise.Lerp(BasinRadiusMin, BasinRadiusMax, Unit(Noise.Hash(h + 4)));
            basin = new Site(px, pz, radius, depth, false, h);
            return true;
        }

        private static bool BasinAllowed(Site b) =>
            Field.FloorAt(b.X, b.Z) - Field.PlainAt(b.X, b.Z) <= 15 && MoundsAt(b.X, b.Z) <= Field.PlainDepth - 5
            && SeamountsAt(b.X, b.Z) <= Field.PlainDepth - 5;

        public static void Sites(double extent, List<Site> craters, List<Site> basins)
        {
            int n = (int)Math.Ceiling(extent / ConeCell);
            for (int i = -n; i <= n; i++)
            {
                for (int k = -n; k <= n; k++)
                {
                    if (Mound(i, k, ConeCell, ConeChance, ConeHeightMin, ConeHeightMax, ConeSpreadMin, ConeSpreadMax, CraterChance, SeedCones, out Site m)
                        && m.Crater && Math.Abs(m.X) < extent && Math.Abs(m.Z) < extent)
                    {
                        craters.Add(new Site(m.X, m.Z, m.Radius * CraterRadius, m.Height, true, m.Hash));
                    }
                }
            }
            for (int s = 0; s < Seamounts.Length; s++)
            {
                var m = Seamounts[s];
                if (m.Caldera)
                {
                    craters.Add(CalderaSite(s));
                }
            }
            n = (int)Math.Ceiling(extent / BasinCell);
            for (int i = -n; i <= n; i++)
            {
                for (int k = -n; k <= n; k++)
                {
                    if (BasinCandidate(i, k, out Site b) && BasinAllowed(b) && Math.Abs(b.X) < extent && Math.Abs(b.Z) < extent)
                    {
                        basins.Add(b);
                    }
                }
            }
        }
    }
}
