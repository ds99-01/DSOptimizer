using System;

namespace DSOptimizer.Core.Abstractions;

public interface ILastBootTime
{
    /// <summary>Czas ostatniego startu systemu. null = nieznany. Nigdy nie rzuca.</summary>
    DateTimeOffset? Read();
}
