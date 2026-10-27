using System;

namespace DSOptimizer.Core.Errors;

/// <summary>Błąd zapisu/wykonania operacji systemowej z kategorią, na którą silnik reaguje rollbackiem.</summary>
public sealed class OperationException : Exception
{
    public OperationException(OperationErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public OperationErrorKind Kind { get; }
}
