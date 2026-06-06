using VideoTrim.Core.Models;

namespace VideoTrim.Core.Encoding;

/// <summary>
/// Pure implementation of the GIF size-reduction priority of §9.2: step <b>colors</b> first, then
/// <b>fps</b>, then <b>height</b>. Each call returns the next-smaller <see cref="GifSettings"/> or
/// null when nothing more can be reduced. Separated from the encode loop so it is unit-testable
/// without touching ffmpeg or the file system.
/// </summary>
public static class GifSizeReducer
{
    /// <summary>Maximum encode attempts in the iterative loop (§9.2).</summary>
    public const int MaxIters = 5;

    public const int ColorFloor = 32;
    public const double FpsFloor = 8;
    public const int HeightFloor = 144;

    public static GifSettings? Reduce(GifSettings current, int sourceHeight)
    {
        var next = Clone(current);

        // (a) Colors: 256 → 128 → 64 → 32 (biggest size win, lowest quality loss first).
        if (current.MaxColors > 128) { next.MaxColors = 128; return next; }
        if (current.MaxColors > 64) { next.MaxColors = 64; return next; }
        if (current.MaxColors > ColorFloor) { next.MaxColors = ColorFloor; return next; }

        // (b) FPS: reduce toward the floor in ×0.75 steps.
        if (current.TargetFps > FpsFloor)
        {
            double stepped = Math.Max(FpsFloor, Math.Round(current.TargetFps * 0.75, MidpointRounding.AwayFromZero));
            next.TargetFps = stepped < current.TargetFps ? stepped : FpsFloor;
            return next;
        }

        // (c) Height: scale down ×0.85 (even), floored.
        int height = current.TargetHeight ?? sourceHeight;
        if (height > HeightFloor)
        {
            int reduced = (int)Math.Floor(height * 0.85);
            if (reduced % 2 != 0)
                reduced -= 1;
            reduced = Math.Max(HeightFloor, reduced);
            if (reduced < height)
            {
                next.TargetHeight = reduced;
                return next;
            }
        }

        return null; // Exhausted — caller keeps the best-effort smallest result (§9.2 step 5).
    }

    private static GifSettings Clone(GifSettings s) => new()
    {
        TargetHeight = s.TargetHeight,
        TargetFps = s.TargetFps,
        MaxColors = s.MaxColors,
        Dither = s.Dither,
        TargetSizeBytes = s.TargetSizeBytes,
    };
}
