using System;

namespace Vidvix.Core.Models;

public sealed class VideoPreviewFailedEventArgs : EventArgs
{
    public VideoPreviewFailedEventArgs(string sourcePath, string message, int errorCode)
    {
        SourcePath = sourcePath;
        Message = message;
        ErrorCode = errorCode;
    }

    public string SourcePath { get; }

    public string Message { get; }

    public int ErrorCode { get; }
}
