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
    private readonly ToolStripMenuItem _weekdaysOnlyMenuItem;
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

        AppSettings settings = AppSettings.Load();

        _weekdaysOnlyMenuItem = new ToolStripMenuItem("Weekdays only")
        {
            CheckOnClick = true,
            Checked = settings.WeekdaysOnly
        };

        _weekdaysOnlyMenuItem.CheckedChanged += async (_, _) =>
        {
            AppSettings.Save(new AppSettings(
                _weekdaysOnlyMenuItem.Checked));

            await RefreshAsync();
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
        menu.Items.Add(_weekdaysOnlyMenuItem);
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
            _trayIcon.BalloonTipText = GetBalloonTipText();
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
        _ = RefreshAsync(showSetupNotification: true);
    }

    private async Task RefreshAsync(
        bool showSetupNotification = false)
    {
        if (_refreshInProgress)
        {
            return;
        }

        _refreshInProgress = true;

        try
        {
            CopilotUsage usage = await CopilotClient.GetUsageAsync();
            WorkdayPace workdayPace = WorkdayCalendar.Calculate(
                DateTime.Today,
                weekdaysOnly: _weekdaysOnlyMenuItem.Checked);

            int displayedNumber = (int)Math.Round(
                workdayPace.PercentElapsed,
                MidpointRounding.AwayFromZero);

            if (usage.Status != CopilotQuotaStatus.Metered)
            {
                SetIcon(displayedNumber, Color.Gray);

                _usageMenuItem.Text = usage.Status switch
                {
                    CopilotQuotaStatus.Unlimited =>
                        "Copilot premium usage is unlimited",
                    CopilotQuotaStatus.Unavailable =>
                        "No Copilot premium quota assigned",
                    _ => "Copilot premium quota unavailable"
                };

                _workMonthMenuItem.Visible = false;
                _paceMenuItem.Visible = false;

                _trayIcon.Text = usage.Status switch
                {
                    CopilotQuotaStatus.Unlimited =>
                        "Copilot premium usage: unlimited",
                    _ => "Copilot premium quota: not assigned"
                };

                return;
            }

            PaceStatus paceStatus = DeterminePaceStatus(
                usage.PercentUsed,
                workdayPace.PercentElapsed);

            Color iconColor = GetPaceColor(paceStatus);

            SetIcon(displayedNumber, iconColor);

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

            string monthDescription = _weekdaysOnlyMenuItem.Checked
                ? "through the work month"
                : "through the month";

            _workMonthMenuItem.Text =
                $"{workdayPace.PercentElapsed:F1}% {monthDescription} " +
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

            if (showSetupNotification &&
                exception is CopilotConfigurationException)
            {
                _trayIcon.BalloonTipTitle =
                    "Copilot Quota Tray setup needed";
                _trayIcon.BalloonTipText = exception.Message;
                _trayIcon.ShowBalloonTip(5000);
            }
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private string GetBalloonTipText()
    {
        var lines = new List<string>
        {
            _usageMenuItem.Text ?? string.Empty
        };

        if (_workMonthMenuItem.Visible)
        {
            lines.Add(_workMonthMenuItem.Text ?? string.Empty);
        }

        if (_paceMenuItem.Visible)
        {
            lines.Add(_paceMenuItem.Text ?? string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void SetIcon(int number, Color color)
    {
        Icon newIcon = NumberIcon.Create(number, color);
        Icon? oldIcon = _currentIcon;

        _currentIcon = newIcon;
        _trayIcon.Icon = newIcon;

        oldIcon?.Dispose();
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
            throw new CopilotConfigurationException(
                "GitHub CLI was not found. Install it or add gh.exe to " +
                "PATH, then restart the app.",
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
            throw GitHubCliFailure.FromExit(
                process.ExitCode,
                error);
        }

        return CopilotUsageParser.Parse(output);
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
    public static WorkdayPace Calculate(
        DateTime today,
        bool weekdaysOnly)
    {
        DateTime firstDay =
            new(today.Year, today.Month, 1);

        DateTime lastDay =
            firstDay.AddMonths(1).AddDays(-1);

        int totalDays = CountEligibleDays(
            firstDay,
            lastDay,
            weekdaysOnly);

        // Includes today when it is eligible under the selected mode.
        int passedDays = CountEligibleDays(
            firstDay,
            today.Date,
            weekdaysOnly);

        double percentElapsed = totalDays == 0
            ? 0.0
            : passedDays * 100.0 / totalDays;

        return new WorkdayPace(
            passedDays,
            totalDays,
            percentElapsed);
    }

    private static int CountEligibleDays(
        DateTime start,
        DateTime end,
        bool weekdaysOnly)
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
            if (!weekdaysOnly || IsWeekday(date))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsWeekday(DateTime date)
    {
        return date.DayOfWeek is not DayOfWeek.Saturday
            and not DayOfWeek.Sunday;
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


internal sealed record AppSettings(bool WeekdaysOnly)
{
    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "CopilotPace",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings(WeekdaysOnly: true);
            }

            string json = File.ReadAllText(SettingsPath);

            AppSettings? settings =
                JsonSerializer.Deserialize<AppSettings>(json);

            return settings ??
                new AppSettings(WeekdaysOnly: true);
        }
        catch
        {
            return new AppSettings(WeekdaysOnly: true);
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(SettingsPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // The toggle still works for the current session.
        }
    }
}

internal enum PaceStatus
{
    UnderPace,
    OnPace,
    OverPace
}

internal readonly record struct WorkdayPace(
    int PassedWorkdays,
    int TotalWorkdays,
    double PercentElapsed);
