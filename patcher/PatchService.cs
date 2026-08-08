using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace LegacyCrossplayPatcher;

public sealed class PatchService
{
    private static readonly string[] RelayFiles =
    {
        "LegacyRelayPolicy.h",
        "LegacyRelayUserConfig.h",
        "NetworkPlayerRelay.cpp",
        "NetworkPlayerRelay.h",
        "PlatformNetworkManagerRelay.cpp",
        "PlatformNetworkManagerRelay.h",
        "RelayTransport.cpp",
        "RelayTransport.h"
    };

    private readonly string _bundleRoot;
    public event Action<string>? LogLine;

    public PatchService()
    {
        _bundleRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LegacyCrossplayPatcher",
            "bundle-0.2.0");
        ExtractBundle();
    }

    public async Task<TargetAnalysis> AnalyzeTargetAsync(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Select an existing game executable or package.", path);

        Log($"Inspecting {Path.GetFileName(path)}");
        var header = new byte[8];
        await using (var stream = File.OpenRead(path))
        {
            _ = await stream.ReadAsync(header);
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var platform = GamePlatform.Unknown;
        var format = "Unknown";
        var valid = false;

        if (header[0] == (byte)'M' && header[1] == (byte)'Z')
        {
            platform = GamePlatform.Pc;
            format = "Windows PE executable";
            valid = extension == ".exe";
        }
        else if (header[0] == (byte)'X' && header[1] == (byte)'E' &&
                 header[2] == (byte)'X' && (header[3] == (byte)'1' || header[3] == (byte)'2'))
        {
            platform = GamePlatform.Xbox360;
            format = "Xbox 360 XEX";
            valid = extension == ".xex";
        }
        else if (header[0] == 0x7f && header[1] == (byte)'P' &&
                 header[2] == (byte)'K' && header[3] == (byte)'G')
        {
            platform = GamePlatform.PlayStation3;
            format = "PlayStation 3 PKG";
            valid = extension == ".pkg";
        }
        else if (header[0] == (byte)'S' && header[1] == (byte)'C' &&
                 header[2] == (byte)'E' && header[3] == 0)
        {
            platform = GamePlatform.PlayStation3;
            format = "PlayStation 3 SELF";
            valid = extension is ".self" or ".bin";
        }

        var info = new FileInfo(path);
        await using var hashStream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream));
        var summary = valid
            ? $"{format} detected. The file will be used as the build target reference and will not be uploaded."
            : "The file signature does not match a supported Windows EXE, Xbox 360 XEX, PS3 PKG, or PS3 SELF.";

        Log($"{format}; SHA-256 {hash}");
        return new TargetAnalysis(path, platform, format, hash, info.Length, valid, summary);
    }

    public async Task<SourceValidation> ValidateSourceAsync(string sourceRoot)
    {
        if (!Directory.Exists(sourceRoot))
            return new SourceValidation(false, false, 0, new[] { "Source folder does not exist." });

        var required = new[]
        {
            "MinecraftConsoles.sln",
            @"Minecraft.Client\Minecraft.Client.vcxproj",
            @"Minecraft.Client\Common\Network\GameNetworkManager.cpp",
            @"Minecraft.World\LevelChunk.cpp"
        };
        var problems = required
            .Where(relative => !File.Exists(Path.Combine(sourceRoot, relative)))
            .Select(relative => $"Missing {relative}")
            .ToList();
        if (problems.Count > 0)
            return new SourceValidation(false, false, 0, problems);

        var manager = await File.ReadAllTextAsync(Path.Combine(
            sourceRoot,
            @"Minecraft.Client\Common\Network\GameNetworkManager.cpp"));
        if (manager.Contains("CPlatformNetworkManagerRelay", StringComparison.Ordinal))
        {
            var configPath = Path.Combine(
                sourceRoot,
                @"Minecraft.Client\Common\Network\Relay\LegacyRelayUserConfig.h");
            if (!File.Exists(configPath))
                problems.Add("Relay patch detected, but LegacyRelayUserConfig.h is missing.");

            var xuiCreatePath = Path.Combine(
                sourceRoot,
                @"Minecraft.Client\Common\XUI\XUI_MultiGameCreate.cpp");
            var xuiJoinPath = Path.Combine(
                sourceRoot,
                @"Minecraft.Client\Common\XUI\XUI_MultiGameJoinLoad.cpp");
            var hasCurrentXuiPatch = File.Exists(xuiCreatePath) &&
                File.Exists(xuiJoinPath) &&
                (await File.ReadAllTextAsync(xuiCreatePath)).Contains(
                    "LegacyRelayPolicy::UsesPlatformDLCInstall", StringComparison.Ordinal) &&
                (await File.ReadAllTextAsync(xuiJoinPath)).Contains(
                    "LegacyRelayPolicy::UsesPlatformDLCInstall", StringComparison.Ordinal);
            if (!hasCurrentXuiPatch)
            {
                problems.Add(
                    "An older relay patch is installed. Restore the clean source backup, then apply patcher 0.2.3 so the Xbox 360 XUI DLC fix is included.");
            }
            return new SourceValidation(problems.Count == 0, true, 0, problems);
        }

        var manifest = Path.Combine(_bundleRoot, @"patches\baseline.sha256");
        var checkedFiles = 0;
        foreach (var line in await File.ReadAllLinesAsync(manifest))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var parts = line.Split("  ", 2, StringSplitOptions.None);
            if (parts.Length != 2)
                continue;
            checkedFiles++;
            var relative = parts[1].Replace('/', Path.DirectorySeparatorChar);
            var candidate = Path.Combine(sourceRoot, relative);
            if (!File.Exists(candidate))
            {
                problems.Add($"Missing {relative}");
                continue;
            }
            await using var stream = File.OpenRead(candidate);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            if (!actual.Equals(parts[0], StringComparison.OrdinalIgnoreCase))
                problems.Add($"Baseline mismatch: {relative}");
        }

        return new SourceValidation(problems.Count == 0, false, checkedFiles, problems);
    }

    public async Task<PatchResult> ApplyAsync(
        string sourceRoot,
        RelayConfiguration config,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration(config);
        var validation = await ValidateSourceAsync(sourceRoot);
        if (!validation.IsValid)
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Problems));

        if (validation.IsAlreadyPatched)
        {
            WriteUserConfig(sourceRoot, config);
            Log("Existing relay patch detected; relay configuration updated.");
            return new PatchResult(false, true, "", "Relay configuration updated.");
        }

        var backup = await CreateBackupAsync(sourceRoot);
        var git = FindGit();
        var patchPath = Path.Combine(_bundleRoot, @"patches\crossplay-core.patch");
        Log("Running patch compatibility check.");
        await RunProcessAsync(
            git,
            new[] { "-c", "core.autocrlf=false", "apply", "--no-index", "--check", "--whitespace=nowarn", patchPath },
            sourceRoot,
            cancellationToken);

        var applied = false;
        try
        {
            Log("Applying crossplay source patch.");
            await RunProcessAsync(
                git,
                new[] { "-c", "core.autocrlf=false", "apply", "--no-index", "--whitespace=nowarn", patchPath },
                sourceRoot,
                cancellationToken);
            applied = true;

            var relayTarget = Path.Combine(sourceRoot, @"Minecraft.Client\Common\Network\Relay");
            Directory.CreateDirectory(relayTarget);
            foreach (var name in RelayFiles)
            {
                File.Copy(
                    Path.Combine(_bundleRoot, "patches", "relay", name),
                    Path.Combine(relayTarget, name),
                    true);
            }
            WriteUserConfig(sourceRoot, config);
        }
        catch
        {
            if (applied)
            {
                try
                {
                    await RunProcessAsync(
                        git,
                        new[] { "-c", "core.autocrlf=false", "apply", "--no-index", "--reverse", "--whitespace=nowarn", patchPath },
                        sourceRoot,
                        CancellationToken.None);
                }
                catch
                {
                    Log($"Automatic rollback failed. Restore the source from {backup}");
                }
            }
            throw;
        }

        Log("Crossplay patch and relay configuration applied successfully.");
        return new PatchResult(true, true, backup, "Source patch applied successfully.");
    }

    public async Task<BuildResult> BuildAsync(
        string sourceRoot,
        GamePlatform platform,
        CancellationToken cancellationToken)
    {
        if (platform == GamePlatform.Unknown)
            throw new InvalidOperationException("Select a supported game file before building.");

        var project = Path.Combine(sourceRoot, @"Minecraft.Client\Minecraft.Client.vcxproj");
        if (!File.Exists(project))
            throw new FileNotFoundException("Minecraft.Client.vcxproj was not found.", project);

        var msbuild = FindMsBuild();
        var platformName = platform switch
        {
            GamePlatform.Pc => "x64",
            GamePlatform.Xbox360 => "Xbox 360",
            GamePlatform.PlayStation3 => "PS3",
            _ => throw new InvalidOperationException("Unsupported platform.")
        };
        var started = DateTime.Now;
        var arguments = new List<string>
        {
            project,
            "/t:Build",
            "/p:Configuration=Release",
            $"/p:Platform={platformName}",
            "/m:1",
            "/v:minimal"
        };

        if (platform == GamePlatform.PlayStation3)
        {
            arguments.Add("/p:TrackFileAccess=false");
            var targetsPath = FindPs3TargetsPath(sourceRoot);
            if (targetsPath is not null)
                arguments.Add($"/p:VCTargetsPath={targetsPath.TrimEnd('\\')}\\");
        }

        Log($"Building Release|{platformName}.");
        await RunProcessAsync(msbuild, arguments, sourceRoot, cancellationToken);

        var patterns = platform switch
        {
            GamePlatform.Pc => new[] { "MINECRAFT.CLIENT.EXE", "Minecraft.Client.exe" },
            GamePlatform.Xbox360 => new[] { "default.xex", "Minecraft.Client.xex" },
            GamePlatform.PlayStation3 => new[] { "Minecraft.Client.self", "EBOOT.BIN" },
            _ => Array.Empty<string>()
        };
        var output = patterns
            .SelectMany(pattern => Directory.EnumerateFiles(sourceRoot, pattern, SearchOption.AllDirectories))
            .Select(path => new FileInfo(path))
            .Where(file => file.LastWriteTime >= started.AddSeconds(-2))
            .OrderByDescending(file => file.LastWriteTime)
            .FirstOrDefault();

        if (output is null)
            return new BuildResult(true, null, "Build completed, but no newly written client output was detected.");

        Log($"Build output: {output.FullName}");
        return new BuildResult(true, output.FullName, "Patched client built successfully.");
    }

    public void OpenContainingFolder(string path)
    {
        if (!File.Exists(path))
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
        {
            UseShellExecute = true
        });
    }

    private async Task<string> CreateBackupAsync(string sourceRoot)
    {
        var backupRoot = Path.Combine(
            sourceRoot,
            "LegacyCrossplayBackups",
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(backupRoot);
        var manifest = Path.Combine(_bundleRoot, @"patches\baseline.sha256");
        foreach (var line in await File.ReadAllLinesAsync(manifest))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var parts = line.Split("  ", 2, StringSplitOptions.None);
            if (parts.Length != 2)
                continue;
            var relative = parts[1].Replace('/', Path.DirectorySeparatorChar);
            var source = Path.Combine(sourceRoot, relative);
            var destination = Path.Combine(backupRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
        }
        Log($"Backup created: {backupRoot}");
        return backupRoot;
    }

    private static void ValidateConfiguration(RelayConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.Host))
            throw new InvalidOperationException("Relay host is required.");
        if (config.Port is < 1 or > 65535)
            throw new InvalidOperationException("Relay port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(config.Session))
            throw new InvalidOperationException("Session ID is required.");
        if (string.IsNullOrWhiteSpace(config.BuildId))
            throw new InvalidOperationException("Build ID is required.");

        foreach (var value in new[] { config.Host, config.Session, config.BuildId, config.Mode, config.Token })
        {
            if (value.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
                throw new InvalidOperationException("Relay settings cannot contain quotes or line breaks.");
        }
    }

    private void WriteUserConfig(string sourceRoot, RelayConfiguration config)
    {
        var path = Path.Combine(
            sourceRoot,
            @"Minecraft.Client\Common\Network\Relay\LegacyRelayUserConfig.h");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = $"""
            #pragma once

            // Generated by Legacy Crossplay Patcher.
            #define CONSOLE_LEGACY_RELAY_ADDR_DEFAULT "{config.Host}:{config.Port}"
            #define CONSOLE_LEGACY_RELAY_MODE_DEFAULT "{config.Mode}"
            #define CONSOLE_LEGACY_RELAY_SESSION_DEFAULT "{config.Session}"
            #define CONSOLE_LEGACY_RELAY_BUILD_DEFAULT "{config.BuildId}"
            #define CONSOLE_LEGACY_RELAY_TOKEN_DEFAULT "{config.Token}"
            """;
        File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
        Log($"Relay defaults written to {path}");
    }

    private void ExtractBundle()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = new Dictionary<string, string>
        {
            ["Bundle.patches.baseline.sha256"] = @"patches\baseline.sha256",
            ["Bundle.patches.crossplay-core.patch"] = @"patches\crossplay-core.patch",
            ["Bundle.patches.relay.LegacyRelayPolicy.h"] = @"patches\relay\LegacyRelayPolicy.h",
            ["Bundle.patches.relay.LegacyRelayUserConfig.h"] = @"patches\relay\LegacyRelayUserConfig.h",
            ["Bundle.patches.relay.NetworkPlayerRelay.cpp"] = @"patches\relay\NetworkPlayerRelay.cpp",
            ["Bundle.patches.relay.NetworkPlayerRelay.h"] = @"patches\relay\NetworkPlayerRelay.h",
            ["Bundle.patches.relay.PlatformNetworkManagerRelay.cpp"] = @"patches\relay\PlatformNetworkManagerRelay.cpp",
            ["Bundle.patches.relay.PlatformNetworkManagerRelay.h"] = @"patches\relay\PlatformNetworkManagerRelay.h",
            ["Bundle.patches.relay.RelayTransport.cpp"] = @"patches\relay\RelayTransport.cpp",
            ["Bundle.patches.relay.RelayTransport.h"] = @"patches\relay\RelayTransport.h"
        };

        foreach (var (resource, relative) in resources)
        {
            var destination = Path.Combine(_bundleRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Embedded patch resource is missing: {resource}");
            using var output = File.Create(destination);
            input.CopyTo(output);
        }
    }

    private static string FindGit()
    {
        var candidates = new[]
        {
            FindOnPath("git.exe"),
            @"C:\Program Files\Git\cmd\git.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe"
        };
        return candidates.FirstOrDefault(path => path is not null && File.Exists(path))
            ?? throw new InvalidOperationException("Git for Windows was not found. Install Git, then restart the patcher.");
    }

    private static string FindMsBuild()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"),
            @"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("MSBuild was not found. Install the toolchain required by the selected source target.");
    }

    private static string? FindPs3TargetsPath(string sourceRoot)
    {
        var configured = Environment.GetEnvironmentVariable("LCE_PS3_VCTARGETS_PATH");
        if (!string.IsNullOrWhiteSpace(configured) &&
            File.Exists(Path.Combine(configured, @"Platforms\PS3\Microsoft.Cpp.PS3.targets")))
            return configured;

        var current = new DirectoryInfo(sourceRoot);
        for (var level = 0; level < 7 && current is not null; level++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, @"toolchains\MSBuild\Microsoft.Cpp\v4.0");
            if (File.Exists(Path.Combine(candidate, @"Platforms\PS3\Microsoft.Cpp.PS3.targets")))
                return candidate;
        }
        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }
        return null;
    }

    private async Task RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        if (Path.GetFileNameWithoutExtension(executable).Equals("git", StringComparison.OrdinalIgnoreCase))
        {
            // --no-index lets git apply operate without repository metadata.
            // Disable discovery so an unrelated parent repo cannot add a prefix.
            start.Environment["GIT_DIR"] = "NUL";
        }

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                Log(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                Log(e.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(executable)} exited with code {process.ExitCode}. See the log for details.");
    }

    private void Log(string line)
    {
        LogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {line}");
    }
}
