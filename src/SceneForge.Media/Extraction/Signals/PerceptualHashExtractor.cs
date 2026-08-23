using OpenCvSharp;

namespace SceneForge.Media.Extraction.Signals;

// A standard mean-thresholded low-frequency-DCT perceptual hash: resize to
// 32x32 grayscale, take the DCT, keep only the top-left 8x8 block (the
// lowest, most content-defining frequencies), and set one output bit per
// coefficient based on whether it exceeds the block's own mean (excluding
// the DC term, which reflects only overall brightness and would otherwise
// dominate the threshold). Two frames with a small resulting Hamming
// distance look visually similar; this is explicitly a cheap heuristic
// fingerprint for clustering candidates, not a cryptographic or
// exact-duplicate hash.
internal static class PerceptualHashExtractor
{
    private const int ResizeDimension = 32;
    private const int BlockDimension = 8;
    private const int CoefficientCount = BlockDimension * BlockDimension;

    public static ulong Extract(Mat gray)
    {
        using var resized = new Mat();
        Cv2.Resize(gray, resized, new Size(ResizeDimension, ResizeDimension), interpolation: InterpolationFlags.Area);

        using var floatMat = new Mat();
        resized.ConvertTo(floatMat, MatType.CV_32F);

        using var dct = new Mat();
        Cv2.Dct(floatMat, dct);

        Span<float> coefficients = stackalloc float[CoefficientCount];
        var index = 0;
        double sum = 0;
        for (var row = 0; row < BlockDimension; row++)
        {
            for (var col = 0; col < BlockDimension; col++)
            {
                var value = dct.At<float>(row, col);
                coefficients[index] = value;
                index++;
                if (row != 0 || col != 0)
                {
                    sum += value;
                }
            }
        }

        var mean = sum / (CoefficientCount - 1);

        ulong hash = 0;
        for (var i = 0; i < CoefficientCount; i++)
        {
            hash <<= 1;
            if (coefficients[i] > mean)
            {
                hash |= 1UL;
            }
        }

        return hash;
    }

    public static int HammingDistance(ulong a, ulong b) => System.Numerics.BitOperations.PopCount(a ^ b);
}
