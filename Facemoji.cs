using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OpenCvSharp;

namespace PhotoBooth;

/// <summary>
/// Karakter 3D ala Memoji. Setiap karakter disusun dari elipsoid berlampu (kepala, telinga, mata, hidung, mulut...) lalu
/// di-ray-cast per piksel dengan z-buffer, memakai orientasi kepala 3D (sumbu U/V/N dari landmark) sehingga ikut menoleh,
/// mengangguk, dan miring. Ekspresi dihitung dari 478 landmark: kedip, mulut terbuka, senyum, mencucu, alis naik/marah,
/// dan arah pandang. Dirender di CPU (paralel) pada resolusi dibatasi lalu diperhalus; logika identik dengan
/// tools/facemoji_proto.py yang dipakai untuk memeriksa tampilan.
/// </summary>
public static class Facemoji
{
    // =============================================================== ekspresi

    sealed class Expr
    {
        public float OpenA = 1, OpenB = 1, Jaw, Smile, Pucker, BrowA, BrowB, GazeX, GazeY, Swing;
        public Expr Clone() => (Expr)MemberwiseClone();
    }

    sealed class State
    {
        public Expr Cur = new();
        public float BaseA = -1, BaseB = -1, PrevRoll, PrevCx, Swing;
        public bool Has;
    }

    static readonly State St = new();

    static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static float Dist(Vector2 a, Vector2 b) => Vector2.Distance(a, b);

    /// <summary>Ekspresi mentah dari satu frame. A = mata/alis yang tampil di sisi kiri layar.</summary>
    static Expr Compute(FaceData f, float baseA, float baseB, out float dA, out float dB)
    {
        var p = f.P;
        float fw = f.FaceW;

        // dua kelompok mata: (159,145,33,133,alis 105) dan (386,374,362,263,alis 334); A = yang paling dekat iris kiri
        var c1 = f.Mid(33, 133);
        var c2 = f.Mid(362, 263);
        bool swap = Dist(f.EyeA, c1) > Dist(f.EyeA, c2);
        int uA = swap ? 386 : 159, lA = swap ? 374 : 145, aA = swap ? 362 : 33, bA2 = swap ? 263 : 133, brA = swap ? 334 : 105;
        int uB = swap ? 159 : 386, lB = swap ? 145 : 374, aB = swap ? 33 : 362, bB2 = swap ? 133 : 263, brB = swap ? 105 : 334;

        float earA = Dist(p[uA], p[lA]) / Math.Max(1f, Dist(p[aA], p[bA2]));
        float earB = Dist(p[uB], p[lB]) / Math.Max(1f, Dist(p[aB], p[bB2]));

        var e = new Expr
        {
            OpenA = Clamp01((earA - 0.10f) / 0.14f),
            OpenB = Clamp01((earB - 0.10f) / 0.14f),
            Jaw = Clamp01(f.MouthOpen * 2.2f),
        };

        float wRatio = Dist(p[61], p[291]) / fw;
        float raise = Vector2.Dot(f.Mid(13, 14) - f.Mid(61, 291), f.V3xy) / fw;   // sudut mulut lebih tinggi = senyum
        e.Smile = Clamp01(0.5f * Clamp01((wRatio - 0.36f) / 0.10f) + 0.5f * Clamp01((raise + 0.01f) / 0.06f));
        e.Pucker = Clamp01((0.36f - wRatio) / 0.08f) * (1f - e.Jaw * 0.5f);

        dA = Dist(p[brA], p[uA]) / fw;
        dB = Dist(p[brB], p[uB]) / fw;
        float bsA = baseA < 0 ? dA : baseA, bsB = baseB < 0 ? dB : baseB;
        e.BrowA = Math.Clamp((dA - bsA) / 0.035f, -1f, 1f);
        e.BrowB = Math.Clamp((dB - bsB) / 0.035f, -1f, 1f);

        var mA = Mid(p, aA, bA2);
        var mB = Mid(p, aB, bB2);
        float hwA = Math.Max(1f, Dist(p[aA], p[bA2]) / 2f), hwB = Math.Max(1f, Dist(p[aB], p[bB2]) / 2f);
        float gx = (Vector2.Dot(f.EyeA - mA, f.U3xy) / hwA + Vector2.Dot(f.EyeB - mB, f.U3xy) / hwB) * 0.5f;
        float gy = (Vector2.Dot(f.EyeA - mA, f.V3xy) / hwA + Vector2.Dot(f.EyeB - mB, f.V3xy) / hwB) * 0.5f;
        e.GazeX = Math.Clamp(gx * 2.2f, -1f, 1f);
        e.GazeY = Math.Clamp(gy * 2.5f, -1f, 1f);
        return e;
    }

    static Vector2 Mid(Vector2[] p, int a, int b) => (p[a] + p[b]) * 0.5f;

    /// <summary>Ekspresi yang dihaluskan antar-frame (kedip cepat, lainnya lebih tenang) + ayunan telinga dari gerakan kepala.</summary>
    static Expr Smoothed(FaceData f)
    {
        var raw = Compute(f, St.BaseA, St.BaseB, out float dA, out float dB);
        if (St.BaseA < 0) { St.BaseA = dA; St.BaseB = dB; }
        St.BaseA += (dA - St.BaseA) * 0.004f;       // acuan alis netral beradaptasi pelan
        St.BaseB += (dB - St.BaseB) * 0.004f;

        var c = St.Cur;
        c.OpenA = Lerp(c.OpenA, raw.OpenA, 0.65f);
        c.OpenB = Lerp(c.OpenB, raw.OpenB, 0.65f);
        c.Jaw = Lerp(c.Jaw, raw.Jaw, 0.55f);
        c.Smile = Lerp(c.Smile, raw.Smile, 0.40f);
        c.Pucker = Lerp(c.Pucker, raw.Pucker, 0.40f);
        c.BrowA = Lerp(c.BrowA, raw.BrowA, 0.40f);
        c.BrowB = Lerp(c.BrowB, raw.BrowB, 0.40f);
        c.GazeX = Lerp(c.GazeX, raw.GazeX, 0.45f);
        c.GazeY = Lerp(c.GazeY, raw.GazeY, 0.45f);

        float cx = f.Mid(10, 152).X;
        if (St.Has)
        {
            float impulse = Math.Clamp((f.Roll - St.PrevRoll) * 12f + (cx - St.PrevCx) / f.FaceW * -2.5f, -1f, 1f);
            St.Swing = St.Swing * 0.85f + impulse * 0.5f;
        }
        St.PrevRoll = f.Roll;
        St.PrevCx = cx;
        St.Has = true;
        c.Swing = Math.Clamp(St.Swing, -1f, 1f);
        return c.Clone();
    }

    // =============================================================== model

    sealed class Prim
    {
        public Vector3 C, R, Col;
        public float Alpha = 1f, Spec = 0.3f, Shin = 30f;
        public bool Unlit;
    }

    sealed class Rig
    {
        public readonly List<Prim> P = new();
        public readonly Expr E;
        public Rig(Expr e) { E = e; }

        public void Add(float cx, float cy, float cz, float rx, float ry, float rz, uint rgb,
                        float spec = 0.3f, float shin = 30f, float alpha = 1f, bool unlit = false)
            => P.Add(new Prim { C = new Vector3(cx, cy, cz), R = new Vector3(rx, ry, rz), Col = Hex(rgb),
                                Spec = spec, Shin = shin, Alpha = alpha, Unlit = unlit });
    }

    static Vector3 Hex(uint c) => new(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);

    /// <summary>Permukaan depan sebuah elipsoid; dipakai untuk menempelkan fitur wajah tepat di atas kepala/moncong.</summary>
    readonly struct Surf
    {
        readonly float cx, cy, cz, rx, ry, rz;
        public Surf(float cx, float cy, float cz, float rx, float ry, float rz)
        { this.cx = cx; this.cy = cy; this.cz = cz; this.rx = rx; this.ry = ry; this.rz = rz; }

        public float Z(float x, float y)
        {
            float q = 1f - (x - cx) * (x - cx) / (rx * rx) - (y - cy) * (y - cy) / (ry * ry);
            return q <= 0 ? -1e6f : cz + rz * MathF.Sqrt(q);
        }
    }

    static Func<float, float, float> On(params Surf[] s) => (x, y) =>
    {
        float m = -1e6f;
        foreach (var q in s) m = MathF.Max(m, q.Z(x, y));
        return m < -1e5f ? 0f : m;
    };

    static Surf Blob(Rig g, float cx, float cy, float cz, float rx, float ry, float rz, uint col,
                     float spec = 0.3f, float shin = 30f)
    {
        g.Add(cx, cy, cz, rx, ry, rz, col, spec, shin);
        return new Surf(cx, cy, cz, rx, ry, rz);
    }

    static Surf MakeHead(Rig g, float rx, float ry, float rz, uint col, float spec = 0.35f)
        => Blob(g, 0, 0, 0, rx, ry, rz, col, spec, 35f);

    enum EyeStyle { Ball, Dot, Big }
    enum NoseStyle { Round, Tiny, Snout }

    static void Eyes(Rig g, Func<float, float, float> z, EyeStyle style, float ex, float ey, float r,
                     uint pupil = 0x16161A, uint white = 0xFFFFFF, float zoff = 0f)
    {
        var e = g.E;
        for (int side = -1; side <= 1; side += 2)
        {
            float op = side < 0 ? e.OpenA : e.OpenB;
            float x = side * ex, y = ey, zz = z(x, y) + zoff;
            float lid = Lerp(0.10f, 1f, op);
            switch (style)
            {
                case EyeStyle.Ball:
                    g.Add(x, y, zz - 0.005f, r, r * 1.15f * lid, 0.06f, white, 0.5f, 50f);
                    if (op > 0.25f)
                    {
                        float gx = e.GazeX * r * 0.45f, gy = e.GazeY * r * 0.40f;
                        g.Add(x + gx, y + gy, zz + 0.045f, r * 0.58f, r * 0.62f * MathF.Max(0.35f, lid), 0.03f, pupil, 0.9f, 70f);
                        g.Add(x + gx - r * 0.18f, y + gy - r * 0.22f, zz + 0.075f, r * 0.14f, r * 0.14f, 0.02f, 0xFFFFFF, 0f, 1f, 1f, true);
                    }
                    else
                    {
                        g.Add(x, y + r * 0.05f, zz + 0.03f, r * 1.05f, 0.012f, 0.03f, 0x2A2022, 0f, 1f, 1f, true);   // mata terpejam
                    }
                    break;
                case EyeStyle.Dot:
                    g.Add(x, y, zz + 0.005f, r * 0.7f, r * 0.8f * lid, 0.05f, pupil, 0.9f, 70f);
                    if (op > 0.4f)
                        g.Add(x - r * 0.2f, y - r * 0.25f * lid, zz + 0.05f, r * 0.17f, r * 0.17f, 0.02f, 0xFFFFFF, 0f, 1f, 1f, true);
                    break;
                case EyeStyle.Big:
                    g.Add(x, y, zz, r * 1.35f, r * 1.9f * lid, 0.07f, pupil, 0.9f, 80f);
                    if (op > 0.4f)
                        g.Add(x - r * 0.35f, y - r * 0.6f * lid, zz + 0.06f, r * 0.28f, r * 0.28f, 0.02f, 0xFFFFFF, 0f, 1f, 1f, true);
                    break;
            }
        }
    }

    static void Nose(Rig g, Func<float, float, float> z, NoseStyle style, float y, uint col, uint nostril = 0x2A1A1C)
    {
        float zz = z(0, y);
        switch (style)
        {
            case NoseStyle.Round:
                g.Add(0, y, zz, 0.075f, 0.055f, 0.06f, col, 0.9f, 70f);
                break;
            case NoseStyle.Tiny:
                g.Add(0, y, zz, 0.04f, 0.03f, 0.04f, col, 0.8f, 60f);
                break;
            case NoseStyle.Snout:
                g.Add(0, y + 0.02f, zz + 0.03f, 0.18f, 0.13f, 0.09f, col, 0.5f, 40f);
                g.Add(-0.06f, y + 0.02f, zz + 0.115f, 0.025f, 0.04f, 0.02f, nostril, 0.2f, 10f);
                g.Add(0.06f, y + 0.02f, zz + 0.115f, 0.025f, 0.04f, 0.02f, nostril, 0.2f, 10f);
                break;
        }
    }

    /// <summary>Mulut: tertutup = garis manik yang melengkung saat senyum; terbuka = rongga gelap dengan lidah.</summary>
    static void Mouth(Rig g, Func<float, float, float> z, float my, float half0,
                      uint inner = 0x5A1822, uint line = 0x2B1A1C, uint tongue = 0xE2707C)
    {
        var e = g.E;
        float half = half0 * (1f + 0.35f * e.Smile - 0.45f * e.Pucker);
        float zf = z(0, my);
        if (e.Jaw > 0.12f)
        {
            float ry = 0.025f + 0.16f * e.Jaw;
            g.Add(0, my + ry * 0.5f, zf + 0.01f, half * (0.9f - 0.25f * e.Pucker), ry, 0.05f, inner, 0f, 1f, 1f, true);
            g.Add(0, my + ry * 0.95f, zf + 0.025f, half * 0.5f, ry * 0.45f, 0.04f, tongue, 0.3f, 20f);
        }
        else
        {
            for (int i = 0; i < 11; i++)
            {
                float u = i / 5f - 1f, x = u * half;
                float y = my + 0.03f * e.Smile - 0.06f * e.Smile * u * u;
                g.Add(x, y, z(x, y) + 0.005f, 0.022f, 0.016f, 0.02f, line, 0.3f, 20f);
            }
        }
    }

    static void Brows(Rig g, Func<float, float, float> z, uint col, float ex, float ey, float ln = 0.12f, float thick = 0.03f)
    {
        var e = g.E;
        for (int side = -1; side <= 1; side += 2)
        {
            float br = side < 0 ? e.BrowA : e.BrowB;        // + naik, - menukik marah
            for (int i = 0; i < 5; i++)
            {
                float u = (i - 2) / 2f;
                float x = side * ex + u * ln * 0.55f;
                float inner = -side * u;
                float tilt = MathF.Max(0f, -br) * 0.05f * inner;
                float y = ey - 0.17f - 0.07f * br + tilt - 0.02f * (1f - MathF.Abs(u)) * MathF.Max(0f, br);
                g.Add(x, y, z(x, y), ln * 0.30f, thick, 0.03f, col, 0.2f, 20f);
            }
        }
    }

    static void Blush(Rig g, Func<float, float, float> z, uint col, float x = 0.40f, float y = 0.16f)
    {
        float a = 0.35f + 0.4f * g.E.Smile;
        for (int s = -1; s <= 1; s += 2)
        {
            float xx = s * x;
            g.Add(xx, y, z(xx, y) + 0.005f, 0.09f, 0.055f, 0.02f, col, 0f, 1f, a, true);
        }
    }

    // =============================================================== karakter

    static void Bear(Rig g)
    {
        var h = MakeHead(g, .62f, .68f, .60f, 0xA0693A);
        float sw = g.E.Swing * .04f;
        foreach (int s in new[] { -1, 1 })
        {
            g.Add(s * .46f + sw * s, -.56f, -.02f, .18f, .18f, .12f, 0xA0693A);
            g.Add(s * .46f + sw * s, -.56f, .07f, .105f, .105f, .06f, 0xE0B88A);
        }
        var mz = Blob(g, 0, .22f, h.Z(0, .22f) - .03f, .23f, .18f, .12f, 0xE4C49C);
        var z = On(h, mz);
        Eyes(g, z, EyeStyle.Dot, .25f, -.08f, .10f);
        Brows(g, z, 0x5A3A1C, .25f, -.08f);
        Nose(g, z, NoseStyle.Round, .13f, 0x2A1A1C);
        Mouth(g, z, .31f, .12f);
        Blush(g, z, 0xFF9AA2);
    }

    static void Cat(Rig g)
    {
        var h = MakeHead(g, .64f, .58f, .55f, 0xE8944A);
        float sw = g.E.Swing * .04f;
        foreach (int s in new[] { -1, 1 })
        {
            g.Add(s * .40f + sw * s, -.60f, 0, .13f, .24f, .08f, 0xE8944A);
            g.Add(s * .40f + sw * s, -.58f, .055f, .07f, .15f, .05f, 0xF5A0A8);
        }
        foreach (float x in new[] { -.14f, 0f, .14f })
            g.Add(x, -.40f, h.Z(x, -.40f), .025f, .08f, .03f, 0xB5651D, 0f, 1f, .85f, true);
        var mz = Blob(g, 0, .20f, h.Z(0, .20f) - .02f, .18f, .13f, .09f, 0xFFF1DC);
        var z = On(h, mz);
        Eyes(g, z, EyeStyle.Ball, .27f, -.06f, .105f, 0x2E7D32);
        Nose(g, z, NoseStyle.Tiny, .12f, 0xF08A9A);
        Mouth(g, z, .27f, .09f);
        foreach (int k in new[] { -1, 0, 1 })
            foreach (int s in new[] { -1, 1 })
                g.Add(s * .46f, .20f + k * .05f, h.Z(s * .30f, .20f) + .03f, .20f, .007f, .01f, 0xFFFFFF, 0f, 1f, .9f, true);
        Brows(g, z, 0xB5651D, .27f, -.06f, .1f, .022f);
    }

    static void Dog(Rig g)
    {
        var h = MakeHead(g, .62f, .66f, .58f, 0xC8924B);
        float sw = g.E.Swing * .05f;
        foreach (int s in new[] { -1, 1 })
            g.Add(s * .62f + sw * s, -.10f, -.05f, .14f, .34f, .12f, 0x7A4A21);
        var mz = Blob(g, 0, .22f, h.Z(0, .22f) - .03f, .24f, .18f, .12f, 0xF2DDB8);
        var z = On(h, mz);
        Eyes(g, z, EyeStyle.Ball, .26f, -.10f, .10f);
        Brows(g, z, 0x6B431C, .26f, -.10f);
        Nose(g, z, NoseStyle.Round, .14f, 0x1E1416);
        Mouth(g, z, .32f, .13f);
        Blush(g, z, 0xFF9AA2, .42f, .12f);
    }

    static void Fox(Rig g)
    {
        var h = MakeHead(g, .62f, .56f, .55f, 0xE2742B);
        float sw = g.E.Swing * .04f;
        foreach (int s in new[] { -1, 1 })
        {
            g.Add(s * .40f + sw * s, -.64f, 0, .12f, .26f, .07f, 0xE2742B);
            g.Add(s * .40f + sw * s, -.66f, .06f, .07f, .15f, .04f, 0x2A2A2A);
            g.Add(s * .30f, .22f, h.Z(s * .30f, .22f) - .05f, .16f, .11f, .09f, 0xFFFFFF);
        }
        var mz = Blob(g, 0, .20f, h.Z(0, .20f) - .02f, .16f, .13f, .13f, 0xFFFFFF);
        var z = On(h, mz);
        Eyes(g, z, EyeStyle.Ball, .26f, -.07f, .10f, 0x3A2A10);
        Brows(g, z, 0x8A3C10, .26f, -.07f);
        Nose(g, z, NoseStyle.Round, .16f, 0x1A1214);
        Mouth(g, z, .30f, .10f);
    }

    static void Bunny(Rig g)
    {
        var h = MakeHead(g, .60f, .62f, .56f, 0xF3F0EC);
        float sw = g.E.Swing * .06f;
        foreach (int s in new[] { -1, 1 })
        {
            g.Add(s * .22f + sw * s, -1.0f, 0, .12f, .46f, .07f, 0xF3F0EC);
            g.Add(s * .22f + sw * s, -.98f, .05f, .06f, .36f, .04f, 0xF5B5C5);
        }
        var z = On(h);
        Eyes(g, z, EyeStyle.Dot, .24f, -.06f, .10f);
        Brows(g, z, 0xBBB4AA, .24f, -.06f);
        Nose(g, z, NoseStyle.Tiny, .12f, 0xF08FA5);
        Mouth(g, z, .26f, .09f);
        if (g.E.Jaw < .15f)
            foreach (int s in new[] { -1, 1 })
                g.Add(s * .035f, .36f + g.E.Jaw * .1f, z(0, .36f) + .01f, .035f, .055f, .03f, 0xFFFFFF, .4f, 40f);
        Blush(g, z, 0xFFA9B8);
    }

    static void Panda(Rig g)
    {
        var h = MakeHead(g, .64f, .64f, .58f, 0xF6F6F6);
        foreach (int s in new[] { -1, 1 })
        {
            g.Add(s * .46f, -.56f, -.02f, .17f, .17f, .12f, 0x1B1B1D);
            g.Add(s * .26f, -.06f, h.Z(s * .26f, -.06f) - .02f, .15f, .19f, .08f, 0x1B1B1D);
        }
        var z = On(h);
        Eyes(g, z, EyeStyle.Ball, .26f, -.06f, .075f, zoff: .04f);
        Nose(g, z, NoseStyle.Round, .15f, 0x1B1B1D);
        Mouth(g, z, .30f, .11f);
        Blush(g, z, 0xFFA9B8, .45f, .20f);
    }

    static void Pig(Rig g)
    {
        var h = MakeHead(g, .66f, .62f, .58f, 0xF7A8B8);
        float sw = g.E.Swing * .05f;
        foreach (int s in new[] { -1, 1 })
            g.Add(s * .48f + sw * s, -.52f, .02f, .16f, .15f, .07f, 0xE88C9F);
        var z = On(h);
        Eyes(g, z, EyeStyle.Dot, .26f, -.12f, .10f);
        Nose(g, z, NoseStyle.Snout, .13f, 0xF08FA5);
        Mouth(g, z, .38f, .13f, line: 0x8A3A4A);
        Blush(g, z, 0xFF7E94, .46f, .12f);
    }

    static void Monkey(Rig g)
    {
        var h = MakeHead(g, .62f, .62f, .56f, 0x8A5A3B);
        foreach (int s in new[] { -1, 1 })
        {
            g.Add(s * .66f, -.02f, -.02f, .15f, .15f, .10f, 0x8A5A3B);
            g.Add(s * .66f, -.02f, .06f, .09f, .09f, .05f, 0xE8C39E);
        }
        var p1 = Blob(g, -.17f, -.05f, h.Z(-.17f, -.05f) - .06f, .22f, .20f, .12f, 0xE9C29B);
        var p2 = Blob(g, .17f, -.05f, h.Z(.17f, -.05f) - .06f, .22f, .20f, .12f, 0xE9C29B);
        var p3 = Blob(g, 0, .22f, h.Z(0, .22f) - .05f, .30f, .25f, .12f, 0xE9C29B);
        var z = On(h, p1, p2, p3);
        Eyes(g, z, EyeStyle.Ball, .22f, -.06f, .09f, zoff: .01f);
        Brows(g, z, 0x4A2E1A, .22f, -.06f);
        foreach (int s in new[] { -1, 1 })
            g.Add(s * .035f, .14f, z(0, .14f) + .02f, .02f, .028f, .02f, 0x4A2E1A, .5f, 30f);
        Mouth(g, z, .30f, .12f, line: 0x4A2E1A);
    }

    static void Lion(Rig g)
    {
        var h = MakeHead(g, .55f, .58f, .55f, 0xE0A64A);
        for (int i = 0; i < 14; i++)
        {
            float a = i / 14f * MathF.Tau;
            g.Add(MathF.Cos(a) * .70f, MathF.Sin(a) * .74f, -.10f, .22f, .22f, .20f, 0xB0701E);
        }
        foreach (int s in new[] { -1, 1 })
        {
            g.Add(s * .38f, -.55f, .02f, .10f, .10f, .07f, 0xE0A64A);
            g.Add(s * .38f, -.55f, .07f, .06f, .06f, .04f, 0xE9B5A0);
        }
        var mz = Blob(g, 0, .20f, h.Z(0, .20f) - .02f, .20f, .15f, .10f, 0xF6E2B5);
        var z = On(h, mz);
        Eyes(g, z, EyeStyle.Ball, .22f, -.07f, .09f);
        Brows(g, z, 0x8A5A18, .22f, -.07f);
        Nose(g, z, NoseStyle.Round, .12f, 0x3A2A24);
        Mouth(g, z, .29f, .10f);
    }

    static void Frog(Rig g)
    {
        var h = MakeHead(g, .68f, .52f, .55f, 0x5FBF4A);
        var e = g.E;
        foreach (int s in new[] { -1, 1 })
        {
            float op = s < 0 ? e.OpenA : e.OpenB;
            g.Add(s * .32f, -.48f, .20f, .19f, .19f, .17f, 0x5FBF4A);
            g.Add(s * .32f, -.48f, .34f, .13f, .13f * Lerp(.12f, 1f, op), .06f, 0xFFFFFF, .5f, 50f);
            if (op > .25f)
                g.Add(s * .32f + e.GazeX * .05f, -.48f + e.GazeY * .04f, .40f, .065f, .07f * MathF.Max(.35f, op), .03f, 0x16161A, .9f, 70f);
        }
        var z = On(h);
        foreach (int s in new[] { -1, 1 })
            g.Add(s * .05f, .02f, z(s * .05f, .02f) + .005f, .015f, .015f, .02f, 0x2A4A24);
        Mouth(g, z, .30f, .30f, line: 0x2A4A24);
        Blush(g, z, 0xFF8FA0, .45f, .18f);
    }

    static void Alien(Rig g)
    {
        var h = MakeHead(g, .60f, .78f, .55f, 0x8FD16A);
        foreach (int s in new[] { -1, 1 })
        {
            g.Add(s * .22f, -.88f, -.02f, .015f, .12f, .015f, 0x6BAA4A);
            g.Add(s * .22f, -1.0f, -.02f, .06f, .06f, .06f, 0xB6FF8A, 0f, 1f, 1f, true);
        }
        var z = On(h);
        Eyes(g, z, EyeStyle.Big, .26f, -.02f, .13f, 0x0E0E16);
        foreach (int s in new[] { -1, 1 })
            g.Add(s * .03f, .20f, z(s * .03f, .20f) + .005f, .012f, .02f, .02f, 0x4A8A32);
        Mouth(g, z, .42f, .10f, line: 0x3A6A28);
    }

    static void Robot(Rig g)
    {
        var h = MakeHead(g, .66f, .62f, .56f, 0xB8C4D0, .6f);
        var e = g.E;
        g.Add(0, -.70f, 0, .018f, .10f, .018f, 0x8895A3);
        g.Add(0, -.82f, 0, .055f, .055f, .055f, 0xFF4A4A, 0f, 1f, 1f, true);
        foreach (int s in new[] { -1, 1 })
            g.Add(s * .70f, 0, 0, .10f, .16f, .12f, 0x8895A3);
        var z = On(h);
        foreach (int s in new[] { -1, 1 })
        {
            float op = s < 0 ? e.OpenA : e.OpenB;
            g.Add(s * .27f, -.08f, z(s * .27f, -.08f) + .005f, .13f, .02f + .09f * op, .03f, 0x39D0FF, 0f, 1f, 1f, true);
        }
        for (int i = 0; i < 5; i++)
        {
            float x = (i - 2) * .075f;
            g.Add(x, .30f + .04f * e.Jaw, z(x, .30f) + .005f, .032f, .02f + .10f * e.Jaw, .03f, 0x39D0FF, 0f, 1f, 1f, true);
        }
        foreach (int s in new[] { -1, 1 })
            g.Add(s * .52f, .30f, z(s * .52f, .30f) + .005f, .025f, .025f, .02f, 0x6A7684);
    }

    static void Ghost(Rig g)
    {
        var h = MakeHead(g, .66f, .78f, .55f, 0xF5F5FF, .5f);
        var z = On(h);
        var e = g.E;
        foreach (int s in new[] { -1, 1 })
        {
            float op = s < 0 ? e.OpenA : e.OpenB;
            g.Add(s * .22f, -.10f, z(s * .22f, -.10f) + .01f, .085f, .15f * Lerp(.12f, 1f, op), .04f, 0x18181F, .6f, 60f);
        }
        float ry = .05f + .20f * e.Jaw;
        g.Add(0, .22f + ry * .5f, z(0, .22f) + .01f, .11f, ry, .04f, 0x18181F, 0f, 1f, 1f, true);
        Blush(g, z, 0xFFB0C0, .38f, .14f);
    }

    static void Unicorn(Rig g)
    {
        var h = MakeHead(g, .60f, .64f, .56f, 0xF6EEF8);
        float sw = g.E.Swing * .04f;
        uint[] cols = { 0xFF8FC8, 0xB78CFF, 0x7FC8FF, 0xFF8FC8, 0xB78CFF };
        for (int i = 0; i < cols.Length; i++)
        {
            g.Add(.52f, -.40f + i * .22f, -.12f, .14f, .16f, .12f, cols[i]);
            g.Add(-.52f, -.40f + i * .22f, -.12f, .14f, .16f, .12f, cols[i]);
        }
        for (int i = 0; i < 5; i++)
            g.Add(0, -.62f - i * .10f, h.Z(0, -.5f) * .6f, .095f - i * .012f, .08f, .085f - i * .012f, 0xFFD36B, .8f, 60f);
        foreach (int s in new[] { -1, 1 })
        {
            g.Add(s * .36f + sw * s, -.62f, -.02f, .09f, .16f, .07f, 0xF6EEF8);
            g.Add(s * .36f + sw * s, -.60f, .04f, .05f, .10f, .04f, 0xF5B5C5);
        }
        var z = On(h);
        Eyes(g, z, EyeStyle.Ball, .24f, -.05f, .10f, 0x7A4FD0);
        Nose(g, z, NoseStyle.Tiny, .13f, 0xE8A0C0);
        Mouth(g, z, .28f, .10f);
        Blush(g, z, 0xFF9AB8);
    }

    static void Skull(Rig g)
    {
        var h = MakeHead(g, .60f, .66f, .56f, 0xF1EFE6);
        var e = g.E;
        var z = On(h);
        foreach (int s in new[] { -1, 1 })
        {
            float op = s < 0 ? e.OpenA : e.OpenB;
            g.Add(s * .25f, -.05f, z(s * .25f, -.05f) + .01f, .15f, .17f * Lerp(.3f, 1f, op), .08f, 0x141416, 0f, 1f, 1f, true);
            g.Add(s * .25f + e.GazeX * .04f, -.05f + e.GazeY * .04f, z(s * .25f, -.05f) + .08f, .03f, .03f, .02f, 0xFFFFFF, 0f, 1f, 1f, true);
        }
        g.Add(0, .15f, z(0, .15f) + .01f, .05f, .07f, .05f, 0x141416, 0f, 1f, 1f, true);
        float j = e.Jaw * .18f;
        g.Add(0, .36f + j * .5f, z(0, .36f) + .01f, .25f, .02f + j * .6f, .04f, 0x141416, 0f, 1f, 1f, true);
        for (int i = 0; i < 6; i++)
        {
            float x = (i - 2.5f) * .075f;
            g.Add(x, .335f, z(x, .335f) + .03f, .035f, .045f, .03f, 0xFFFFFF, .4f, 40f);
            g.Add(x, .39f + j, z(x, .39f) + .03f, .035f, .045f, .03f, 0xFFFFFF, .4f, 40f);
        }
    }

    static void Chicken(Rig g)
    {
        var h = MakeHead(g, .62f, .64f, .58f, 0xFFFFFF);
        foreach (var (x, y) in new[] { (0f, -.72f), (-.13f, -.68f), (.13f, -.68f) })
            g.Add(x, y, 0, .10f, .10f, .08f, 0xE02626);
        var z = On(h);
        var e = g.E;
        Eyes(g, z, EyeStyle.Dot, .25f, -.08f, .10f);
        float zf = z(0, .14f);
        g.Add(0, .14f, zf + .12f, .13f, .06f, .15f, 0xFFA21A, .5f, 40f);
        g.Add(0, .22f + e.Jaw * .12f, zf + .10f, .10f, .04f, .12f, 0xFF9A10, .5f, 40f);
        g.Add(0, .36f + e.Jaw * .12f, zf, .05f, .08f, .05f, 0xE02626);
        Blush(g, z, 0xFFB0B8, .42f, .14f);
    }

    static void Koala(Rig g)
    {
        var h = MakeHead(g, .62f, .62f, .58f, 0x9AA0A6);
        foreach (int s in new[] { -1, 1 })
        {
            g.Add(s * .55f, -.50f, -.04f, .27f, .27f, .15f, 0x9AA0A6);
            g.Add(s * .55f, -.50f, .10f, .16f, .16f, .07f, 0xE8E8EA);
        }
        var z = On(h);
        Eyes(g, z, EyeStyle.Dot, .25f, -.06f, .08f);
        g.Add(0, .12f, z(0, .12f), .11f, .15f, .07f, 0x2A2A2E, .9f, 70f);
        Mouth(g, z, .36f, .11f);
        Blush(g, z, 0xFFB0B8, .43f, .17f);
    }

    static void Dino(Rig g)
    {
        var h = MakeHead(g, .60f, .60f, .56f, 0x6CCB5F);
        foreach (var (x, y) in new[] { (-.30f, -.52f), (-.15f, -.65f), (0f, -.70f), (.15f, -.65f), (.30f, -.52f) })
            g.Add(x, y, 0, .09f, .09f, .08f, 0xF2E394);
        var mz = Blob(g, 0, .20f, h.Z(0, .20f) - .02f, .30f, .20f, .20f, 0x8FDD7E);
        var z = On(h, mz);
        Eyes(g, z, EyeStyle.Ball, .24f, -.10f, .095f);
        Brows(g, z, 0x3E8A34, .24f, -.10f);
        foreach (int s in new[] { -1, 1 })
            g.Add(s * .07f, .16f, z(s * .07f, .16f) + .01f, .02f, .028f, .02f, 0x2A5A24);
        Mouth(g, z, .32f, .16f, line: 0x2A5A24);
    }

    static readonly Dictionary<EffectKind, Action<Rig>> Builders = new()
    {
        [EffectKind.FmBear] = Bear, [EffectKind.FmCat] = Cat, [EffectKind.FmDog] = Dog,
        [EffectKind.FmFox] = Fox, [EffectKind.FmBunny] = Bunny, [EffectKind.FmPanda] = Panda,
        [EffectKind.FmPig] = Pig, [EffectKind.FmMonkey] = Monkey, [EffectKind.FmLion] = Lion,
        [EffectKind.FmFrog] = Frog, [EffectKind.FmAlien] = Alien, [EffectKind.FmRobot] = Robot,
        [EffectKind.FmGhost] = Ghost, [EffectKind.FmUnicorn] = Unicorn, [EffectKind.FmSkull] = Skull,
        [EffectKind.FmChicken] = Chicken, [EffectKind.FmKoala] = Koala, [EffectKind.FmDino] = Dino,
    };

    public static bool Supports(EffectKind k) => Builders.ContainsKey(k);

    // =============================================================== renderer

    static readonly Vector3 Light = Vector3.Normalize(new Vector3(-0.40f, -0.60f, -0.70f));
    static readonly Vector3 Half = Vector3.Normalize(Light + new Vector3(0, 0, -1));

    static float[] zb = Array.Empty<float>(), cr = Array.Empty<float>(), cg = Array.Empty<float>(),
                   cb = Array.Empty<float>(), ca = Array.Empty<float>();
    static byte[] pix = Array.Empty<byte>();

    public static void Render(Mat frame, FaceData f, EffectKind kind)
    {
        if (!Builders.TryGetValue(kind, out var build) || f.P.Length < 468) return;
        var e = f.Preview ? Compute(f, -1f, -1f, out _, out _) : Smoothed(f);
        var rig = new Rig(e);
        build(rig);
        Draw(frame, f, rig.P);
    }

    static void Draw(Mat frame, FaceData f, List<Prim> prims)
    {
        Vector3 U = f.U3, V = f.V3, N = f.N3;
        Vector2 uxy = f.U3xy, vxy = f.V3xy, nxy = f.N3xy;
        float s = f.FaceW;
        Vector2 head = f.Mid(10, 152) - vxy * (f.FaceW * 0.02f);

        // area gambar = gabungan bola pembatas semua primitif
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in prims)
        {
            var pc = head + (uxy * p.C.X + vxy * p.C.Y + nxy * p.C.Z) * s;
            float rad = MathF.Max(p.R.X, MathF.Max(p.R.Y, p.R.Z)) * s + 2f;
            minX = MathF.Min(minX, pc.X - rad); maxX = MathF.Max(maxX, pc.X + rad);
            minY = MathF.Min(minY, pc.Y - rad); maxY = MathF.Max(maxY, pc.Y + rad);
        }
        int x0 = Math.Max(0, (int)minX), y0 = Math.Max(0, (int)minY);
        int x1 = Math.Min(frame.Cols, (int)maxX + 1), y1 = Math.Min(frame.Rows, (int)maxY + 1);
        int rw = x1 - x0, rh = y1 - y0;
        if (rw < 8 || rh < 8) return;

        // resolusi render dibatasi agar tetap lancar; hasil diperbesar dengan interpolasi halus
        float sc = MathF.Min(1f, 360f / Math.Max(rw, rh));
        int bw = Math.Max(8, (int)MathF.Ceiling(rw * sc)), bh = Math.Max(8, (int)MathF.Ceiling(rh * sc));
        int n = bw * bh;
        if (zb.Length < n)
        {
            zb = new float[n]; cr = new float[n]; cg = new float[n]; cb = new float[n]; ca = new float[n];
        }
        Array.Fill(zb, float.MaxValue, 0, n);
        Array.Clear(cr, 0, n); Array.Clear(cg, 0, n); Array.Clear(cb, 0, n); Array.Clear(ca, 0, n);

        float cxb = (head.X - x0) * sc, cyb = (head.Y - y0) * sc, Sb = s * sc;
        float invS = 1f / Sb;
        var dirL = new Vector3(U.Z, V.Z, N.Z) * invS;

        foreach (var p in prims)
        {
            var pc = new Vector2(cxb, cyb) + (uxy * p.C.X + vxy * p.C.Y + nxy * p.C.Z) * Sb;
            float rad = MathF.Max(p.R.X, MathF.Max(p.R.Y, p.R.Z)) * Sb + 2f;
            int px0 = Math.Max(0, (int)(pc.X - rad)), px1 = Math.Min(bw, (int)(pc.X + rad) + 1);
            int py0 = Math.Max(0, (int)(pc.Y - rad)), py1 = Math.Min(bh, (int)(pc.Y + rad) + 1);
            if (px1 <= px0 || py1 <= py0) continue;

            var invR = new Vector3(1f / p.R.X, 1f / p.R.Y, 1f / p.R.Z);
            var dp = dirL * invR;
            float a = Vector3.Dot(dp, dp);
            if (a < 1e-9f) continue;
            float invA = 1f / a;
            float rpx = Sb * MathF.Pow(p.R.X * p.R.Y * p.R.Z, 1f / 3f);
            float edge = 2f / rpx;
            var prim = p;

            void Row(int y)
            {
                float dy = y + 0.5f - cyb;
                for (int x = px0; x < px1; x++)
                {
                    float dx = x + 0.5f - cxb;
                    var ol = new Vector3(U.X * dx + U.Y * dy, V.X * dx + V.Y * dy, N.X * dx + N.Y * dy) * invS;
                    var op = (ol - prim.C) * invR;
                    float b = Vector3.Dot(op, dp);
                    float m = Vector3.Dot(op, op) - 1f - b * b * invA;
                    if (m > edge) continue;
                    float al = Math.Clamp(0.5f - m * rpx * 0.5f, 0f, 1f) * prim.Alpha;
                    if (al < 0.01f) continue;
                    float t = -b * invA - (m < 0f ? MathF.Sqrt(-m * invA) : 0f);
                    int i = y * bw + x;

                    float oldA = ca[i];
                    bool near = t < zb[i];
                    if (!near && (1f - oldA) * al < 0.003f) continue;

                    var nl = (op + dp * t) * invR;
                    float len = nl.Length();
                    if (len < 1e-6f) continue;
                    nl /= len;
                    var nc = U * nl.X + V * nl.Y + N * nl.Z;

                    Vector3 col;
                    if (prim.Unlit)
                    {
                        col = prim.Col;
                    }
                    else
                    {
                        float diff = MathF.Max(0f, Vector3.Dot(nc, Light));
                        float spec = MathF.Pow(MathF.Max(0f, Vector3.Dot(nc, Half)), prim.Shin) * prim.Spec;
                        float rim = MathF.Pow(1f - MathF.Min(1f, MathF.Max(0f, -nc.Z)), 3f) * 0.12f;
                        col = prim.Col * (0.36f + 0.64f * diff) + new Vector3(spec + rim);
                    }
                    col = Vector3.Clamp(col, Vector3.Zero, Vector3.One);

                    if (near)
                    {
                        float inv = 1f - al;
                        cr[i] = col.X * al + cr[i] * inv;
                        cg[i] = col.Y * al + cg[i] * inv;
                        cb[i] = col.Z * al + cb[i] * inv;
                        ca[i] = al + oldA * inv;
                        if (al > 0.5f) zb[i] = t;
                    }
                    else
                    {
                        float add = al * (1f - oldA);
                        cr[i] += col.X * add;
                        cg[i] += col.Y * add;
                        cb[i] += col.Z * add;
                        ca[i] = oldA + add;
                    }
                }
            }

            if ((px1 - px0) * (py1 - py0) > 6000) Parallel.For(py0, py1, Row);
            else for (int y = py0; y < py1; y++) Row(y);
        }

        // BGRA berpremultiplikasi -> perbesar -> tempelkan ke frame
        if (pix.Length < n * 4) pix = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            pix[i * 4] = (byte)(MathF.Min(1f, cb[i]) * 255f + 0.5f);
            pix[i * 4 + 1] = (byte)(MathF.Min(1f, cg[i]) * 255f + 0.5f);
            pix[i * 4 + 2] = (byte)(MathF.Min(1f, cr[i]) * 255f + 0.5f);
            pix[i * 4 + 3] = (byte)(MathF.Min(1f, ca[i]) * 255f + 0.5f);
        }

        using var small = new Mat(bh, bw, MatType.CV_8UC4);
        Marshal.Copy(pix, 0, small.Data, n * 4);
        using var big = new Mat();
        if (bw != rw || bh != rh) Cv2.Resize(small, big, new Size(rw, rh), 0, 0, InterpolationFlags.Linear);
        else small.CopyTo(big);

        Cv2.Split(big, out Mat[] ch);
        try
        {
            using var pm = new Mat();
            Cv2.Merge(new[] { ch[0], ch[1], ch[2] }, pm);
            using var a3 = new Mat();
            Cv2.Merge(new[] { ch[3], ch[3], ch[3] }, a3);
            using var ia3 = new Mat();
            Cv2.BitwiseNot(a3, ia3);
            using var roi = new Mat(frame, new Rect(x0, y0, rw, rh));
            using var tmp = new Mat();
            Cv2.Multiply(roi, ia3, tmp, 1.0 / 255.0);
            Cv2.Add(tmp, pm, roi);
        }
        finally
        {
            foreach (var m in ch) m.Dispose();
        }
    }
}
