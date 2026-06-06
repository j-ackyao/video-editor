namespace VideoTrim.Core.Models;

/// <summary>
/// The kept time range of the source (§5). Invariant enforced by callers/view-models:
/// <c>0 &lt;= Start &lt; End &lt;= source duration</c>.
/// </summary>
public sealed record TrimRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}
