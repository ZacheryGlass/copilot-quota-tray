namespace WorkdayProgress;

internal enum PaceStatus
{
    UnderPace,
    OnPace,
    OverPace
}

internal static class UsageDisplay
{
    // Yellow means usage is within this many percentage points of
    // the percentage of workdays completed.
    private const double YellowBandPercentagePoints = 3.0;

    public static int GetIconNumber(
        CopilotUsage usage,
        double expectedPercent,
        bool showActualUsage)
    {
        double displayedPercent =
            showActualUsage &&
            usage.Status == CopilotQuotaStatus.Metered
                ? usage.PercentUsed
                : expectedPercent;

        return (int)Math.Round(
            displayedPercent,
            MidpointRounding.AwayFromZero);
    }

    public static PaceStatus DeterminePace(
        double actualPercent,
        double expectedPercent)
    {
        double difference = actualPercent - expectedPercent;

        if (difference > YellowBandPercentagePoints)
        {
            return PaceStatus.OverPace;
        }

        if (difference >= -YellowBandPercentagePoints)
        {
            return PaceStatus.OnPace;
        }

        return PaceStatus.UnderPace;
    }
}
