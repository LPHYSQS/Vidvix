namespace Vidvix.Core.Models;

public readonly record struct VideoPreviewHostPlacement(
    int X,
    int Y,
    int Width,
    int Height,
    bool IsVisible);
