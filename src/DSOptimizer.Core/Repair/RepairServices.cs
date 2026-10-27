using System;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Core.Repair;

/// <summary>Zależności handlerów napraw (składane w AppComposition z prawdziwych adapterów, w testach z fake'ów).</summary>
public sealed class RepairServices
{
    public required IServiceManager Services { get; init; }

    public required IRegistry Registry { get; init; }

    public required IToolRunner Tools { get; init; }

    /// <summary>Długie narzędzia serwisowania (DISM, SFC, cleanmgr): poza Job Objectem, bez zabijania po limicie.</summary>
    public required IServicingToolRunner LongTools { get; init; }

    public required IDiskSpace DiskSpace { get; init; }

    /// <summary>Trwająca operacja serwisowania (strażnik drugiej naprawy w trakcie).</summary>
    public required IServicingActivity Servicing { get; init; }

    public required IScriptRunner Scripts { get; init; }

    /// <summary>Zalogowany użytkownik (kroki konta, np. jego <c>%TEMP%</c>).</summary>
    public required IUserContext User { get; init; }

    public required IRepairFileSystem Files { get; init; }

    public required IDomainInfo Domain { get; init; }

    public required IClock Clock { get; init; }

    public required ILog Log { get; init; }

    public required RepairPaths Paths { get; init; }

    /// <summary>Strefa czasu dla znaczników w nazwach kopii (czas lokalny); testy podają stałą strefę.</summary>
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Local;
}
