namespace PC2Pad.Host.Models;

public sealed record GameEntry(
    string Id,
    string Title,
    string? Executable,
    string? Arguments,
    string? WorkingDirectory,
    string? CoverUrl,
    bool Enabled
);
