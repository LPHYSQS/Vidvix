using System;
using System.IO;

namespace Vidvix.Core.Models;

public sealed class LocalizedInvalidOperationException : InvalidOperationException
{
    private readonly Func<string> _messageResolver;

    public LocalizedInvalidOperationException(Func<string> messageResolver, Exception? innerException = null)
        : base((messageResolver ?? throw new ArgumentNullException(nameof(messageResolver))).Invoke(), innerException)
    {
        _messageResolver = messageResolver;
    }

    public override string Message => _messageResolver();
}

public sealed class LocalizedFileNotFoundException : FileNotFoundException
{
    private readonly Func<string> _messageResolver;

    public LocalizedFileNotFoundException(Func<string> messageResolver, string? fileName)
        : base((messageResolver ?? throw new ArgumentNullException(nameof(messageResolver))).Invoke(), fileName)
    {
        _messageResolver = messageResolver;
    }

    public override string Message => _messageResolver();
}
