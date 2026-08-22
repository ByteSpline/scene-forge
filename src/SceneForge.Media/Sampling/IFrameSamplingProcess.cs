namespace SceneForge.Media.Sampling;

// Abstraction over a running ffmpeg decode process, seamed here so
// FrameSampler's channel/pooling/backpressure logic can be exercised in
// tests against synthetic streams without spawning a real process.
internal interface IFrameSamplingProcess : IAsyncDisposable
{
    // Raw video bytes (rawvideo muxer output). Binary - never routed
    // through a text encoding, unlike ProcessRunner's line-oriented capture.
    Stream StandardOutput { get; }

    // ffmpeg's log output, including one showinfo line per emitted frame.
    TextReader StandardError { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    int ExitCode { get; }

    // Kills the entire process tree if still running; safe to call more
    // than once or after the process has already exited.
    void Kill();
}
