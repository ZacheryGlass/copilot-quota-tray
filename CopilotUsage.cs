using System.Text.Json;

namespace WorkdayProgress;

internal enum CopilotQuotaStatus
{
    Metered,
    Unlimited,
    Unavailable
}

internal readonly record struct CopilotUsage(
    double CreditsUsed,
    double Entitlement,
    double PercentUsed,
    CopilotQuotaStatus Status);

internal static class CopilotUsageParser
{
    public static CopilotUsage Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty(
                "quota_snapshots",
                out JsonElement snapshots) ||
            !snapshots.TryGetProperty(
                "premium_interactions",
                out JsonElement quota))
        {
            throw new InvalidOperationException(
                "GitHub did not return the premium_interactions quota.");
        }

        if (ReadOptionalBoolean(quota, "unlimited") == true)
        {
            return new CopilotUsage(
                CreditsUsed: 0,
                Entitlement: 0,
                PercentUsed: 0,
                CopilotQuotaStatus.Unlimited);
        }

        double? entitlement = ReadOptionalNumber(
            quota,
            "entitlement");

        double? creditsUsed = ReadOptionalNumber(
            quota,
            "credits_used");

        bool? hasQuota =
            ReadOptionalBoolean(quota, "has_quota");

        if (entitlement is null)
        {
            if (hasQuota == false)
            {
                if (creditsUsed is > 0)
                {
                    throw new InvalidOperationException(
                        "GitHub returned Copilot usage without an entitlement.");
                }

                return Unavailable();
            }

            throw new InvalidOperationException(
                "GitHub did not return a valid entitlement value.");
        }

        if (entitlement < 0)
        {
            throw new InvalidOperationException(
                "GitHub returned an invalid Copilot entitlement.");
        }

        if (entitlement == 0)
        {
            if (creditsUsed is > 0)
            {
                throw new InvalidOperationException(
                    "GitHub returned Copilot usage without an entitlement.");
            }

            return Unavailable();
        }

        if (creditsUsed is null || creditsUsed < 0)
        {
            throw new InvalidOperationException(
                "GitHub did not return a valid credits_used value.");
        }

        double percentUsed =
            creditsUsed.Value * 100.0 / entitlement.Value;

        return new CopilotUsage(
            creditsUsed.Value,
            entitlement.Value,
            percentUsed,
            CopilotQuotaStatus.Metered);
    }

    private static CopilotUsage Unavailable()
    {
        return new CopilotUsage(
            CreditsUsed: 0,
            Entitlement: 0,
            PercentUsed: 0,
            CopilotQuotaStatus.Unavailable);
    }

    private static double? ReadOptionalNumber(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(
                propertyName,
                out JsonElement element))
        {
            return null;
        }

        if (!element.TryGetDouble(out double value) ||
            !double.IsFinite(value))
        {
            throw new InvalidOperationException(
                $"GitHub did not return a valid {propertyName} value.");
        }

        return value;
    }

    private static bool? ReadOptionalBoolean(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(
                propertyName,
                out JsonElement element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException(
                $"GitHub did not return a valid {propertyName} value.")
        };
    }
}
