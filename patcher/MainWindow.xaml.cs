using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LegacyCrossplayPatcher;

public partial class MainWindow : Window
{
    private readonly PatchService _patchService;
    private readonly string _settingsPath;
    private TargetAnalysis? _target;
    private string? _buildOutput;
    private bool _working;

    public MainWindow(bool loadUserSettings = true)
    {
        InitializeComponent();
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LegacyCrossplayPatcher",
            "settings.json");
        _patchService = new PatchService();
        _patchService.LogLine += AppendLog;
        if (loadUserSettings)
            LoadSettings();
        ShowGuide("quick");
        AppendLog("[ready] Legacy Crossplay Patcher 0.2.3");
    }

    public void PrepareScreenshot(string view)
    {
        ShowPage(view);
        if (view == "log")
        {
            LogBox.Text =
                "[12:04:01] Inspecting Minecraft Game" + Environment.NewLine +
                "[12:04:02] Platform signature recognized" + Environment.NewLine +
                "[12:04:02] SHA-256 calculated locally" + Environment.NewLine +
                "[12:04:03] All 31 baseline files matched" + Environment.NewLine +
                "[12:04:03] Backup created" + Environment.NewLine +
                "[12:04:04] Crossplay patch applied successfully" + Environment.NewLine +
                "[12:04:04] Relay defaults written" + Environment.NewLine +
                "[12:04:04] Source is ready to build";
        }
        UpdateLayout();
    }

    public void SaveScreenshot(string path)
    {
        RootGrid.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(RootGrid.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(RootGrid.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(RootGrid);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private async void BrowseTargetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a game executable or package",
            Filter = "Supported game files|*.exe;*.xex;*.pkg;*.self;*.bin|Windows executable|*.exe|Xbox 360 XEX|*.xex|PS3 package or executable|*.pkg;*.self;*.bin|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            await AnalyzeTargetAsync(dialog.FileName);
    }

    private void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the matching Legacy Console source folder",
            Multiselect = false
        };
        if (Directory.Exists(SourcePathBox.Text))
            dialog.InitialDirectory = SourcePathBox.Text;
        if (dialog.ShowDialog(this) == true)
        {
            SourcePathBox.Text = dialog.FolderName;
            FooterStatusText.Text = "Source folder selected";
            SaveSettings();
        }
    }

    private async void ValidateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureInputs())
            return;
        await RunWorkAsync("Validating source", async () =>
        {
            var result = await _patchService.ValidateSourceAsync(SourcePathBox.Text);
            if (!result.IsValid)
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Problems));

            ActionStatusText.Text = result.IsAlreadyPatched ? "Patch already installed" : "Ready to patch";
            ActionDetailText.Text = result.IsAlreadyPatched
                ? "The source contains the relay adapter. Apply will update its relay settings."
                : $"{result.CheckedFiles} baseline files matched exactly. A backup will be created before patching.";
            BuildButton.IsEnabled = result.IsAlreadyPatched;
            AppendLog($"[valid] Source validation passed. Patched={result.IsAlreadyPatched}");
        });
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureInputs())
            return;
        if (!TryReadConfiguration(out var config))
            return;

        await RunWorkAsync("Applying crossplay patch", async () =>
        {
            var result = await _patchService.ApplyAsync(
                SourcePathBox.Text,
                config,
                CancellationToken.None);
            ActionStatusText.Text = result.Applied ? "Patch applied" : "Configuration updated";
            ActionDetailText.Text = string.IsNullOrEmpty(result.BackupPath)
                ? "Relay defaults were updated. The source is ready to build."
                : $"Backup: {result.BackupPath}";
            BuildButton.IsEnabled = true;
            SaveSettings();
        });
    }

    private async void BuildButton_Click(object sender, RoutedEventArgs e)
    {
        if (_target is null || !EnsureInputs())
            return;

        await RunWorkAsync($"Building {_target.Platform}", async () =>
        {
            var result = await _patchService.BuildAsync(
                SourcePathBox.Text,
                _target.Platform,
                CancellationToken.None);
            ActionStatusText.Text = result.Succeeded ? "Build completed" : "Build failed";
            ActionDetailText.Text = result.Message;
            _buildOutput = result.OutputPath;
            OpenOutputButton.Visibility = string.IsNullOrEmpty(_buildOutput)
                ? Visibility.Collapsed
                : Visibility.Visible;
        });
    }

    private void OpenOutputButton_Click(object sender, RoutedEventArgs e)
    {
        if (_buildOutput is not null)
            _patchService.OpenContainingFolder(_buildOutput);
    }

    private async Task AnalyzeTargetAsync(string path)
    {
        await RunWorkAsync("Inspecting game file", async () =>
        {
            _target = await _patchService.AnalyzeTargetAsync(path);
            TargetPathBox.Text = path;
            TargetResultBorder.Visibility = Visibility.Visible;
            TargetFormatText.Text = $"{PlatformName(_target.Platform)} · {_target.Format} · {FormatSize(_target.Length)}";
            TargetHashText.Text = $"SHA-256  {_target.Sha256}";
            TargetStatusText.Text = _target.SignatureValid ? "SUPPORTED" : "NOT RECOGNIZED";
            TargetStatusText.Foreground = _target.SignatureValid
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("WarningBrush");
            TargetStatusBadge.Background = _target.SignatureValid
                ? new SolidColorBrush(Color.FromRgb(234, 247, 241))
                : new SolidColorBrush(Color.FromRgb(255, 246, 229));
            ActionDetailText.Text = _target.Summary;
            SaveSettings();
        });
    }

    private async Task RunWorkAsync(string status, Func<Task> operation)
    {
        if (_working)
            return;
        _working = true;
        SetActionsEnabled(false);
        WorkProgress.Visibility = Visibility.Visible;
        FooterStatusText.Text = status;
        AppendLog($"[start] {status}");
        try
        {
            await operation();
            FooterStatusText.Text = "Completed";
            AppendLog($"[done] {status}");
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = "Action failed";
            ActionStatusText.Text = "Could not complete action";
            ActionDetailText.Text = ex.Message;
            AppendLog($"[error] {ex.Message}");
            MessageBox.Show(this, ex.Message, "Legacy Crossplay Patcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            WorkProgress.Visibility = Visibility.Collapsed;
            SetActionsEnabled(true);
            _working = false;
        }
    }

    private void SetActionsEnabled(bool enabled)
    {
        ValidateButton.IsEnabled = enabled;
        ApplyButton.IsEnabled = enabled;
        if (!enabled)
            BuildButton.IsEnabled = false;
        else if (_target is not null &&
                 File.Exists(Path.Combine(SourcePathBox.Text, @"Minecraft.Client\Common\Network\Relay\RelayTransport.cpp")))
            BuildButton.IsEnabled = true;
    }

    private bool EnsureInputs()
    {
        if (_target is null || !_target.SignatureValid)
        {
            MessageBox.Show(this, "Choose a supported game file first.", "Missing game file", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!Directory.Exists(SourcePathBox.Text))
        {
            MessageBox.Show(this, "Choose the matching source folder.", "Missing source", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    private bool TryReadConfiguration(out RelayConfiguration config)
    {
        config = null!;
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show(this, "Enter a relay port between 1 and 65535.", "Invalid relay port", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var mode = ((ComboBoxItem)ModeBox.SelectedItem).Content?.ToString() ?? "local";
        config = new RelayConfiguration(
            HostBox.Text.Trim(),
            port,
            SessionBox.Text.Trim(),
            BuildIdBox.Text.Trim(),
            mode,
            TokenBox.Text.Trim());
        return true;
    }

    private void PatchNavButton_Click(object sender, RoutedEventArgs e) => ShowPage("patch");
    private void GuideNavButton_Click(object sender, RoutedEventArgs e) => ShowPage("guide");
    private void LogNavButton_Click(object sender, RoutedEventArgs e) => ShowPage("log");

    private void ShowPage(string page)
    {
        PatchPage.Visibility = page == "patch" ? Visibility.Visible : Visibility.Collapsed;
        GuidePage.Visibility = page == "guide" ? Visibility.Visible : Visibility.Collapsed;
        LogPage.Visibility = page == "log" ? Visibility.Visible : Visibility.Collapsed;
        SetNavState(PatchNavButton, page == "patch");
        SetNavState(GuideNavButton, page == "guide");
        SetNavState(LogNavButton, page == "log");
        (PageTitleText.Text, PageSubtitleText.Text) = page switch
        {
            "guide" => ("Setup guide", "Platform-specific instructions kept beside the patch workflow."),
            "log" => ("Activity log", "Validation, patch, and build output from this session."),
            _ => ("Patch client", "Validate, configure, patch, and build one supported client.")
        };
    }

    private static void SetNavState(Button button, bool selected)
    {
        button.Background = selected
            ? new SolidColorBrush(Color.FromRgb(38, 58, 87))
            : Brushes.Transparent;
        button.Foreground = selected ? Brushes.White : new SolidColorBrush(Color.FromRgb(201, 211, 226));
    }

    private void QuickGuideButton_Click(object sender, RoutedEventArgs e) => ShowGuide("quick");
    private void PcGuideButton_Click(object sender, RoutedEventArgs e) => ShowGuide("pc");
    private void XboxGuideButton_Click(object sender, RoutedEventArgs e) => ShowGuide("xbox");
    private void Ps3GuideButton_Click(object sender, RoutedEventArgs e) => ShowGuide("ps3");
    private void RelayGuideButton_Click(object sender, RoutedEventArgs e) => ShowGuide("relay");
    private void TroubleshootingGuideButton_Click(object sender, RoutedEventArgs e) => ShowGuide("troubleshooting");

    private void ShowGuide(string topic)
    {
        var guide = topic switch
        {
            "pc" => (
                "PC / Windows",
                "Build the native Windows64 LCE client from the same patched source used by the consoles.",
                """
                1. Select your existing Minecraft Game so the patcher identifies the PC target and records its SHA-256.

                2. Select the clean matching LCE source root.

                3. Enter the relay address. For a relay on this PC use 127.0.0.1. Use the same session and build ID as every console.

                4. Choose Validate, then Apply patch. The patcher creates LegacyCrossplayBackups inside the source tree before changing files.

                5. Choose Build client. The patcher invokes Release|x64. Run the result from its complete output folder so its DLLs and Common resources remain beside it.

                PC can override relay settings at runtime with CONSOLE_LEGACY_RELAY_ADDR, CONSOLE_LEGACY_RELAY_SESSION, CONSOLE_LEGACY_RELAY_BUILD_ID, and CONSOLE_LEGACY_RELAY_TOKEN.
                """),
            "xbox" => (
                "Xbox 360 / Xenia",
                "XEX files require the Xbox 360 platform toolchain; a desktop compiler cannot create or sign them.",
                """
                1. Select your legally obtained Minecraft Game. The patcher validates the Xbox 360 signature but does not decrypt or upload it.

                2. Select the matching clean LCE source root and enter the relay PC or VPS numeric IPv4 address. Do not use 127.0.0.1 for a physical console.

                3. Validate and apply. Relay defaults are written into LegacyRelayUserConfig.h and compiled into the console build.

                4. Choose Build client only when the licensed Xbox 360 SDK and its Visual Studio/MSBuild integration are installed. The target is Release|Xbox 360.

                5. Keep the newly built Minecraft Game beside the matching resources from your own dump, then boot it in Xenia or on compatible development hardware.

                6. Before joining through Xenia, select one valid Xenia profile and confirm it is signed in. Restart Xenia after changing profiles.

                The patcher does not include an XDK, title update, signing key, game content, or license bypass.
                """),
            "ps3" => (
                "PS3 / RPCS3",
                "The tested target is BLES01976 update 1.84, APP_VER 01.84, using the matching source baseline.",
                """
                1. Select your legally obtained Minecraft Game. The patcher identifies the PS3 target without decrypting or modifying the package directly.

                2. Select the matching source root. Set the relay to a numeric IPv4 address reachable by RPCS3 or the console.

                3. Validate and apply. The current relay transport includes PS3 join pacing at 32 KiB per game-loop pass.

                4. Build client requires the PS3 SDK/project integration used by your legally obtained source environment. Set LCE_PS3_VCTARGETS_PATH when the custom PS3 MSBuild targets are not registered globally.

                5. A successful build produces a new Minecraft Game build. Packaging or installing it into your own update layout remains a separate platform-toolchain step.

                PSL1GHT can build standalone probes but is not a complete replacement for every library used by the original full game project.
                """),
            "relay" => (
                "Relay and VPS",
                "The relay routes sessions and does not parse or host the Minecraft world.",
                """
                LOCAL / LAN
                Run the relay on the PC and listen on port 61000. Use 127.0.0.1 for a PC client on that machine. Consoles must use the PC's LAN IPv4 address.

                EXTERNAL VPS
                Select mode vps, enter the VPS numeric public IPv4 address, and use the same access token on the relay and every client. Open TCP 61000 only to the players who need it.

                ALL CLIENTS MUST MATCH
                Session ID, build ID, relay address, port, and access token must be identical. Start the relay first, then enter the PC host world, then join Xbox and PS3.

                The shared token authenticates the relay handshake but does not encrypt gameplay traffic. Prefer a VPN or strict firewall rules for public deployments.
                """),
            "troubleshooting" => (
                "Troubleshooting",
                "Most failures come from mixed source revisions, stale executables, or an unreachable relay.",
                """
                BASELINE MISMATCH
                Start from a clean copy of the exact LCE 1.2.3 / net 495 source. Do not force the patch over an older experiment or another title update.

                BUILD TOOLCHAIN NOT FOUND
                Install the platform toolchain required by the source project. The patcher deliberately does not download proprietary console SDKs.

                XENIA CONNECTING FOREVER
                Close Minecraft, select one valid signed-in Xenia profile, restart Xenia, and retry. If it still stalls, back up the original profile and test with a fresh Xenia-generated profile before changing relay settings.

                XUI DLC INSTALL FREEZE
                Version 0.2.3 bypasses StartInstallDLCProcess in relay create/join scenes. Reapply the patch and rebuild the Xbox 360 target; changing an existing XEX is not enough.

                CONNECTING FOREVER
                Confirm the relay is listening, the PC host is already inside an online world, and every client has exactly the same session and build ID.

                CLIENT CLOSES ON JOIN
                Compare SHA-256 hashes and rebuild stale targets. A client may reach the loading screen even when its packet-facing code is incompatible.

                ONLY A SMALL AREA LOADS
                Rebuild every client with the raw BlockRegionUpdatePacket path from the same patch revision.

                PS3 JOIN STUTTER
                Use the current RelayTransport.cpp. The default 32 KiB receive budget prevents the initial world burst from monopolizing a PS3 frame.
                """),
            _ => (
                "Quick start",
                "Patch one verified source tree, then build every participating platform from that same revision.",
                """
                1. CHOOSE A GAME FILE
                Add your existing Minecraft Game. The file stays local and is used to identify the correct platform build target.

                2. CHOOSE THE SOURCE
                Select the legal matching LCE source root. The patcher checks the tested baseline before changing anything.

                3. CONFIGURE THE RELAY
                For local PC testing use 127.0.0.1:61000. Consoles need the relay PC's LAN IPv4 address. Keep the same session and build ID everywhere.

                4. VALIDATE AND APPLY
                Validate first. Apply creates a timestamped backup, installs the crossplay source patch, copies the relay adapter, and saves relay defaults.

                5. BUILD
                Build client invokes the matching Release target when its toolchain is installed. Console packages still require their normal platform packaging/signing flow.

                6. TEST
                Start the relay, host on PC, then join Xbox and PS3. Verify login, chat, movement, player visibility, and full chunk loading.
                """)
        };
        GuideTitleText.Text = guide.Item1;
        GuideIntroText.Text = guide.Item2;
        GuideBodyText.Text = guide.Item3;
    }

    private void CopyLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(LogBox.Text))
            Clipboard.SetText(LogBox.Text);
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    private void AppendLog(string line)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length == 1 && File.Exists(files[0]))
            await AnalyzeTargetAsync(files[0]);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return;
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath));
            if (settings is null)
                return;
            SourcePathBox.Text = settings.SourceRoot;
            HostBox.Text = settings.Host;
            PortBox.Text = settings.Port.ToString();
            SessionBox.Text = settings.Session;
            BuildIdBox.Text = settings.BuildId;
            ModeBox.SelectedIndex = settings.Mode == "vps" ? 1 : 0;
            if (File.Exists(settings.TargetPath))
                Dispatcher.BeginInvoke(async () => await AnalyzeTargetAsync(settings.TargetPath));
        }
        catch (Exception ex)
        {
            AppendLog($"[warning] Settings were not loaded: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var settings = new AppSettings
            {
                TargetPath = TargetPathBox.Text,
                SourceRoot = SourcePathBox.Text,
                Host = HostBox.Text.Trim(),
                Port = int.TryParse(PortBox.Text, out var port) ? port : 61000,
                Session = SessionBox.Text.Trim(),
                BuildId = BuildIdBox.Text.Trim(),
                Mode = ((ComboBoxItem)ModeBox.SelectedItem).Content?.ToString() ?? "local"
            };
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppendLog($"[warning] Settings were not saved: {ex.Message}");
        }
    }

    private static string PlatformName(GamePlatform platform) => platform switch
    {
        GamePlatform.Pc => "PC / Windows",
        GamePlatform.Xbox360 => "Xbox 360",
        GamePlatform.PlayStation3 => "PlayStation 3",
        _ => "Unknown platform"
    };

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
