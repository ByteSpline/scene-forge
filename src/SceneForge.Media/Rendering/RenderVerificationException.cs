namespace SceneForge.Media.Rendering;

// Thrown when RenderOutputVerifier's post-render checks fail - a broken
// output file is a hard failure (unlike TimelinePlan.FeasibilityWarning's
// soft, non-throwing shortfall report), but Result always carries the full,
// itemized RenderVerificationResult rather than a bare message, so nothing
// about why verification failed is opaque (CLAUDE.md rule 10).
public sealed class RenderVerificationException : Exception
{
    public RenderVerificationResult Result { get; }

    public RenderVerificationException(RenderVerificationResult result)
        : base(BuildMessage(result))
    {
        Result = result;
    }

    private static string BuildMessage(RenderVerificationResult result) =>
        $"Rendered output failed verification: {string.Join("; ", result.Failures)}.";
}
