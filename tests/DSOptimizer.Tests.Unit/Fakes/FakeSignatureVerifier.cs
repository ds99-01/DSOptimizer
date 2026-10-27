using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Weryfikator podpisu w pamięci: zwraca <see cref="Result"/> albo rzuca <see cref="Throws"/>; zapisuje ścieżki.</summary>
public sealed class FakeSignatureVerifier : ISignatureVerifier
{
    /// <summary>Podmiot certyfikatu RustDesk z pomiaru VM (spike 2026-10-24).</summary>
    public const string PurslaneSubject =
        "CN=PURSLANE, O=PURSLANE, S=North West, C=SG, OID.2.5.4.15=Business Entity, OID.1.3.6.1.4.1.311.60.2.1.3=SG, SERIALNUMBER=53481265A";

    public List<string> Calls { get; } = new();

    public List<string>? Trace { get; set; }

    public SignatureCheck Result { get; set; } = new(SignatureStatus.Valid, PurslaneSubject, null);

    public Exception? Throws { get; set; }

    public SignatureCheck Verify(string filePath)
    {
        Calls.Add(filePath);
        Trace?.Add("Verify " + filePath);
        if (Throws is not null)
        {
            throw Throws;
        }

        return Result;
    }
}
