using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DSOptimizer.Core.Repair;

/// <summary>Werdykt <c>sfc /scannow</c>.</summary>
public enum SfcVerdict
{
    /// <summary>Kod 0, grupy <c>[SR] Verify complete</c>, bez znaczników naprawy (zmierzone na VM 26H2).</summary>
    NoViolations,

    /// <summary><c>Repairing corrupted file … from store</c> / <c>Repaired file … by copying from backup</c> (KB 928228, niezmierzony).</summary>
    Repaired,

    /// <summary>Ślad naprawy w <c>[SR]</c> i klucz <c>RebootPending</c>, który pojawił się w trakcie SFC (niezmierzony).</summary>
    RepairPendingReboot,

    /// <summary><c>Cannot repair member file</c> (KB 928228, niezmierzony).</summary>
    NotAllRepaired,

    /// <summary>Wyniku nie da się jednoznacznie odczytać (brak segmentu, brak <c>[SR]</c>, kod ≠ 0 bez znaczników).</summary>
    Unknown,
}

/// <summary>
/// Wynik SFC ze znaczników <c>[SR]</c> w segmencie CBS.log z tego przebiegu (angielskie niezależnie od języka systemu; pomiar Task 2,
/// pkt 2r). Kody wyjścia SFC dla naprawy nie są opisane w dokumentacji MS, więc „brak naruszeń” wymaga kodu 0, a pozostałe
/// werdykty opierają się na znacznikach.
/// </summary>
public sealed record SfcOutcome(SfcVerdict Verdict, int ExitCode)
{
    private const string Marker = "[SR]";

    private static readonly Regex RepairingComponents = new(@"\[SR\] Repairing ([0-9]{1,9}) components", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public bool IsSuccess => Verdict is SfcVerdict.NoViolations or SfcVerdict.Repaired or SfcVerdict.RepairPendingReboot;

    /// <param name="rebootPendingAppeared">Klucz <c>…\Component Based Servicing\RebootPending</c> nie istniał przed SFC, a istnieje po nim.</param>
    public static SfcOutcome Classify(int exitCode, string? cbsSegment, bool rebootPendingAppeared)
    {
        var verdict = Decide(exitCode, cbsSegment, rebootPendingAppeared);
        return new SfcOutcome(verdict, exitCode);
    }

    private static SfcVerdict Decide(int exitCode, string? segment, bool rebootPendingAppeared)
    {
        if (string.IsNullOrEmpty(segment) || segment!.IndexOf(Marker, StringComparison.Ordinal) < 0)
        {
            return SfcVerdict.Unknown;
        }

        if (Has(segment, "[SR] Cannot repair member file"))
        {
            return SfcVerdict.NotAllRepaired;
        }

        if (Has(segment, "[SR] Repairing corrupted file") || Has(segment, "[SR] Repaired file"))
        {
            return rebootPendingAppeared ? SfcVerdict.RepairPendingReboot : SfcVerdict.Repaired;
        }

        if (RepairsSomething(segment))
        {
            return rebootPendingAppeared ? SfcVerdict.RepairPendingReboot : SfcVerdict.Unknown;
        }

        return exitCode == 0 && Has(segment, "[SR] Verify complete") ? SfcVerdict.NoViolations : SfcVerdict.Unknown;
    }

    private static bool Has(string segment, string text) => segment.IndexOf(text, StringComparison.Ordinal) >= 0;

    private static bool RepairsSomething(string segment)
    {
        foreach (Match match in RepairingComponents.Matches(segment))
        {
            if (int.Parse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture) > 0)
            {
                return true;
            }
        }

        return false;
    }
}
