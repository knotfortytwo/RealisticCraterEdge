using System;
using System.Collections.Generic;
using System.IO;

namespace CraterEdge.Generation
{
    public sealed class Features
    {
        private const int FileVersion = 4;
        private const double ExposedTolerance = 3, BrineBelowRim = 1.5, BrineMinDepth = 3, BrineShore = 1.5, GeyserRadius = 3,
            VentRadius = 2;
        private const int RimSamples = 12;

        public enum Kind : byte
        {
            Vent = 1,
            Geyser = 2,
            Brine = 3,
            Drillable = 4,
            Patrol = 5,
            Waypoint = 6,
        }

        public readonly struct Feature
        {
            public readonly Kind Kind;
            public readonly float X, Y, Z, Radius;
            public readonly float Level;
            public readonly float NX, NY, NZ;
            public readonly byte Variant;

            public Feature(Kind kind, double x, double y, double z, double radius, double level,
                double nx = 0, double ny = 1, double nz = 0, int variant = 0)
            {
                Kind = kind;
                X = (float)x;
                Y = (float)y;
                Z = (float)z;
                Radius = (float)radius;
                Level = (float)level;
                NX = (float)nx;
                NY = (float)ny;
                NZ = (float)nz;
                Variant = (byte)variant;
            }
        }

        public readonly List<Feature> All = new List<Feature>();

        public static Features Build(Field field, FarField far, FarDistance distance)
        {
            var features = new Features();
            var craters = new List<Landforms.Site>();
            var basins = new List<Landforms.Site>();
            Landforms.Sites(FarField.Extent - 200, craters, basins);
            var column = new Field.Column();
            var exposed = new List<Landforms.Site>();

            foreach (var crater in craters)
            {
                double y = SurfaceAt(field, distance, column, crater.X, crater.Z);
                if (!Landforms.Vents(crater.X, crater.Z, out _, out _, out double ground)
                    || Math.Abs(y - (ground + Field.Hummocks(crater.X, crater.Z) + Field.Bumps(crater.X, crater.Z))) > ExposedTolerance)
                {
                    continue;
                }
                exposed.Add(crater);
                int count = Landforms.VentCount(crater);
                for (int i = 0; i < count; i++)
                {
                    var v = Landforms.VentOf(crater, i);
                    double vy = SurfaceAt(field, distance, column, v.X, v.Z);
                    features.All.Add(new Feature(v.Geyser ? Kind.Geyser : Kind.Vent, v.X, vy, v.Z, v.Geyser ? GeyserRadius : VentRadius, vy));
                }
            }

            foreach (var basin in basins)
            {
                double floor = SurfaceAt(field, distance, column, basin.X, basin.Z);
                if (!Exposed(field, column, basin.X, basin.Z, floor))
                {
                    continue;
                }
                double rim = double.MaxValue;
                for (int i = 0; i < RimSamples; i++)
                {
                    double rx = Dodecagon[2 * i] * basin.Radius, rz = Dodecagon[2 * i + 1] * basin.Radius;
                    rim = Math.Min(rim, SurfaceAt(field, distance, column, basin.X + rx, basin.Z + rz));
                }
                double level = rim - BrineBelowRim;
                if (level - floor < BrineMinDepth)
                {
                    continue;
                }
                double waterline = 0;
                for (int i = 0; i < RimSamples; i++)
                {
                    for (double r = 1; r <= basin.Radius * 1.3; r += 1)
                    {
                        if (SurfaceAt(field, distance, column, basin.X + Dodecagon[2 * i] * r, basin.Z + Dodecagon[2 * i + 1] * r) >= level)
                        {
                            waterline = Math.Max(waterline, r);
                            break;
                        }
                    }
                }
                features.All.Add(new Feature(Kind.Brine, basin.X, floor, basin.Z, waterline + BrineShore, level));
            }

            foreach (var chamber in field.Caves.Chambers)
            {
                double low = chamber.Y - chamber.Radius - 6, high = chamber.Y;
                field.Prepare(column, chamber.X, chamber.Z);
                double b = BaseAt(field, chamber.X, chamber.Z);
                if (field.Evaluate(column, b, low) < 0)
                {
                    continue;
                }
                for (int i = 0; i < 24; i++)
                {
                    double mid = 0.5 * (low + high);
                    if (field.Evaluate(column, b, mid) >= 0)
                    {
                        low = mid;
                    }
                    else
                    {
                        high = mid;
                    }
                }
                double y = 0.5 * (low + high);
                features.All.Add(new Feature(Kind.Vent, chamber.X, y, chamber.Z, VentRadius, y));
            }

            foreach (var d in Deposits.Place(field, distance, exposed))
            {
                features.All.Add(new Feature(Kind.Drillable, d.X, d.Y, d.Z, 0, d.Y, d.NX, d.NY, d.NZ, d.Resource));
            }

            foreach (var zone in Patrols.Place(far))
            {
                double ground = far.HeightAt(zone.X, zone.Z, false);
                features.All.Add(new Feature(Kind.Patrol, zone.X, ground, zone.Z, zone.Radius, ground, variant: zone.Route.Count));
                for (int i = 0; i < zone.Route.Count; i++)
                {
                    var (x, y, z) = zone.Route[i];
                    features.All.Add(new Feature(Kind.Waypoint, x, y, z, 0, y, variant: i));
                }
            }
            return features;
        }

        private static readonly double[] Dodecagon =
        {
            1, 0, 0.8660254037844387, 0.5, 0.5, 0.8660254037844387, 0, 1, -0.5, 0.8660254037844387, -0.8660254037844387, 0.5,
            -1, 0, -0.8660254037844387, -0.5, -0.5, -0.8660254037844387, 0, -1, 0.5, -0.8660254037844387, 0.8660254037844387, -0.5,
        };

        private static bool InBox(double x, double z) =>
            x > World.MinX && x < World.MinX + World.SizeX && z > World.MinZ && z < World.MinZ + World.SizeZ;

        private static double BaseAt(Field field, double x, double z)
        {
            int cx = (int)Math.Floor(x) - World.MinX, cz = (int)Math.Floor(z) - World.MinZ;
            cx = cx < 0 ? 0 : cx >= World.SizeX ? World.SizeX - 1 : cx;
            cz = cz < 0 ? 0 : cz >= World.SizeZ ? World.SizeZ - 1 : cz;
            return field.Model.Base[cx * IslandModel.Width + cz];
        }

        private static double SurfaceAt(Field field, FarDistance distance, Field.Column column, double x, double z) =>
            FarField.SurfaceHeight(field, column, InBox(x, z) ? BaseAt(field, x, z) : distance.At(x, z), x, z, true);

        private static bool Exposed(Field field, Field.Column column, double x, double z, double surface)
        {
            field.Prepare(column, x, z);
            return Math.Abs(surface - column.Ground) < ExposedTolerance;
        }

        public byte[] Write()
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(FileVersion);
                w.Write(Field.Version);
                w.Write(All.Count);
                foreach (var f in All)
                {
                    w.Write((byte)f.Kind);
                    w.Write(f.X);
                    w.Write(f.Y);
                    w.Write(f.Z);
                    w.Write(f.Radius);
                    w.Write(f.Level);
                    w.Write(f.NX);
                    w.Write(f.NY);
                    w.Write(f.NZ);
                    w.Write(f.Variant);
                }
                w.Flush();
                return stream.ToArray();
            }
        }

        public static Features Read(byte[] data)
        {
            var features = new Features();
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                if (r.ReadInt32() != FileVersion || r.ReadInt32() != Field.Version)
                {
                    throw new InvalidDataException("features from another version");
                }
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var kind = (Kind)r.ReadByte();
                    float x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle(), radius = r.ReadSingle(), level = r.ReadSingle();
                    float nx = r.ReadSingle(), ny = r.ReadSingle(), nz = r.ReadSingle();
                    byte variant = r.ReadByte();
                    features.All.Add(new Feature(kind, x, y, z, radius, level, nx, ny, nz, variant));
                }
            }
            return features;
        }
    }
}
