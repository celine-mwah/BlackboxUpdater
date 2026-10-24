using System;
using System.Globalization;
using System.Numerics;

namespace BlackboxUpdater.Core
{
    public sealed class P256PublicKey
    {
        internal readonly BigInteger X;
        internal readonly BigInteger Y;

        private P256PublicKey(BigInteger x, BigInteger y) { X = x; Y = y; }

        public static P256PublicKey FromCoordinates(byte[] x, byte[] y)
        {
            if (x == null || y == null || x.Length != 32 || y.Length != 32) throw new FormatException("public key coordinates must be 32 bytes");
            var bx = P256Ecdsa.FromBigEndian(x);
            var by = P256Ecdsa.FromBigEndian(y);
            if (!P256Ecdsa.IsOnCurve(bx, by)) throw new FormatException("public key is not a point on P-256");
            return new P256PublicKey(bx, by);
        }
    }

    public static class P256Ecdsa
    {
        #region Constants & Fields
        private static readonly BigInteger P = FromHex("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");
        private static readonly BigInteger N = FromHex("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551");
        private static readonly BigInteger B = FromHex("5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B");
        private static readonly BigInteger A = P - 3;
        private static readonly Point G = new Point(
            FromHex("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296"),
            FromHex("4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5"));
        #endregion

        private struct Point
        {
            public readonly BigInteger X, Y;
            public readonly bool Infinity;
            public Point(BigInteger x, BigInteger y) { X = x; Y = y; Infinity = false; }
            private Point(bool inf) { X = BigInteger.Zero; Y = BigInteger.Zero; Infinity = inf; }
            public static Point AtInfinity => new Point(true);
        }

        #region Public API
        public static bool Verify(P256PublicKey key, byte[] hash, byte[] signature)
        {
            if (key == null || hash == null || hash.Length != 32 || signature == null || signature.Length != 64) return false;

            var r = FromBigEndian(Slice(signature, 0, 32));
            var s = FromBigEndian(Slice(signature, 32, 32));
            if (r.IsZero || r >= N || s.IsZero || s >= N) return false;

            var e = FromBigEndian(hash);
            var w = Inverse(s, N);
            var u1 = Mod(e * w, N);
            var u2 = Mod(r * w, N);

            var result = MultiplyAdd(u1, u2, new Point(key.X, key.Y));
            if (result.Infinity) return false;
            return Mod(result.X, N) == r;
        }

        public static bool SelfTest()
        {
            try
            {
                var key = P256PublicKey.FromCoordinates(
                    HexBytes("60FED4BA255A9D31C961EB74C6356D68C049B8923B61FA6CE669622E60F29FB6"),
                    HexBytes("7903FE1008B8BC99A41AE9E95628BC64F2F1B20C2D7E9F5177A3C294D4462299"));
                var sig = HexBytes("EFD48B2AACB6A8FD1140DD9CD45E81D69D2C877B56AAF991C34D0EA84EAF3716"
                                 + "F7CB1C942D657C41D436C7A1B6E29F65F3E900DBB9AFF4064DC4AB2F843ACDA8");
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    var good = sha.ComputeHash(System.Text.Encoding.ASCII.GetBytes("sample"));
                    var bad = sha.ComputeHash(System.Text.Encoding.ASCII.GetBytes("Sample"));
                    return Verify(key, good, sig) && !Verify(key, bad, sig);
                }
            }
            catch (Exception) { return false; }
        }

        private static byte[] HexBytes(string hex)
        {
            var b = new byte[hex.Length / 2];
            for (var i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        internal static bool IsOnCurve(BigInteger x, BigInteger y)
        {
            if (x.Sign < 0 || y.Sign < 0 || x >= P || y >= P) return false;
            return Mod(y * y, P) == Mod(x * x * x + A * x + B, P);
        }

        internal static BigInteger FromBigEndian(byte[] bytes)
        {
            var le = new byte[bytes.Length + 1];
            for (var i = 0; i < bytes.Length; i++) le[i] = bytes[bytes.Length - 1 - i];
            return new BigInteger(le);
        }
        #endregion

        #region Internal Utils
        private static BigInteger FromHex(string hex) => BigInteger.Parse("0" + hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        private static byte[] Slice(byte[] src, int offset, int count)
        {
            var r = new byte[count];
            Buffer.BlockCopy(src, offset, r, 0, count);
            return r;
        }

        private static BigInteger Mod(BigInteger v, BigInteger m)
        {
            var r = v % m;
            return r.Sign < 0 ? r + m : r;
        }

        private static BigInteger Inverse(BigInteger a, BigInteger m)
        {
            BigInteger t = 0, newT = 1, r = m, newR = Mod(a, m);
            while (!newR.IsZero)
            {
                var q = r / newR;
                var tmp = t - q * newT; t = newT; newT = tmp;
                tmp = r - q * newR; r = newR; newR = tmp;
            }
            if (r != BigInteger.One) throw new ArgumentException("value is not invertible");
            return t.Sign < 0 ? t + m : t;
        }

        private static Point Double(Point p)
        {
            if (p.Infinity || p.Y.IsZero) return Point.AtInfinity;
            var lambda = Mod((3 * p.X * p.X + A) * Inverse(Mod(2 * p.Y, P), P), P);
            var x3 = Mod(lambda * lambda - 2 * p.X, P);
            var y3 = Mod(lambda * (p.X - x3) - p.Y, P);
            return new Point(x3, y3);
        }

        private static Point Add(Point p, Point q)
        {
            if (p.Infinity) return q;
            if (q.Infinity) return p;
            if (p.X == q.X)
                return p.Y == q.Y ? Double(p) : Point.AtInfinity;
            var lambda = Mod((q.Y - p.Y) * Inverse(Mod(q.X - p.X, P), P), P);
            var x3 = Mod(lambda * lambda - p.X - q.X, P);
            var y3 = Mod(lambda * (p.X - x3) - p.Y, P);
            return new Point(x3, y3);
        }

        private static Point MultiplyAdd(BigInteger u1, BigInteger u2, Point q)
        {
            var gq = Add(G, q);
            var bits = (int)Math.Max(BitLength(u1), BitLength(u2));
            var acc = Point.AtInfinity;
            for (var i = bits - 1; i >= 0; i--)
            {
                acc = Double(acc);
                var b1 = !((u1 >> i) & BigInteger.One).IsZero;
                var b2 = !((u2 >> i) & BigInteger.One).IsZero;
                if (b1 && b2) acc = Add(acc, gq);
                else if (b1) acc = Add(acc, G);
                else if (b2) acc = Add(acc, q);
            }
            return acc;
        }

        private static long BitLength(BigInteger v)
        {
            long n = 0;
            while (!v.IsZero) { v >>= 1; n++; }
            return n;
        }
        #endregion
    }
}
