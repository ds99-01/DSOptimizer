namespace DSOptimizer.Core.Abstractions;

public sealed class RestorePointResult
{
    public RestorePointResult(RestorePointStatus status, string? message, uint? code = null)
    {
        Status = status;
        Message = message;
        Code = code;
    }

    public RestorePointStatus Status { get; }

    public string? Message { get; }

    /// <summary>
    /// Kod wyniku: 0, gdy CreateRestorePoint zwrócił 0 – zarówno przy <see cref="RestorePointStatus.Created"/>, jak i przy
    /// <see cref="RestorePointStatus.Failed"/>, gdy punkt nie pojawił się na liście (timeout) albo listy nie dało się odczytać
    /// po zwrocie; niezerowy kod API przy niezerowym zwrocie (Failed); 1058 przy <see cref="RestorePointStatus.ProtectionDisabled"/>
    /// (wyprowadzone z wyjątku HRESULT 0x80070422, nie zwrócone przez API); null przy innych wyjątkach, błędzie odczytu
    /// rejestru lub listy punktów przed wywołaniem API. Sam kod 0 nie oznacza sukcesu – decyduje <see cref="Status"/>.
    /// </summary>
    public uint? Code { get; }
}
