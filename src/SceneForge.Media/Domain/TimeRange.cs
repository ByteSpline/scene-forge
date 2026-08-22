namespace SceneForge.Media.Domain;

public readonly record struct TimeRange
{
    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeRange(TimeSpan start, TimeSpan end)
    {
        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, "Start must not be negative.");
        }

        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), end, "End must not be earlier than start.");
        }

        Start = start;
        End = end;
    }

    public TimeSpan Duration => End - Start;

    public bool Contains(TimeSpan timestamp) => timestamp >= Start && timestamp <= End;

    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;

    public override string ToString() => $"[{Start}, {End}]";
}
