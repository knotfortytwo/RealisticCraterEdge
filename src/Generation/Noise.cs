using System;

namespace CraterEdge.Generation
{
    public static class Noise
    {
        public static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return x;
            }
        }

        public static uint Hash(int x, int y, int z, uint seed)
        {
            unchecked
            {
                return Hash((uint)x * 0x8da6b343U ^ Hash((uint)y * 0xd8163841U ^ Hash((uint)z * 0xcb1ab31fU ^ seed)));
            }
        }

        public static uint Hash(int x, int z, uint seed)
        {
            unchecked
            {
                return Hash((uint)x * 0x8da6b343U ^ Hash((uint)z * 0xcb1ab31fU ^ seed));
            }
        }

        private static readonly double[] GradX = { 1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0 };
        private static readonly double[] GradY = { 1, 1, -1, -1, 0, 0, 0, 0, 1, -1, 1, -1 };
        private static readonly double[] GradZ = { 0, 0, 0, 0, 1, 1, -1, -1, 1, 1, -1, -1 };

        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

        private static double Grad(int ix, int iy, int iz, double dx, double dy, double dz, uint seed)
        {
            int g = (int)(Hash(ix, iy, iz, seed) % 12);
            return GradX[g] * dx + GradY[g] * dy + GradZ[g] * dz;
        }

        public static double Gradient(double x, double y, double z, uint seed)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
            int ix = (int)fx, iy = (int)fy, iz = (int)fz;
            double dx = x - fx, dy = y - fy, dz = z - fz;
            double u = Fade(dx), v = Fade(dy), w = Fade(dz);
            double x00 = Lerp(Grad(ix, iy, iz, dx, dy, dz, seed), Grad(ix + 1, iy, iz, dx - 1, dy, dz, seed), u);
            double x10 = Lerp(Grad(ix, iy + 1, iz, dx, dy - 1, dz, seed), Grad(ix + 1, iy + 1, iz, dx - 1, dy - 1, dz, seed), u);
            double x01 = Lerp(Grad(ix, iy, iz + 1, dx, dy, dz - 1, seed), Grad(ix + 1, iy, iz + 1, dx - 1, dy, dz - 1, seed), u);
            double x11 = Lerp(Grad(ix, iy + 1, iz + 1, dx, dy - 1, dz - 1, seed), Grad(ix + 1, iy + 1, iz + 1, dx - 1, dy - 1, dz - 1, seed), u);
            return Lerp(Lerp(x00, x10, v), Lerp(x01, x11, v), w);
        }

        public static double Gradient(double x, double z, uint seed) => Gradient(x, 0.5, z, seed);

        public static double Fbm(double x, double y, double z, int octaves, uint seed)
        {
            double sum = 0, amp = 1, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Gradient(x, y, z, seed + (uint)i * 0x9e3779b9U);
                norm += amp;
                x *= 2;
                y *= 2;
                z *= 2;
                amp *= 0.5;
            }
            return sum / norm;
        }

        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = (x - edge0) / (edge1 - edge0);
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return t * t * (3 - 2 * t);
        }
    }
}
