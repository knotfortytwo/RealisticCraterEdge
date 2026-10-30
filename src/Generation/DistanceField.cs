using System;

namespace CraterEdge.Generation
{
    public static class DistanceField
    {
        public static float[] Signed(bool[] inside, int width, int height)
        {
            var result = new float[inside.Length];
            WriteDistance(inside, false, width, height, result);
            WriteDistance(inside, true, width, height, result);
            return result;
        }

        private static void WriteDistance(bool[] cells, bool target, int width, int height, float[] result)
        {
            const double Inf = 1e20;
            var d = new float[cells.Length];
            int n = Math.Max(width, height);
            var f = new double[n];
            var outRow = new double[n];
            var v = new int[n];
            var zBuf = new double[n + 1];
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    f[z] = cells[x * height + z] == target ? 0 : Inf;
                }
                Transform1D(f, height, outRow, v, zBuf);
                for (int z = 0; z < height; z++)
                {
                    d[x * height + z] = (float)outRow[z];
                }
            }
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    f[x] = d[x * height + z];
                }
                Transform1D(f, width, outRow, v, zBuf);
                for (int x = 0; x < width; x++)
                {
                    int i = x * height + z;
                    if (cells[i] != target)
                    {
                        result[i] = target ? 0.5f - (float)Math.Sqrt(outRow[x]) : (float)Math.Sqrt(outRow[x]) - 0.5f;
                    }
                }
            }
        }

        public static int[] NearestTarget(bool[] target, int width, int height)
        {
            const double Inf = 1e20;
            var nearestZ = new int[target.Length];
            var d = new float[target.Length];
            int n = Math.Max(width, height);
            var f = new double[n];
            var outRow = new double[n];
            var arg = new int[n];
            var v = new int[n];
            var zBuf = new double[n + 1];
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    f[z] = target[x * height + z] ? 0 : Inf;
                }
                Transform1D(f, height, outRow, v, zBuf, arg);
                for (int z = 0; z < height; z++)
                {
                    d[x * height + z] = (float)outRow[z];
                    nearestZ[x * height + z] = outRow[z] >= Inf ? -1 : arg[z];
                }
            }
            var result = new int[target.Length];
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    f[x] = d[x * height + z];
                }
                Transform1D(f, width, outRow, v, zBuf, arg);
                for (int x = 0; x < width; x++)
                {
                    int sx = arg[x];
                    int sz = nearestZ[sx * height + z];
                    result[x * height + z] = outRow[x] >= Inf || sz < 0 ? -1 : sx * height + sz;
                }
            }
            return result;
        }

        private static void Transform1D(double[] f, int n, double[] d, int[] v, double[] z) =>
            Transform1D(f, n, d, v, z, null);

        private static void Transform1D(double[] f, int n, double[] d, int[] v, double[] z, int[] arg)
        {
            int k = 0;
            v[0] = 0;
            z[0] = double.NegativeInfinity;
            z[1] = double.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                double s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q)
                {
                    k++;
                }
                d[q] = (double)(q - v[k]) * (q - v[k]) + f[v[k]];
                if (arg != null)
                {
                    arg[q] = v[k];
                }
            }
        }
    }
}
