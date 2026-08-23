using OpenCvSharp;

namespace SceneForge.Media.Detection.Signals;

// Dense (Farneback) optical flow between consecutive grayscale frames,
// reduced immediately to a small GridSize x GridSize grid of per-cell mean
// vectors and then to a handful of scalars - the dense flow Mat itself is
// created, read, and disposed within Extract and never retained (CLAUDE.md
// rule 6/7: no unbounded per-pixel state survives past this one call).
// RadialOutwardScore is the zoom signature (flow vectors consistently
// pointing away from - or toward - the frame center); DirectionalConsistency
// is the wipe/slide signature (flow vectors consistently pointing the same
// way, unrelated to center). A genuinely static frame pair produces near-zero
// magnitude and both scores are meaningless noise at that point - callers
// must gate on Magnitude via a profile's MinMotionMagnitude threshold before
// trusting either score.
internal sealed class GlobalMotionEstimateExtractor : IGlobalMotionSignalExtractor
{
    private const int GridSize = 3;

    public string Name => nameof(GlobalMotionEstimateExtractor);

    public GlobalMotionEstimate Extract(AnalyzedFrame previous, AnalyzedFrame current)
    {
        using var flow = new Mat();
        Cv2.CalcOpticalFlowFarneback(
            previous.Gray,
            current.Gray,
            flow,
            pyrScale: 0.5,
            levels: 4,
            winsize: 21,
            iterations: 3,
            polyN: 5,
            polySigma: 1.1,
            flags: OpticalFlowFlags.None);

        var width = flow.Cols;
        var height = flow.Rows;
        var cellWidth = Math.Max(1, width / GridSize);
        var cellHeight = Math.Max(1, height / GridSize);

        var cells = new List<(double CenterX, double CenterY, double Dx, double Dy)>();
        for (var gridY = 0; gridY < GridSize; gridY++)
        {
            var y0 = gridY * cellHeight;
            var y1 = gridY == GridSize - 1 ? height : y0 + cellHeight;
            if (y1 <= y0)
            {
                continue;
            }

            for (var gridX = 0; gridX < GridSize; gridX++)
            {
                var x0 = gridX * cellWidth;
                var x1 = gridX == GridSize - 1 ? width : x0 + cellWidth;
                if (x1 <= x0)
                {
                    continue;
                }

                using var cell = new Mat(flow, new Rect(x0, y0, x1 - x0, y1 - y0));
                var meanVector = Cv2.Mean(cell);
                cells.Add(((x0 + x1) / 2.0, (y0 + y1) / 2.0, meanVector.Val0, meanVector.Val1));
            }
        }

        if (cells.Count == 0)
        {
            return new GlobalMotionEstimate { MeanDx = 0, MeanDy = 0, Magnitude = 0, RadialOutwardScore = 0, DirectionalConsistency = 0 };
        }

        var meanDx = cells.Average(cell => cell.Dx);
        var meanDy = cells.Average(cell => cell.Dy);
        var diagonal = Math.Sqrt((width * (double)width) + (height * (double)height));
        var magnitude = diagonal > 0 ? Math.Sqrt((meanDx * meanDx) + (meanDy * meanDy)) / diagonal : 0.0;

        var centerX = width / 2.0;
        var centerY = height / 2.0;
        var radialOutwardScore = ComputeRadialOutwardScore(cells, centerX, centerY);
        var directionalConsistency = ComputeDirectionalConsistency(cells, meanDx, meanDy);

        return new GlobalMotionEstimate
        {
            MeanDx = meanDx,
            MeanDy = meanDy,
            Magnitude = magnitude,
            RadialOutwardScore = radialOutwardScore,
            DirectionalConsistency = directionalConsistency,
        };
    }

    private static double ComputeRadialOutwardScore(
        List<(double CenterX, double CenterY, double Dx, double Dy)> cells,
        double centerX,
        double centerY)
    {
        double sum = 0;
        var weight = 0;
        foreach (var cell in cells)
        {
            var radialX = cell.CenterX - centerX;
            var radialY = cell.CenterY - centerY;
            var radialLength = Math.Sqrt((radialX * radialX) + (radialY * radialY));
            var vectorLength = Math.Sqrt((cell.Dx * cell.Dx) + (cell.Dy * cell.Dy));
            if (radialLength < 1e-6 || vectorLength < 1e-6)
            {
                continue;
            }

            sum += ((cell.Dx * radialX) + (cell.Dy * radialY)) / (radialLength * vectorLength);
            weight++;
        }

        return weight > 0 ? Math.Clamp(sum / weight, -1.0, 1.0) : 0.0;
    }

    private static double ComputeDirectionalConsistency(
        List<(double CenterX, double CenterY, double Dx, double Dy)> cells,
        double meanDx,
        double meanDy)
    {
        var meanLength = Math.Sqrt((meanDx * meanDx) + (meanDy * meanDy));
        if (meanLength < 1e-6)
        {
            return 0.0;
        }

        var meanUnitX = meanDx / meanLength;
        var meanUnitY = meanDy / meanLength;

        double sum = 0;
        var weight = 0;
        foreach (var cell in cells)
        {
            var vectorLength = Math.Sqrt((cell.Dx * cell.Dx) + (cell.Dy * cell.Dy));
            if (vectorLength < 1e-6)
            {
                continue;
            }

            sum += ((cell.Dx * meanUnitX) + (cell.Dy * meanUnitY)) / vectorLength;
            weight++;
        }

        return weight > 0 ? Math.Clamp(sum / weight, 0.0, 1.0) : 0.0;
    }
}
