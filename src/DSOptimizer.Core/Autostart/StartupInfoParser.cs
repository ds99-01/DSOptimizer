using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Proces z pliku pomiaru Windows: pełna ścieżka pliku procesu (<c>Name</c>), linia poleceń, czas procesora (µs) i odczyt z dysku (B)
/// samego procesu, nazwa procesu rodzica i czas startu. Pole nieobecne albo nieczytelne = null.
/// </summary>
public sealed record StartupProcess(string Name, string? CommandLine, long? CpuMicroseconds, long? DiskBytes, string? ParentName, DateTime? StartTime);

/// <summary>Rozbiór pliku <c>StartupInfo</c> (<c>StartupData</c> → <c>Process</c>), zmierzony schemat z VM 26H2.</summary>
public static class StartupInfoParser
{
    private const string Root = "StartupData";
    private const string StartTimeFormat = "yyyy/MM/dd:HH:mm:ss.FFFFFFF";

    /// <summary>
    /// Procesy w kolejności pliku; proces bez <c>Name</c> jest pomijany. DTD zabronione, bez rozwiązywania zasobów zewnętrznych; niepoprawny
    /// XML albo inny element główny niż <c>StartupData</c> = <see cref="XmlException"/>.
    /// </summary>
    public static IReadOnlyList<StartupProcess> Parse(string xml)
    {
        if (xml is null)
        {
            throw new ArgumentNullException(nameof(xml));
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        XDocument document;
        using (var text = new StringReader(xml.TrimStart('﻿')))
        using (var reader = XmlReader.Create(text, settings))
        {
            document = XDocument.Load(reader);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != Root)
        {
            throw new XmlException("Expected root element " + Root + ", found " + (root?.Name.LocalName ?? "none") + ".");
        }

        var processes = new List<StartupProcess>();
        foreach (var element in root.Elements("Process"))
        {
            var name = ((string?)element.Attribute("Name"))?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            processes.Add(new StartupProcess(
                name!,
                element.Element("CommandLine")?.Value,
                NumberOf(element.Element("CpuUsage")),
                NumberOf(element.Element("DiskUsage")),
                element.Element("ParentName")?.Value.Trim(),
                TimeOf(element.Element("StartTime"))));
        }

        return processes;
    }

    private static long? NumberOf(XElement? element)
        => element is not null && long.TryParse(element.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTime? TimeOf(XElement? element)
        => element is not null
           && DateTime.TryParseExact(element.Value.Trim(), StartTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : null;
}
