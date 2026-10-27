using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace DSOptimizer.Core.Cli;

/// <summary>Serializuje raport CLI do stabilnego JSON-a (camelCase, enumy jako nazwy C#, null jawnie, daty ISO 8601).</summary>
public static class CliReportSerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = { new StringEnumConverter() },
        NullValueHandling = NullValueHandling.Include,
        Formatting = Formatting.Indented,
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        Culture = CultureInfo.InvariantCulture,
    };

    public static string Serialize(CliReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        return JsonConvert.SerializeObject(report, Settings);
    }
}
