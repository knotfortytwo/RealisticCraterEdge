using System;

namespace CraterEdge.Generation
{
    public sealed class BatchBuilder
    {
        private const int Cell = 4, Cells = Octree.Size / Cell, Lattice = Cells + 1;
        private const double CellBound = Field.Lipschitz * 3.47 + 1.05;
        private const double OctreeBound = Field.Lipschitz * 27.72 + 1.05;

        private readonly Field field;
        private readonly VanillaTerrain vanilla;
        private readonly FarDistance distance;
        private readonly System.Threading.ThreadLocal<Workspace> workspaces =
            new System.Threading.ThreadLocal<Workspace>(() => new Workspace());

        public BatchBuilder(Field field, VanillaTerrain vanilla, FarDistance distance)
        {
            this.field = field;
            this.vanilla = vanilla;
            this.distance = distance;
        }

        public int MaxRow()
        {
            int maxCut = int.MinValue;
            foreach (short c in field.Model.Cut)
            {
                if (c > maxCut) maxCut = c;
            }
            return (maxCut - 1 - World.MinY) / Batch.Size;
        }

        public Batch Baseline(int bx, int by, int bz) => World.InBox(bx, bz) ? vanilla.Load(bx, by, bz) : null;

        public Batch Build(int bx, int by, int bz, out Batch baseline)
        {
            var source = baseline = Baseline(bx, by, bz);
            var result = new Batch(bx, by, bz);
            bool changed = false;
            var work = workspaces.Value;
            for (int local = 0; local < result.Octrees.Length; local++)
            {
                var (ox, oy, oz) = result.OctreeCoords(local);
                if (oy < 0 || oy >= World.OctreesY)
                {
                    return null;
                }
                var original = source?.Octrees[local] ?? Water;
                var built = BuildOctree(ox, oy, oz, original, work);
                result.Octrees[local] = built;
                changed |= !ReferenceEquals(built, original) && !built.SameAs(original);
            }
            return changed ? result : null;
        }

        private sealed class Workspace
        {
            public readonly Voxel[] Grid = new Voxel[Octree.Voxels];
            public readonly double[] Lattice = new double[BatchBuilder.Lattice * BatchBuilder.Lattice * BatchBuilder.Lattice];
            public readonly int[] Cut = new int[Octree.Size * Octree.Size];
            public readonly Field.Column[] Columns = new Field.Column[ColumnsPerSide * ColumnsPerSide];
            public readonly double[] ColumnBase = new double[ColumnsPerSide * ColumnsPerSide];
            public readonly bool[] Prepared = new bool[ColumnsPerSide * ColumnsPerSide];
            public readonly Field.Column Scratch = new Field.Column();

            public Workspace()
            {
                for (int i = 0; i < Columns.Length; i++)
                {
                    Columns[i] = new Field.Column();
                }
            }
        }

        private const int ColumnsPerSide = Octree.Size + 1;

        private static readonly Octree Water = new Octree(new byte[4]);
        private static readonly Octree Rock = new Octree(new byte[] { Field.RockType, 0, 0, 0 });
        private static readonly Voxel FarRock = new Voxel(Field.RockType, Density.Far), FarWater = new Voxel(0, Density.Far);
        private const int CellVoxels = Cell * Cell * Cell;

        private double BaseAt(int vx, int vz) =>
            vx >= 0 && vz >= 0 && vx < World.SizeX && vz < World.SizeZ
                ? field.Model.Base[vx * IslandModel.Width + vz]
                : distance.At(World.MinX + vx + 0.5, World.MinZ + vz + 0.5);

        private int CutAt(int vx, int vz)
        {
            int cx = vx < 0 ? 0 : vx >= World.SizeX ? World.SizeX - 1 : vx;
            int cz = vz < 0 ? 0 : vz >= World.SizeZ ? World.SizeZ - 1 : vz;
            return field.CutAt(cx * IslandModel.Width + cz);
        }

        private double Eval(Workspace w, int vx, int vy, int vz) =>
            field.Evaluate(w.Scratch, BaseAt(vx, vz), World.MinX + vx + 0.5, World.MinY + vy + 0.5, World.MinZ + vz + 0.5);

        private double Eval(Workspace w, int x0, int z0, int lx, int vy, int lz)
        {
            int i = lx * ColumnsPerSide + lz;
            Field.Column c = w.Columns[i];
            if (!w.Prepared[i])
            {
                w.ColumnBase[i] = BaseAt(x0 + lx, z0 + lz);
                field.Prepare(c, World.MinX + x0 + lx + 0.5, World.MinZ + z0 + lz + 0.5);
                w.Prepared[i] = true;
            }
            return field.Evaluate(c, w.ColumnBase[i], World.MinY + vy + 0.5);
        }

        private static bool BelowCut(Workspace w, int ci, int ck, int top)
        {
            for (int x = ci * Cell; x < ci * Cell + Cell; x++)
            {
                for (int z = ck * Cell; z < ck * Cell + Cell; z++)
                {
                    if (top >= w.Cut[x * Octree.Size + z])
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static sbyte SignIn(Workspace w, int worldY0, int x, int y, int z) =>
            worldY0 + y >= w.Cut[x * Octree.Size + z] ? (sbyte)0 : w.Grid[Morton.Index(x, y, z)].Type != 0 ? (sbyte)1 : (sbyte)-1;

        private sbyte SignAt(Workspace w, int vx, int vy, int vz)
        {
            if (vy < 0 || vy >= World.SizeY)
            {
                return 0;
            }
            if (World.MinY + vy >= CutAt(vx, vz))
            {
                return 0;
            }
            return Eval(w, vx, vy, vz) >= 0 ? (sbyte)1 : (sbyte)-1;
        }

        private Octree BuildOctree(int ox, int oy, int oz, Octree original, Workspace w)
        {
            int x0 = ox * Octree.Size, y0 = oy * Octree.Size, z0 = oz * Octree.Size;
            int worldY0 = World.MinY + y0;
            int minCut = int.MaxValue, maxCut = int.MinValue;
            for (int x = 0; x < Octree.Size; x++)
            {
                for (int z = 0; z < Octree.Size; z++)
                {
                    int c = CutAt(x0 + x, z0 + z);
                    w.Cut[x * Octree.Size + z] = c;
                    if (c < minCut) minCut = c;
                    if (c > maxCut) maxCut = c;
                }
            }
            if (worldY0 >= maxCut)
            {
                return original;
            }
            Array.Clear(w.Prepared, 0, w.Prepared.Length);
            bool allGenerated = worldY0 + Octree.Size <= minCut;
            Voxel[] grid = w.Grid;
            if (allGenerated)
            {
                double f = Eval(w, x0, z0, 16, y0 + 16, 16);
                if (f > OctreeBound) return Rock;
                if (f < -OctreeBound) return Water;
            }
            else
            {
                original.Rasterize(grid);
            }

            for (int i = 0; i < Lattice; i++)
            {
                for (int k = 0; k < Lattice; k++)
                {
                    for (int j = 0; j < Lattice; j++)
                    {
                        w.Lattice[(i * Lattice + j) * Lattice + k] = Eval(w, x0, z0, i * Cell, y0 + j * Cell, k * Cell);
                    }
                }
            }

            for (int ci = 0; ci < Cells; ci++)
            {
                for (int cj = 0; cj < Cells; cj++)
                {
                    for (int ck = 0; ck < Cells; ck++)
                    {
                        double min = double.MaxValue, max = double.MinValue;
                        for (int c = 0; c < 8; c++)
                        {
                            double v = w.Lattice[((ci + (c >> 2)) * Lattice + cj + ((c >> 1) & 1)) * Lattice + ck + (c & 1)];
                            if (v < min) min = v;
                            if (v > max) max = v;
                        }
                        bool solid = min > CellBound, open = max < -CellBound;
                        if ((solid || open) && BelowCut(w, ci, ck, worldY0 + cj * Cell + Cell - 1))
                        {
                            grid.AsSpan(Morton.Index(ci * Cell, cj * Cell, ck * Cell), CellVoxels).Fill(solid ? FarRock : FarWater);
                            continue;
                        }
                        for (int x = ci * Cell; x < ci * Cell + Cell; x++)
                        {
                            for (int z = ck * Cell; z < ck * Cell + Cell; z++)
                            {
                                int cut = w.Cut[x * Octree.Size + z];
                                for (int y = cj * Cell; y < cj * Cell + Cell; y++)
                                {
                                    if (worldY0 + y >= cut)
                                    {
                                        continue;
                                    }
                                    double f = solid ? CellBound : open ? -CellBound : Eval(w, x0, z0, x, y0 + y, z);
                                    grid[Morton.Index(x, y, z)] = new Voxel(f >= 0 ? Field.RockType : (byte)0, Density.Encode((float)f));
                                }
                            }
                        }
                    }
                }
            }

            for (int x = 0; x < Octree.Size; x++)
            {
                for (int z = 0; z < Octree.Size; z++)
                {
                    int cut = w.Cut[x * Octree.Size + z];
                    for (int y = 0; y < Octree.Size; y++)
                    {
                        int i = Morton.Index(x, y, z);
                        if (worldY0 + y >= cut || grid[i].Density == Density.Far) continue;
                        bool rock = false, water = false, nearVanilla = false;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dz = -1; dz <= 1; dz++)
                                {
                                    int nx = x + dx, ny = y + dy, nz = z + dz;
                                    sbyte s = nx >= 0 && ny >= 0 && nz >= 0 && nx < Octree.Size && ny < Octree.Size && nz < Octree.Size
                                        ? SignIn(w, worldY0, nx, ny, nz)
                                        : SignAt(w, x0 + nx, y0 + ny, z0 + nz);
                                    if (s == 0) nearVanilla = true;
                                    else if (s > 0) rock = true;
                                    else water = true;
                                }
                            }
                        }
                        if (!nearVanilla && !(rock && water))
                        {
                            grid[i].Density = Density.Far;
                        }
                    }
                }
            }

            for (int x = 0; x < Octree.Size; x++)
            {
                for (int z = 0; z < Octree.Size; z++)
                {
                    int cut = w.Cut[x * Octree.Size + z];
                    double wx = World.MinX + x0 + x + 0.5, wz = World.MinZ + z0 + z + 0.5;
                    double anchor = double.NaN;
                    for (int y = 0; y < Octree.Size; y++)
                    {
                        int i = Morton.Index(x, y, z);
                        if (worldY0 + y >= cut || grid[i].Type == 0 || grid[i].Density == Density.Far)
                        {
                            continue;
                        }
                        if (double.IsNaN(anchor))
                        {
                            anchor = field.AnchorAt(wx, wz);
                        }
                        double wy = worldY0 + y + 0.5;
                        grid[i].Type = Palette.TypeAt(wx, wy, wz, cut - wy, anchor - wy);
                    }
                }
            }
            return Octree.Build(grid);
        }
    }
}
