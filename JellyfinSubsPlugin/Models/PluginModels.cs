namespace Jellyfin.Plugin.SubsRo.Models;

internal record SubtitleFile
{
    public required string FileName { get; init; }
    public required byte[] Content { get; init; }
}
