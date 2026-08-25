using WorkdayProgress;

var tests = new (string Name, Action Run)[]
{
    ("active quota", ActiveQuota),
    ("exhausted quota", ExhaustedQuota),
    ("no entitlement", NoEntitlement),
    ("no entitlement without numeric fields", NoEntitlementWithoutNumbers),
    ("unlimited quota", UnlimitedQuota),
    ("overage", Overage),
    ("missing quota data", MissingQuotaData)
};

int failures = 0;

foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

return failures == 0 ? 0 : 1;

static void ActiveQuota()
{
    CopilotUsage usage = ParseQuota("""
        {
          "has_quota": true,
          "unlimited": false,
          "percent_remaining": 21.9,
          "credits_used": 23418,
          "entitlement": 30000
        }
        """);

    Equal(CopilotQuotaStatus.Metered, usage.Status);
    Equal(23418, usage.CreditsUsed);
    Equal(30000, usage.Entitlement);
    CloseTo(78.06, usage.PercentUsed);
}

static void ExhaustedQuota()
{
    CopilotUsage usage = ParseQuota("""
        {
          "has_quota": false,
          "unlimited": false,
          "percent_remaining": 0.0,
          "credits_used": 15000,
          "entitlement": 15000
        }
        """);

    Equal(CopilotQuotaStatus.Metered, usage.Status);
    CloseTo(100, usage.PercentUsed);
}

static void NoEntitlement()
{
    CopilotUsage usage = ParseQuota("""
        {
          "has_quota": false,
          "unlimited": false,
          "credits_used": 0,
          "entitlement": 0
        }
        """);

    Equal(CopilotQuotaStatus.Unavailable, usage.Status);
}

static void NoEntitlementWithoutNumbers()
{
    CopilotUsage usage = ParseQuota("""
        {
          "has_quota": false,
          "unlimited": false
        }
        """);

    Equal(CopilotQuotaStatus.Unavailable, usage.Status);
}

static void UnlimitedQuota()
{
    CopilotUsage usage = ParseQuota("""
        {
          "has_quota": true,
          "unlimited": true,
          "credits_used": 0,
          "entitlement": 0
        }
        """);

    Equal(CopilotQuotaStatus.Unlimited, usage.Status);
}

static void Overage()
{
    CopilotUsage usage = ParseQuota("""
        {
          "has_quota": false,
          "unlimited": false,
          "percent_remaining": 0.0,
          "credits_used": 16500,
          "entitlement": 15000
        }
        """);

    Equal(CopilotQuotaStatus.Metered, usage.Status);
    CloseTo(110, usage.PercentUsed);
}

static void MissingQuotaData()
{
    Throws<InvalidOperationException>(() =>
        CopilotUsageParser.Parse("""{"quota_snapshots": {}}"""));
}

static CopilotUsage ParseQuota(string quota)
{
    return CopilotUsageParser.Parse($$"""
        {
          "quota_snapshots": {
            "premium_interactions": {{quota}}
          }
        }
        """);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"Expected {expected}, but got {actual}.");
    }
}

static void CloseTo(double expected, double actual)
{
    if (Math.Abs(expected - actual) > 0.0001)
    {
        throw new InvalidOperationException(
            $"Expected {expected}, but got {actual}.");
    }
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Expected {typeof(TException).Name}.");
}
