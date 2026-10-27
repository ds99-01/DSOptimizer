using System;

namespace DSOptimizer.Core.Gui;

public enum GuiLogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>Linia raportu pokazywana w oknie.</summary>
public sealed record GuiLogLine(DateTimeOffset At, GuiLogLevel Level, string Text, string? Path = null);