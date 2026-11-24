namespace Pereulki.Launcher.Models;

public enum EffectQuality
{
    Low,
    High
}

public enum LaunchBehavior
{
    KeepOpen,
    Minimize,
    Close
}

public sealed record LauncherSettings
{
    public string GamePath { get; init; } = string.Empty;
    public bool AnimationsEnabled { get; init; } = true;
    public EffectQuality EffectQuality { get; init; } = EffectQuality.High;
    public LaunchBehavior LaunchBehavior { get; init; } = LaunchBehavior.Minimize;
}
