namespace WorkdayProgress;

internal sealed class CopilotConfigurationException : InvalidOperationException
{
    public CopilotConfigurationException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal static class GitHubCliFailure
{
    public static InvalidOperationException FromExit(
        int exitCode,
        string standardError)
    {
        string error = standardError.Trim();

        if (IsAuthenticationFailure(error))
        {
            return new CopilotConfigurationException(
                "GitHub CLI is not authenticated. Run \"gh auth login\", " +
                "then choose Refresh now.");
        }

        string message = string.IsNullOrWhiteSpace(error)
            ? $"gh.exe exited with code {exitCode}."
            : error;

        return new InvalidOperationException(message);
    }

    private static bool IsAuthenticationFailure(string error)
    {
        return error.Contains(
                   "gh auth login",
                   StringComparison.OrdinalIgnoreCase) ||
               error.Contains(
                   "not logged into any GitHub hosts",
                   StringComparison.OrdinalIgnoreCase) ||
               error.Contains(
                   "bad credentials",
                   StringComparison.OrdinalIgnoreCase) ||
               error.Contains(
                   "HTTP 401",
                   StringComparison.OrdinalIgnoreCase);
    }
}
