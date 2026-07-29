using LegacyCrossplayPatcher;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PatcherSmokeTests <source-fixture> <target-exe>");
    return 2;
}

var sourceRoot = Path.GetFullPath(args[0]);
var targetExe = Path.GetFullPath(args[1]);
var service = new PatchService();
service.LogLine += Console.WriteLine;

var target = await service.AnalyzeTargetAsync(targetExe);
Require(target.SignatureValid, "PE target signature was not recognized.");
Require(target.Platform == GamePlatform.Pc, "PE target was not classified as PC.");

var before = await service.ValidateSourceAsync(sourceRoot);
Require(before.IsValid, "Clean source did not pass validation.");
Require(!before.IsAlreadyPatched, "Clean source was incorrectly marked patched.");
Require(before.CheckedFiles == 30, $"Expected 30 baseline files, got {before.CheckedFiles}.");

var firstConfig = new RelayConfiguration(
    "192.168.50.10",
    61000,
    "smoke-test",
    "584111F7-1.0.10.0-lce1.2.3-net495-proto39",
    "local",
    "");
var applied = await service.ApplyAsync(sourceRoot, firstConfig, CancellationToken.None);
Require(applied.Applied, "First run did not apply the source patch.");
Require(Directory.Exists(applied.BackupPath), "Backup directory was not created.");

var relayRoot = Path.Combine(sourceRoot, @"Minecraft.Client\Common\Network\Relay");
var expectedRelayFiles = new[]
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
foreach (var name in expectedRelayFiles)
    Require(File.Exists(Path.Combine(relayRoot, name)), $"Missing relay file: {name}");

var after = await service.ValidateSourceAsync(sourceRoot);
Require(after.IsValid && after.IsAlreadyPatched, "Patched source was not recognized.");

var secondConfig = firstConfig with { Host = "10.20.30.40", Session = "updated-session" };
var updated = await service.ApplyAsync(sourceRoot, secondConfig, CancellationToken.None);
Require(!updated.Applied && updated.ConfigurationUpdated, "Second run was not configuration-only.");

var generated = await File.ReadAllTextAsync(Path.Combine(relayRoot, "LegacyRelayUserConfig.h"));
Require(generated.Contains("\"10.20.30.40:61000\"", StringComparison.Ordinal), "Updated relay address is missing.");
Require(generated.Contains("\"updated-session\"", StringComparison.Ordinal), "Updated session is missing.");

Console.WriteLine("PATCHER_SMOKE_TEST_OK");
return 0;

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
