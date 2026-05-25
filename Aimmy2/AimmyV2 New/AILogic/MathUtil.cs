using Vector2.AILogic;
using System.Drawing;
using System.Numerics;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;

namespace AILogic
{
    public static class MathUtil
    {
        private const uint SignMask32 = 0x80000000u;
        private const uint ExpMask32 = 0x7F800000u;
        private const uint MantMask32 = 0x007FFFFFu;

        private const uint SignMask16 = 0x8000u;
        private const uint ExpMask16 = 0x7C00u;
        private const uint MantMask16 = 0x03FFu;
        public static Func<double[], double[], double> L2Norm_Squared_Double = (x, y) =>
        {
            double dist = 0f;
            for (int i = 0; i < x.Length; i++)
            {
                dist += (x[i] - y[i]) * (x[i] - y[i]);
            }

            return dist;
        };
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(Prediction a, Prediction b)
        {
            float dx = a.ScreenCenterX - b.ScreenCenterX;
            float dy = a.ScreenCenterY - b.ScreenCenterY;
            return dx * dx + dy * dy;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateTargetScore(
            Prediction candidate,
            Prediction? currentTarget,
            float predictedX,
            float predictedY,
            float currentLockScore,
            float maxLockScore,
            float threshold)
        {
            // Base score from distance to predicted position (where we expect current target to be)
            float dx = candidate.ScreenCenterX - predictedX;
            float dy = candidate.ScreenCenterY - predictedY;
            float distSq = dx * dx + dy * dy;

            // Normalize distance score (0 = far, 1 = close)
            float thresholdSq = threshold * threshold;
            float distanceScore = Math.Max(0f, 1f - (distSq / thresholdSq));

            // Confidence bonus (0-0.3 range)
            float confidenceBonus = candidate.Confidence * 0.3f;

            // Size bonus - larger targets are more stable (0-0.2 range)
            float area = candidate.Rectangle.Width * candidate.Rectangle.Height;
            float sizeBonus = Math.Min(0.2f, area / 50000f);

            // Lock bonus for current target (0-0.5 range based on accumulated score)
            float lockBonus = (currentTarget != null && distanceScore > 0.3f)
                ? (currentLockScore / maxLockScore) * 0.5f
                : 0f;

            return distanceScore + confidenceBonus + sizeBonus + lockBonus;
        }
        public static int CalculateNumDetections(int imageSize)
        {
            // YOLOv8 detection calculation: (size/8)² + (size/16)² + (size/32)²
            int stride8 = imageSize / 8;
            int stride16 = imageSize / 16;
            int stride32 = imageSize / 32;

            return (stride8 * stride8) + (stride16 * stride16) + (stride32 * stride32);
        }
        // LUT = look up table
        // REFERENCE: https://stackoverflow.com/questions/1089235/where-can-i-find-a-byte-to-float-lookup-table
        // "In this case, the lookup table should be faster than using direct calculation. The more complex the math (trigonometry, etc.), the bigger the performance gain."
        // although we used small calculations, something is better than nothing.
        private static readonly float[] _byteToFloatLut = CreateByteToFloatLut();
        private static float[] CreateByteToFloatLut()
        {
            var lut = new float[256];
            for (int i = 0; i < 256; i++)
                lut[i] = i / 255f;
            return lut;
        }
        #region Half-precision float conversion
        // references: https://devblogs.microsoft.com/dotnet/introducing-the-half-type/
        // https://stackoverflow.com/questions/76799117/how-to-convert-a-float-to-a-half-type-and-the-other-way-around-in-c (in C)
        // https://stackoverflow.com/questions/3026441/float32-to-float16
        // I would just like to say now, that python users are extremely lucky: https://onnxruntime.ai/docs/performance/model-optimizations/float16.html


        /// <summary>
        /// convert single-precision (32-bit) float to half-precision (16-bit) float stored in ushort
        /// </summary>
        /// <param name="f"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort FloatToHalfBits(float f)
        {
            uint fbits = BitConverter.SingleToUInt32Bits(f);
            uint sign = (fbits >> 16) & SignMask16;
            uint val = fbits & ~SignMask32;

            // exactly equal means infinity otherwise its NaN
            // NaN / Inf
            if (val >= ExpMask32)
            {
                if (val == ExpMask32)
                    return (ushort)(sign | ExpMask16); // Inf

                // NaN: preserve top mantissa bits
                return (ushort)(sign | ExpMask16 | ((fbits & MantMask32) >> 13));
            }

            // Too small for normalized half
            if (val < 0x38800000u) // (113 << 23) 
            {
                // subnormal half
                uint mant = (fbits & MantMask32) | (1u << 23);
                int shift = (113 - (int)(fbits >> 23));

                if (shift < 24)
                {
                    uint res = (mant + (1u << (shift - 1)) + ((mant >> shift) & 1u)) >> shift;
                    return (ushort)(sign | (res & MantMask16));
                }

                return (ushort)sign; // underflow to zero
            }

            // Normalized
            uint exp = ((fbits >> 23) - 127 + 15) & 0x1Fu;
            uint mantissa = (fbits >> 13) & MantMask16;
            return (ushort)(sign | (exp << 10) | mantissa); // store as 16 bit
        }

        /// <summary>
        ///  convert half-precision (16-bit) float stored in ushort to single-precision (32-bit) float
        /// </summary>
        /// <param name="h"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float HalfBitsToFloat(ushort h)
        {
            uint sign = (uint)(h & SignMask16) << 16;
            uint exp = (uint)(h & ExpMask16) >> 10;
            uint mant = (uint)(h & MantMask16);

            uint fbits;

            if (exp == 0)
            {
                if (mant == 0)
                {
                    fbits = sign; // Zero
                }
                else
                {
                    // normalize with bit scan
                    int shift = BitOperations.LeadingZeroCount(mant) - 21; // adjust to align to float mantissa
                    mant <<= shift;
                    uint exp32 = (uint)(127 - 14 - shift); // 127 bias - 15 bias + 1
                    fbits = sign | (exp32 << 23) | ((mant & MantMask16) << 13);
                }
            }
            else if (exp == 0x1F)
            {
                // Inf or NaN
                fbits = sign | ExpMask32 | (mant << 13);
            }
            else
            {
                uint exp32 = exp - 15 + 127;
                fbits = sign | (exp32 << 23) | (mant << 13);
            }

            return BitConverter.UInt32BitsToSingle(fbits);
        }
        #endregion
        // this new function reduces gc pressure as i stopped using array.copy
        // REFERENCE: https://www.codeproject.com/Articles/617613/Fast-Pixel-Operations-in-NET-With-and-Without-unsa
        public static unsafe void BitmapToFloatArrayInPlace(Bitmap image, float[] result, int IMAGE_SIZE)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (result == null) throw new ArgumentNullException(nameof(result));

            int width = IMAGE_SIZE;
            int height = IMAGE_SIZE;
            int totalPixels = width * height;

            // check if it has the right size
            if (result.Length != 3 * totalPixels)
                throw new ArgumentException($"result must be length {3 * totalPixels}", nameof(result));

            //const float multiplier = 1f / 255f; kept for reference
            var rect = new Rectangle(0, 0, width, height);

            // Lock the bitmap
            var bmpData = image.LockBits(rect, ImageLockMode.ReadOnly, image.PixelFormat);
            try
            {
                byte* basePtr = (byte*)bmpData.Scan0;
                int stride = Math.Abs(bmpData.Stride); //handle negative stride, topdown vs bottomup

                // array offsets for the three color channels
                // 32gbpp format is hardcoded but 24bpp is just 3 bytes per pixel
                const int bytesPerPixel = 4;
                const int pixelsPerIteration = 4; // process 4 pixels at a time

                int rOffset = 0; // Red channel starts at index 0
                int gOffset = totalPixels; // Green channel starts after red
                int bOffset = totalPixels * 2; // Blue channel starts after green

                // prevent gc from moving the array while we are using it
                fixed (float* dest = result)
                {
                    float* rPtr = dest + rOffset; //pointers to the start of each channel
                    float* gPtr = dest + gOffset; //variables are arranged in RGB but its actually BGR.
                    float* bPtr = dest + bOffset;

                    // process rows in parallel (avoid creating 640 threads)
                    Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = 4 }, (y) =>
                    {
                        byte* row = basePtr + (long)y * stride;
                        int rowStart = y * width;
                        int x = 0;

                        int widthLimit = width - pixelsPerIteration + 1;
                        // optimize for 4 pixels at a time
                        // to remove loop overhead and (cache (?))
                        for (; x < widthLimit; x += pixelsPerIteration)
                        {
                            int baseIdx = rowStart + x;
                            byte* p = row + (x * bytesPerPixel);

                            // bgr(a) values
                            // windows bitmap uses BGR order

                            // process 1st pixel / pixel 0 (16bytes)
                            bPtr[baseIdx] = _byteToFloatLut[p[0]];
                            gPtr[baseIdx] = _byteToFloatLut[p[1]];
                            rPtr[baseIdx] = _byteToFloatLut[p[2]];
                            //alpha is ignored

                            // pixel 1
                            bPtr[baseIdx + 1] = _byteToFloatLut[p[4]];
                            gPtr[baseIdx + 1] = _byteToFloatLut[p[5]];
                            rPtr[baseIdx + 1] = _byteToFloatLut[p[6]];
                            // pixel 2
                            bPtr[baseIdx + 2] = _byteToFloatLut[p[8]];
                            gPtr[baseIdx + 2] = _byteToFloatLut[p[9]];
                            rPtr[baseIdx + 2] = _byteToFloatLut[p[10]];
                            // pixel 3
                            bPtr[baseIdx + 3] = _byteToFloatLut[p[12]];
                            gPtr[baseIdx + 3] = _byteToFloatLut[p[13]];
                            rPtr[baseIdx + 3] = _byteToFloatLut[p[14]];

                            p += 16; // move pointer 16 bytes forward (4 pixels * 4 bytes per pixel)
                        }

                        // handle the rest of the pixels when width is not divisible by 4
                        for (; x < width; x++)
                        {
                            int idx = rowStart + x;
                            byte* p = row + (x * bytesPerPixel);

                            // process by BGR(a) value like before
                            bPtr[idx] = _byteToFloatLut[p[0]];
                            gPtr[idx] = _byteToFloatLut[p[1]];
                            rPtr[idx] = _byteToFloatLut[p[2]];
                        }
                    });
                }
            }
            finally
            {
                //unlock the bitmap finally
                image.UnlockBits(bmpData);
            }
        }
    }
}
