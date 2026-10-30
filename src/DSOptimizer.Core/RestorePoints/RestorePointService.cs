using System;
using System.Collections.Generic;
using System.Globalization;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.RestorePoints;

/// <summary>
/// Tworzy punkt przywracania, na czas wywołania wyłączając 24-godzinny limit
/// (SystemRestorePointCreationFrequency = 0) i przywracając poprzednią wartość.
/// </summary>
public sealed class RestorePointService : IRestorePointService
{
    public const string FrequencyKeyPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    public const string FrequencyValueName = "SystemRestorePointCreationFrequency";

    /// <summary>Co ile odpytujemy listę punktów po zwrocie 0 z CreateRestorePoint.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Jak długo czekamy, aż nowy punkt pojawi się na liście.</summary>
    public static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);

    private const int ServiceDisabledHResult = unchecked((int)0x80070422);
    private const uint ServiceDisabledCode = 1058;
    private const string Component = "RestorePoint";
    private static string ProtectionDisabledMessage => L.T("Ochrona systemu jest wyłączona (HRESULT 0x80070422, błąd 1058).");

    private readonly IRegistry _registry;
    private readonly IRestorePointApi _api;
    private readonly ILog _log;
    private readonly IClock _clock;
    private readonly Action<TimeSpan> _sleep;

    public RestorePointService(IRegistry registry, IRestorePointApi api, ILog log, IClock clock, Action<TimeSpan>? sleep = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _sleep = sleep ?? System.Threading.Thread.Sleep;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public RestorePointResult Create(string description)
    {
        var previous = _registry.Read(FrequencyKeyPath, FrequencyValueName);
        if (previous.Status is not (ReadStatus.Found or ReadStatus.NotFound))
        {
            return new RestorePointResult(RestorePointStatus.Failed, L.F("Nie można odczytać {0}: {1}", FrequencyValueName, previous.Error));
        }

        HashSet<uint> existing;
        try
        {
            existing = SequenceNumbers();
        }
        catch (Exception ex)
        {
            _log.Fail(Component, L.T("Nie można odczytać listy punktów przywracania."), ex);
            return new RestorePointResult(
                RestorePointStatus.Failed,
                L.F("Nie można odczytać listy punktów przywracania, więc nie da się potwierdzić wyniku: {0}", Describe(ex)));
        }

        try
        {
            _registry.Write(FrequencyKeyPath, FrequencyValueName, RegValue.FromDWord(0));
            var code = _api.CreateRestorePoint(description);
            if (code != 0)
            {
                return new RestorePointResult(
                    RestorePointStatus.Failed,
                    L.F("SystemRestore.CreateRestorePoint zwrócił kod {0} – ochrona systemu może być wyłączona.", code),
                    code);
            }

            return ConfirmCreated(description, existing);
        }
        catch (Exception ex) when (ex.HResult == ServiceDisabledHResult)
        {
            // Ostrzeżenie = dokładnie komunikat wyniku: okno rozpoznaje je w raporcie operacji i nie dubluje; szczegóły tylko w pliku.
            _log.Warn(Component, ProtectionDisabledMessage);
            _log.Info(Component, L.F("Szczegóły błędu 0x80070422: {0}", Describe(ex)));
            return new RestorePointResult(RestorePointStatus.ProtectionDisabled, ProtectionDisabledMessage, ServiceDisabledCode);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, L.T("Tworzenie punktu przywracania nie powiodło się."), ex);
            return new RestorePointResult(RestorePointStatus.Failed, L.F("Usługa punktów przywracania zgłosiła błąd: {0}", Describe(ex)));
        }
        finally
        {
            try
            {
                _registry.Write(FrequencyKeyPath, FrequencyValueName, previous.Value);
            }
            catch (OperationException ex)
            {
                _log.Warn(Component, L.F("Nie udało się przywrócić {0}: {1}", FrequencyValueName, ex.Message));
            }
        }
    }

    /// <summary>
    /// CreateRestorePoint potrafi zwrócić 0, choć Windows pominął punkt (np. limit 24 h), więc sukces = nowy punkt na liście.
    /// </summary>
    private RestorePointResult ConfirmCreated(string description, HashSet<uint> existing)
    {
        var deadline = _clock.UtcNow + PollTimeout;
        while (true)
        {
            IReadOnlyList<RestorePointInfo> points;
            try
            {
                points = _api.GetRestorePoints();
            }
            catch (Exception ex)
            {
                _log.Fail(Component, L.T("Nie można odczytać listy punktów przywracania po utworzeniu."), ex);
                return new RestorePointResult(
                    RestorePointStatus.Failed,
                    L.F("Nie można potwierdzić wyniku (błąd odczytu listy punktów przywracania): {0}", Describe(ex)),
                    0);
            }

            foreach (var point in points)
            {
                if (!existing.Contains(point.SequenceNumber) && string.Equals(point.Description, description, StringComparison.Ordinal))
                {
                    _log.Info(Component, L.F("Utworzono punkt przywracania: {0} (numer sekwencji {1})", description, point.SequenceNumber));
                    return new RestorePointResult(RestorePointStatus.Created, null, 0);
                }
            }

            if (_clock.UtcNow >= deadline)
            {
                var message = L.F("SystemRestore.CreateRestorePoint zwrócił 0, ale punkt nie pojawił się na liście w ciągu {0} s (możliwy 24-godzinny limit lub pominięcie przez usługę).", (int)PollTimeout.TotalSeconds);
                _log.Warn(Component, message);
                return new RestorePointResult(RestorePointStatus.Failed, message, 0);
            }

            _sleep(PollInterval);
        }
    }

    private HashSet<uint> SequenceNumbers()
    {
        var set = new HashSet<uint>();
        foreach (var point in _api.GetRestorePoints())
        {
            set.Add(point.SequenceNumber);
        }

        return set;
    }

    /// <summary>Wyjątki WMI/COM bywają z pustym Message, więc dodajemy typ i HRESULT.</summary>
    private static string Describe(Exception ex)
        => string.IsNullOrWhiteSpace(ex.Message)
            ? string.Format(CultureInfo.InvariantCulture, "{0} (HRESULT 0x{1:X8})", ex.GetType().Name, ex.HResult)
            : ex.Message;
}
