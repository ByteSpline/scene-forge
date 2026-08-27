using SceneForge.Media.Tooling;

namespace SceneForge.Media.Tests.TestSupport;

internal sealed class FakeOpenCvNativeProbe : IOpenCvNativeProbe
{
    private readonly string? _buildInformation;
    private readonly Exception? _exceptionToThrow;

    private FakeOpenCvNativeProbe(string? buildInformation, Exception? exceptionToThrow)
    {
        _buildInformation = buildInformation;
        _exceptionToThrow = exceptionToThrow;
    }

    public static FakeOpenCvNativeProbe Succeeding(string buildInformation = "OpenCV 4.13.0") => new(buildInformation, null);

    public static FakeOpenCvNativeProbe Throwing(Exception exception) => new(null, exception);

    public string Probe() => _exceptionToThrow is null ? _buildInformation! : throw _exceptionToThrow;
}
