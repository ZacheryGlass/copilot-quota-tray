using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace WorkdayProgress;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new WorkdayProgressContext());
    }
}

internal sealed class WorkdayProgressContext : ApplicationContext
{
    private const int RefreshIntervalMilliseconds = 5 * 60 * 1000;

    // Yellow means usage is within this many percentage points of
    // the percentage of workdays completed.
    private const double YellowBandPercentagePoints = 3.0;

    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _usageMenuItem;
    private readonly ToolStripMenuItem _workMonthMenuItem;
    private readonly ToolStripMenuItem _paceMenuItem;
    private readonly System.Windows.Forms.Timer _timer;

    private Icon? _currentIcon;
    private bool _refreshInProgress;

    public WorkdayProgressContext()
    {
        _usageMenuItem = new ToolStripMenuItem("Loading Copilot usage...")
        {
            Enabled = false
        };

        _workMonthMenuItem = new ToolStripMenuItem
        {
            Enabled = false,
            Visible = false
        };

        _paceMenuItem = new ToolStripMenuItem
        {
            Enabled = false,
            Visible = false
        };

        var refreshMenuItem = new ToolStripMenuItem("Refresh now");
        refreshMenuItem.Click += async (_, _) =>
        {
            await RefreshAsync();
        };

        var exitMenuItem = new ToolStripMenuItem("Exit");
        exitMenuItem.Click += (_, _) =>
        {
            ExitThread();
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_usageMenuItem);
        menu.Items.Add(_workMonthMenuItem);
        menu.Items.Add(_paceMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(refreshMenuItem);
        menu.Items.Add(exitMenuItem);

        // Temporary neutral icon until the first GitHub request finishes.
        _currentIcon = NumberIcon.Create(0, Color.Gray);

        _trayIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = "Copilot usage: loading",
            ContextMenuStrip = menu,
            Visible = true
        };

        _trayIcon.DoubleClick += async (_, _) =>
        {
            await RefreshAsync();

            _trayIcon.BalloonTipTitle = "GitHub Copilot usage";
            _trayIcon.BalloonTipText = string.Join(
                Environment.NewLine,
                _usageMenuItem.Text,
                _workMonthMenuItem.Text,
                _paceMenuItem.Text);
            _trayIcon.ShowBalloonTip(3000);
        };

        _timer = new System.Windows.Forms.Timer
        {
            Interval = RefreshIntervalMilliseconds
        };

        _timer.Tick += async (_, _) =>
        {
            await RefreshAsync();
        };

        _timer.Start();

        // Run the first refresh immediately.
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_refreshInProgress)
        {
            return;
        }

        _refreshInProgress = true;

        try
        {
            CopilotUsage usage = await CopilotClient.GetUsageAsync();
            WorkdayPace workdayPace = WorkdayCalendar.Calculate(DateTime.Today);

            PaceStatus paceStatus = DeterminePaceStatus(
                usage.PercentUsed,
                workdayPace.PercentElapsed);

            Color iconColor = GetPaceColor(paceStatus);

            int displayedNumber = (int)Math.Round(
                workdayPace.PercentElapsed,
                MidpointRounding.AwayFromZero);

            Icon newIcon = NumberIcon.Create(
                displayedNumber,
                iconColor);

            Icon? oldIcon = _currentIcon;

            _currentIcon = newIcon;
            _trayIcon.Icon = newIcon;

            oldIcon?.Dispose();

            double difference =
                usage.PercentUsed - workdayPace.PercentElapsed;

            string paceDescription = paceStatus switch
            {
                PaceStatus.UnderPace => "under pace",
                PaceStatus.OnPace => "on pace",
                PaceStatus.OverPace => "over pace",
                _ => "unknown"
            };

            _usageMenuItem.Text =
                $"{usage.PercentUsed:F1}% used " +
                $"({usage.CreditsUsed:N0}/{usage.Entitlement:N0} credits)";

            _workMonthMenuItem.Text =
                $"{workdayPace.PercentElapsed:F1}% through the work month " +
                $"({workdayPace.PassedWorkdays}/{workdayPace.TotalWorkdays})";

            _paceMenuItem.Text =
                $"{Math.Abs(difference):F1} points {paceDescription}";

            _workMonthMenuItem.Visible = true;
            _paceMenuItem.Visible = true;

            // Keep this short because Windows limits tray tooltip length.
            _trayIcon.Text =
                $"Copilot {usage.PercentUsed:F1}% — {paceDescription}";
        }
        catch (Exception exception)
        {
            _usageMenuItem.Text =
                $"Refresh failed: {exception.Message}";

            _workMonthMenuItem.Visible = false;
            _paceMenuItem.Visible = false;

            _trayIcon.Text = "Copilot usage refresh failed";
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private static PaceStatus DeterminePaceStatus(
        double usagePercent,
        double workdayPercent)
    {
        double difference = usagePercent - workdayPercent;

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

    private static Color GetPaceColor(PaceStatus status)
    {
        return status switch
        {
            PaceStatus.UnderPace => Color.LimeGreen,
            PaceStatus.OnPace => Color.Gold,
            PaceStatus.OverPace => Color.Red,
            _ => Color.Gray
        };
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _timer.Dispose();

        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        _currentIcon?.Dispose();

        base.ExitThreadCore();
    }
}

internal static class CopilotClient
{
    public static async Task<CopilotUsage> GetUsageAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FindGhExecutable(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("api");
        startInfo.ArgumentList.Add("/copilot_internal/user");

        using var process = new Process
        {
            StartInfo = startInfo
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    "Windows could not start gh.exe.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Could not find gh.exe. Make sure GitHub CLI is installed " +
                "and available in PATH.",
                exception);
        }

        Task<string> outputTask =
            process.StandardOutput.ReadToEndAsync();

        Task<string> errorTask =
            process.StandardError.ReadToEndAsync();

        using var timeout =
            new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore failure to kill a process that may have already exited.
            }

            throw new TimeoutException(
                "The GitHub CLI request did not finish within 30 seconds.");
        }

        string output = await outputTask;
        string error = await errorTask;

        if (process.ExitCode != 0)
        {
            string message = string.IsNullOrWhiteSpace(error)
                ? $"gh.exe exited with code {process.ExitCode}."
                : error.Trim();

            throw new InvalidOperationException(message);
        }

        using JsonDocument document = JsonDocument.Parse(output);

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

        if (quota.TryGetProperty(
                "has_quota",
                out JsonElement hasQuotaElement) &&
            hasQuotaElement.ValueKind == JsonValueKind.False)
        {
            throw new InvalidOperationException(
                "GitHub reports that this account has no Copilot quota.");
        }

        double creditsUsed = ReadRequiredNumber(
            quota,
            "credits_used");

        double entitlement = ReadRequiredNumber(
            quota,
            "entitlement");

        double percentUsed;

        if (quota.TryGetProperty(
                "percent_remaining",
                out JsonElement percentRemainingElement) &&
            percentRemainingElement.TryGetDouble(
                out double percentRemaining))
        {
            percentUsed = 100.0 - percentRemaining;
        }
        else
        {
            if (entitlement <= 0)
            {
                throw new InvalidOperationException(
                    "GitHub returned an invalid Copilot entitlement.");
            }

            percentUsed = creditsUsed * 100.0 / entitlement;
        }

        percentUsed = Math.Max(0.0, percentUsed);

        return new CopilotUsage(
            creditsUsed,
            entitlement,
            percentUsed);
    }

    private static double ReadRequiredNumber(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(
                propertyName,
                out JsonElement element) ||
            !element.TryGetDouble(out double value))
        {
            throw new InvalidOperationException(
                $"GitHub did not return a valid {propertyName} value.");
        }

        return value;
    }

    private static string FindGhExecutable()
    {
        // Optional override:
        // setx GH_PATH "C:\path\to\gh.exe"
        string? configuredPath =
            Environment.GetEnvironmentVariable("GH_PATH");

        if (!string.IsNullOrWhiteSpace(configuredPath) &&
            File.Exists(configuredPath))
        {
            return configuredPath;
        }

        string[] candidates =
        {
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles),
                "GitHub CLI",
                "gh.exe"),

            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "GitHub CLI",
                "gh.exe"),

            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                "scoop",
                "shims",
                "gh.exe")
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Fall back to normal PATH resolution.
        return "gh.exe";
    }
}

internal static class WorkdayCalendar
{
    public static WorkdayPace Calculate(DateTime today)
    {
        DateTime firstDay =
            new(today.Year, today.Month, 1);

        DateTime lastDay =
            firstDay.AddMonths(1).AddDays(-1);

        int totalWorkdays =
            CountWorkdays(firstDay, lastDay);

        // Includes today when today is Monday through Friday.
        int passedWorkdays =
            CountWorkdays(firstDay, today.Date);

        double percentElapsed = totalWorkdays == 0
            ? 0.0
            : passedWorkdays * 100.0 / totalWorkdays;

        return new WorkdayPace(
            passedWorkdays,
            totalWorkdays,
            percentElapsed);
    }

    private static int CountWorkdays(
        DateTime start,
        DateTime end)
    {
        if (end < start)
        {
            return 0;
        }

        int count = 0;

        for (DateTime date = start.Date;
             date <= end.Date;
             date = date.AddDays(1))
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday
                and not DayOfWeek.Sunday)
            {
                count++;
            }
        }

        return count;
    }
}

internal static class NumberIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    public static Icon Create(int number, Color textColor)
    {
        const int size = 64;

        using var bitmap = new Bitmap(
            size,
            size,
            PixelFormat.Format32bppArgb);

        using Graphics graphics =
            Graphics.FromImage(bitmap);

        graphics.Clear(Color.Transparent);
        graphics.PageUnit = GraphicsUnit.Pixel;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingQuality =
            CompositingQuality.HighQuality;
        graphics.InterpolationMode =
            InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode =
            PixelOffsetMode.HighQuality;

        string text =
            number.ToString(CultureInfo.InvariantCulture);

        using GraphicsPath textPath =
            CreateCenteredTextPath(text, size);

        // A thin dark outline keeps the colored number readable
        // against both light and dark taskbars.
        using var outlinePen = new Pen(
            Color.FromArgb(240, 0, 0, 0),
            3.0f)
        {
            LineJoin = LineJoin.Round
        };

        using var textBrush =
            new SolidBrush(textColor);

        graphics.DrawPath(outlinePen, textPath);
        graphics.FillPath(textBrush, textPath);

        IntPtr iconHandle = bitmap.GetHicon();

        try
        {
            using Icon temporaryIcon =
                Icon.FromHandle(iconHandle);

            return (Icon)temporaryIcon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    private static GraphicsPath CreateCenteredTextPath(
        string text,
        int canvasSize)
    {
        using FontFamily fontFamily =
            CreateFontFamily();

        using StringFormat format =
            (StringFormat)StringFormat.GenericTypographic.Clone();

        format.FormatFlags |=
            StringFormatFlags.NoWrap;

        GraphicsPath? selectedPath = null;

        // Automatically use the largest font size that fits.
        for (float fontSize = 60;
             fontSize >= 8;
             fontSize -= 1)
        {
            var candidate = new GraphicsPath();

            candidate.AddString(
                text,
                fontFamily,
                (int)FontStyle.Bold,
                fontSize,
                new PointF(0, 0),
                format);

            RectangleF bounds =
                candidate.GetBounds();

            if (bounds.Width <= canvasSize - 6 &&
                bounds.Height <= canvasSize - 6)
            {
                selectedPath = candidate;
                break;
            }

            candidate.Dispose();
        }

        if (selectedPath is null)
        {
            selectedPath = new GraphicsPath();

            selectedPath.AddString(
                text,
                fontFamily,
                (int)FontStyle.Bold,
                8,
                new PointF(0, 0),
                format);
        }

        RectangleF textBounds =
            selectedPath.GetBounds();

        float x =
            (canvasSize - textBounds.Width) / 2f -
            textBounds.X;

        float y =
            (canvasSize - textBounds.Height) / 2f -
            textBounds.Y;

        using var transform = new Matrix();
        transform.Translate(x, y);

        selectedPath.Transform(transform);

        return selectedPath;
    }

    private static FontFamily CreateFontFamily()
    {
        try
        {
            // Narrow digits allow the number to remain larger.
            return new FontFamily("Arial Narrow");
        }
        catch (ArgumentException)
        {
            return new FontFamily("Segoe UI");
        }
    }
}

internal enum PaceStatus
{
    UnderPace,
    OnPace,
    OverPace
}

internal readonly record struct CopilotUsage(
    double CreditsUsed,
    double Entitlement,
    double PercentUsed);

internal readonly record struct WorkdayPace(
    int PassedWorkdays,
    int TotalWorkdays,
    double PercentElapsed);