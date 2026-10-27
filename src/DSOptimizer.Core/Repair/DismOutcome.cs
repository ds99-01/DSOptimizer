using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DSOptimizer.Core.Repair;

/// <summary>Werdykt <c>DISM /Online /Cleanup-Image /RestoreHealth</c>.</summary>
public enum DismVerdict
{
    /// <summary>Kod 0, blok podsumowania CBS.log: wykryto 0 uszkodzeń (zmierzone na VM 26H2).</summary>
    Healthy,

    /// <summary>Kod 0, wykryte uszkodzenia naprawione w całości (format z KB 947821, niezmierzony).</summary>
    Repaired,

    /// <summary>Kod 0 bez czytelnego bloku podsumowania: zdrowy albo naprawiony, nie wiadomo który.</summary>
    HealthyOrRepaired,

    /// <summary>Kod 3010: naprawa dokończy się po restarcie (niezmierzony).</summary>
    RebootRequired,

    /// <summary>Brak plików źródłowych (0x800F081F, 0x800F0906, 0x800F0907 – dokumentacja MS, niezmierzony).</summary>
    SourceMissing,

    /// <summary>Inny kod albo wykryto więcej uszkodzeń, niż naprawiono.</summary>
    Failed,
}

/// <summary>
/// Wynik DISM z kodu wyjścia i bloku „Checking System Update Readiness.” w segmencie CBS.log z tego przebiegu (pomiar Task 2,
/// pkt 1z: samo wyjście konsoli i dism.log nie odróżniają „zdrowy” od „naprawiony”, a zdania bywają zlokalizowane).
/// <c>0x800F0950</c> nie jest brakiem źródła (Ruling R13: MS go nie opisuje).
/// </summary>
public sealed record DismOutcome(DismVerdict Verdict, int ExitCode, int? Detected, int? Repaired)
{
    public const int RebootRequiredExitCode = 3010;

    private const string SummaryStart = "Checking System Update Readiness.";

    private static readonly HashSet<int> SourceMissingCodes = new()
    {
        unchecked((int)0x800F081F),
        unchecked((int)0x800F0906),
        unchecked((int)0x800F0907),
    };

    private static readonly Regex DetectedCount = new(@"Total Detected Corruption:\s*([0-9]{1,9})", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RepairedCount = new(@"Total Repaired Corruption:\s*([0-9]{1,9})", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public bool IsSuccess => Verdict is DismVerdict.Healthy or DismVerdict.Repaired or DismVerdict.HealthyOrRepaired or DismVerdict.RebootRequired;

    public static DismOutcome Classify(int exitCode, string? cbsSegment)
    {
        if (exitCode == RebootRequiredExitCode)
        {
            return new DismOutcome(DismVerdict.RebootRequired, exitCode, null, null);
        }

        if (SourceMissingCodes.Contains(exitCode))
        {
            return new DismOutcome(DismVerdict.SourceMissing, exitCode, null, null);
        }

        if (exitCode != 0)
        {
            return new DismOutcome(DismVerdict.Failed, exitCode, null, null);
        }

        var (detected, repaired) = ReadSummary(cbsSegment);
        if (detected is not { } d || repaired is not { } r)
        {
            return new DismOutcome(DismVerdict.HealthyOrRepaired, exitCode, null, null);
        }

        var verdict = d == 0 ? DismVerdict.Healthy : r >= d ? DismVerdict.Repaired : DismVerdict.Failed;
        return new DismOutcome(verdict, exitCode, d, r);
    }

    /// <summary>Kod jako HRESULT: <c>0x</c> i 8 cyfr szesnastkowych.</summary>
    public static string Hex(int code) => "0x" + unchecked((uint)code).ToString("X8", CultureInfo.InvariantCulture);

    private static (int? Detected, int? Repaired) ReadSummary(string? segment)
    {
        if (string.IsNullOrEmpty(segment))
        {
            return (null, null);
        }

        var start = segment!.LastIndexOf(SummaryStart, StringComparison.Ordinal);
        if (start < 0)
        {
            return (null, null);
        }

        var block = segment.Substring(start);
        return (Count(DetectedCount, block), Count(RepairedCount, block));
    }

    private static int? Count(Regex pattern, string block)
    {
        var match = pattern.Match(block);
        return match.Success ? int.Parse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture) : null;
    }
}
