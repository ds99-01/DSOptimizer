namespace DSOptimizer.Core.Localization;

/// <summary>Jeden plik tłumaczeń: nazwa (do komunikatów o błędach) i jego treść JSON.</summary>
public sealed record TranslationSource(string Name, string Json);
