namespace LegacyCrossplayPatcher;

public enum GamePlatform
{
    Unknown,
    Pc,
    Xbox360,
    PlayStation3
}

public sealed record TargetAnalysis(
    string Path,
    GamePlatform Platform,
    string Format,
    string Sha256,
    long Length,
    bool SignatureValid,
    string Summary);

public sealed record SourceValidation(
    bool IsValid,
    bool IsAlreadyPatched,
    int CheckedFiles,
    IReadOnlyList<string> Problems);

public sealed record RelayConfiguration(
    string Host,
    int Port,
    string Session,
    string BuildId,
    string Mode,
    string Token);

public sealed record PatchResult(
    bool Applied,
    bool ConfigurationUpdated,
    string BackupPath,
    string Message);

public sealed record BuildResult(
    bool Succeeded,
    string? OutputPath,
    string Message);

public sealed class AppSettings
{
    public string TargetPath { get; set; } = "";
    public string SourceRoot { get; set; } = "";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 61000;
    public string Session { get; set; } = "local-test";
    public string BuildId { get; set; } = "584111F7-1.0.10.0-lce1.2.3-net495-proto39";
    public string Mode { get; set; } = "local";
}
