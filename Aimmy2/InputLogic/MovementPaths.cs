using System.Drawing;

namespace InputLogic
{
    // All paths work in PointF, not Point. They used to truncate to int on every return, which threw
    // away the fractional part of every step -- so any command below 1px became zero permanently.
    // MouseManager now carries the fraction forward in a residual accumulator instead.
    //
    // None of these apply EMA internally any more. CubicBezier used to, which meant the "Adaptive"
    // path (which calls it) got smoothed twice: once inside, once again in MoveCrosshair.
    class MovementPaths
    {
        private static readonly int[] permutation = new int[512];

        // The permutation table was previously declared and never filled, so every entry was 0.
        // With an all-zero table Grad() degenerates and Noise() stops being noise at all -- it
        // returned a smooth deterministic function, and because t is constant per tick the "Perlin
        // Noise" path was adding the SAME offset every frame: a fixed directional bias, not variation.
        static MovementPaths()
        {
            // Fixed seed: the path should be reproducible run to run, and the variation comes from
            // the input position, not from a different table each launch.
            var random = new Random(1337);
            var source = new int[256];
            for (int i = 0; i < 256; i++) source[i] = i;

            for (int i = 255; i > 0; i--)
            {
                int swap = random.Next(i + 1);
                (source[i], source[swap]) = (source[swap], source[i]);
            }

            for (int i = 0; i < 512; i++) permutation[i] = source[i & 255];
        }

        internal static PointF CubicBezier(PointF start, PointF end, PointF control1, PointF control2, double t)
        {
            double u = 1 - t;
            double tt = t * t;
            double uu = u * u;

            double x = uu * u * start.X + 3 * uu * t * control1.X + 3 * u * tt * control2.X + tt * t * end.X;
            double y = uu * u * start.Y + 3 * uu * t * control1.Y + 3 * u * tt * control2.Y + tt * t * end.Y;

            return new PointF((float)x, (float)y);
        }

        internal static PointF Lerp(PointF start, PointF end, double t)
        {
            return new PointF(
                (float)(start.X + (end.X - start.X) * t),
                (float)(start.Y + (end.Y - start.Y) * t));
        }

        // t is clamped to 1 before the power so the result can never exceed the full distance.
        // Without the clamp, Mouse Sensitivity below 0.2 produced t > 1 and t^exponent > 1, which
        // commanded MORE than the remaining distance -- guaranteed overshoot and reversal every
        // frame (e.g. Sensitivity 0.01 with exponent 5 asked for 239% of the distance).
        internal static PointF Exponential(PointF start, PointF end, double t, double exponent = 2.0)
        {
            double eased = Math.Pow(Math.Clamp(t, 0.0, 1.0), exponent);
            return new PointF(
                (float)(start.X + (end.X - start.X) * eased),
                (float)(start.Y + (end.Y - start.Y) * eased));
        }

        internal static PointF Smoothstep(PointF start, PointF end, double t)
        {
            double clamped = Math.Clamp(t, 0.0, 1.0);
            double smooth = clamped * clamped * (2.5 - 1.5 * clamped);
            return new PointF(
                (float)(start.X + (end.X - start.X) * smooth),
                (float)(start.Y + (end.Y - start.Y) * smooth));
        }

        // threshold is a DISTANCE IN PIXELS, not a "strength": under it the approach is a straight
        // lerp, over it the eased bezier is used.
        internal static PointF Adaptive(PointF start, PointF end, double t, double threshold = 100.0)
        {
            double distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
            if (distance < threshold)
            {
                return Lerp(start, end, t);
            }

            PointF control1 = new PointF(start.X + (end.X - start.X) / 3f, start.Y + (end.Y - start.Y) / 3f);
            PointF control2 = new PointF(start.X + 2 * (end.X - start.X) / 3f, start.Y + 2 * (end.Y - start.Y) / 3f);
            return CubicBezier(start, end, control1, control2, t);
        }

        internal static PointF PerlinNoise(PointF start, PointF end, double t, double amplitude = 10.0, double frequency = 0.1)
        {
            double baseX = start.X + (end.X - start.X) * t;
            double baseY = start.Y + (end.Y - start.Y) * t;

            // Sample along the actual travel, not along t (which is constant per tick), so the
            // offset genuinely varies as the crosshair moves instead of being a fixed bias.
            double sample = (Math.Abs(baseX) + Math.Abs(baseY)) * frequency;
            double noiseX = Noise(sample, 0) * amplitude;
            double noiseY = Noise(sample, 100) * amplitude;

            double perpX = -(end.Y - start.Y);
            double perpY = end.X - start.X;
            double perpLength = Math.Sqrt(perpX * perpX + perpY * perpY);

            if (perpLength > 0)
            {
                perpX /= perpLength;
                perpY /= perpLength;
            }

            return new PointF(
                (float)(baseX + perpX * noiseX + noiseY * 0.3),
                (float)(baseY + perpY * noiseX + noiseY * 0.3));
        }

        private static double Fade(double t)
        {
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + t * (b - a);
        }

        private static double Grad(int hash, double x, double y)
        {
            int h = hash & 15;
            double u = h < 8 ? x : y;
            double v = h < 4 ? y : h == 12 || h == 14 ? x : 0;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        private static double Noise(double x, double y)
        {
            int X = (int)Math.Floor(x) & 255;
            int Y = (int)Math.Floor(y) & 255;

            x -= Math.Floor(x);
            y -= Math.Floor(y);

            double u = Fade(x);
            double v = Fade(y);

            int A = permutation[X] + Y;
            int AA = permutation[A & 255];
            int AB = permutation[(A + 1) & 255];
            int B = permutation[(X + 1) & 255] + Y;
            int BA = permutation[B & 255];
            int BB = permutation[(B + 1) & 255];

            return Lerp(Lerp(Grad(permutation[AA], x, y),
                           Grad(permutation[BA], x - 1, y), u),
                      Lerp(Grad(permutation[AB], x, y - 1),
                           Grad(permutation[BB], x - 1, y - 1), u), v);
        }
    }
}
