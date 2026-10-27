namespace DSOptimizer.Core.Abstractions;

/// <summary>Proces <c>explorer.exe</c>: sesja, SID konta, które go uruchomiło, i linia poleceń (null = nieznana).</summary>
public sealed record ExplorerProcessInfo(int SessionId, string OwnerSid, string? CommandLine);
