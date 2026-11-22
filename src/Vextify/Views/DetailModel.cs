namespace Vextify.Views;

/// <summary>What the detail window shows. Local mods, installed themes and catalog entries all use the same window.</summary>
public sealed class DetailModel
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? PreviewUrl { get; init; }
    public string? PerfNote { get; init; }

    /// <summary>Optional: fetches a longer text (e.g. the README) after the window is open.</summary>
    public Func<Task<string?>>? LoadMore { get; init; }

    public string? PrimaryText { get; init; }
    /// <summary>Returns (success, message shown as a toast).</summary>
    public Func<Task<(bool ok, string message)>>? PrimaryAction { get; init; }

    public string? LinkText { get; init; }
    public string? LinkUrl { get; init; }
}
