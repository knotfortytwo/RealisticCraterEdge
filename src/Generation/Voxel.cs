using System;
using System.Runtime.InteropServices;

namespace CraterEdge.Generation
{
    [StructLayout(LayoutKind.Explicit, Size = 2)]
    public struct Voxel : IEquatable<Voxel>
    {
        [FieldOffset(0)] public byte Type;
        [FieldOffset(1)] public byte Density;
        [FieldOffset(0)] public ushort Bits;

        public Voxel(byte type, byte density)
        {
            Bits = 0;
            Type = type;
            Density = density;
        }

        public bool IsNearSurface => Density != 0;
        public bool IsBelowSurface => Density == 0 ? Type > 0 : Density >= Generation.Density.Surface;
        public bool IsNearAndBelowSurface => Type > 0 && Density >= Generation.Density.Surface;

        public bool Equals(Voxel other) => Bits == other.Bits;
        public override bool Equals(object obj) => obj is Voxel v && v.Bits == Bits;
        public override int GetHashCode() => Bits;
    }

    public static class Morton
    {
        private static readonly ushort[] X = new ushort[Octree.Size], Y = new ushort[Octree.Size], Z = new ushort[Octree.Size];

        static Morton()
        {
            for (int v = 0; v < Octree.Size; v++)
            {
                int spread = 0;
                for (int bit = 0; bit < 5; bit++)
                {
                    spread |= ((v >> bit) & 1) << (3 * bit);
                }
                Z[v] = (ushort)spread;
                Y[v] = (ushort)(spread << 1);
                X[v] = (ushort)(spread << 2);
            }
        }

        public static int Index(int x, int y, int z) => X[x] | Y[y] | Z[z];
    }
}
