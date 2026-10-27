namespace DSOptimizer.Core.Abstractions;

/// <summary>Wpis z listy punktów przywracania (root\default:SystemRestore).</summary>
public sealed record RestorePointInfo(uint SequenceNumber, string Description);
