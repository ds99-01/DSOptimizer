using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.RestorePoints;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>
/// Zapamiętuje wartość SystemRestorePointCreationFrequency w chwili wywołania i symuluje listę punktów:
/// po udanym Create nowy punkt pojawia się na liście przy <see cref="AppearsOnReadNumber"/>-tym odczycie po wywołaniu
/// (null = nigdy).
/// </summary>
public sealed class FakeRestorePointApi : IRestorePointApi
{
    private readonly FakeRegistry _registry;
    private uint _nextSequence = 100;
    private bool _created;
    private int _readsSinceCreate;

    public FakeRestorePointApi(FakeRegistry registry) => _registry = registry;

    public uint ReturnCode { get; set; }

    public Exception? ThrowOnCall { get; set; }

    /// <summary>Wyjątek rzucany przy odczycie listy o numerze <see cref="ThrowOnReadNumber"/> (domyślnie przy każdym).</summary>
    public Exception? ThrowOnRead { get; set; }

    /// <summary>Numer odczytu (liczony od 1 od początku), przy którym rzucamy; null = każdy odczyt.</summary>
    public int? ThrowOnReadNumber { get; set; }

    /// <summary>1 = punkt widoczny już przy pierwszym odczycie po Create; null = punkt nigdy się nie pojawia.</summary>
    public int? AppearsOnReadNumber { get; set; } = 1;

    /// <summary>Opis, z jakim punkt pojawia się na liście (null = opis przekazany do Create).</summary>
    public string? ListedDescription { get; set; }

    public List<RestorePointInfo> Points { get; } = new() { new RestorePointInfo(1, "Stary punkt") };

    public List<RegValue> FrequencyAtCall { get; } = new();

    public int ReadCount { get; private set; }

    public int CreateCalls { get; private set; }

    public uint CreateRestorePoint(string description)
    {
        CreateCalls++;
        FrequencyAtCall.Add(_registry.Get(RestorePointService.FrequencyKeyPath, RestorePointService.FrequencyValueName));
        if (ThrowOnCall is not null)
        {
            throw ThrowOnCall;
        }

        if (ReturnCode == 0)
        {
            _created = true;
            _readsSinceCreate = 0;
            _pendingDescription = ListedDescription ?? description;
        }

        return ReturnCode;
    }

    private string _pendingDescription = string.Empty;

    public IReadOnlyList<RestorePointInfo> GetRestorePoints()
    {
        ReadCount++;
        if (ThrowOnRead is not null && (ThrowOnReadNumber is null || ThrowOnReadNumber == ReadCount))
        {
            throw ThrowOnRead;
        }

        if (_created)
        {
            _readsSinceCreate++;
            if (AppearsOnReadNumber is { } k && _readsSinceCreate >= k)
            {
                _created = false;
                Points.Add(new RestorePointInfo(_nextSequence++, _pendingDescription));
            }
        }

        return Points.ToArray();
    }
}
