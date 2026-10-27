namespace DSOptimizer.Core.Abstractions;

public interface ISystemInfo
{
    /// <summary>CurrentBuild, np. 26300.</summary>
    int Build { get; }

    /// <summary>UBR, np. 9278.</summary>
    int Revision { get; }

    string Edition { get; }

    string MachineName { get; }
}
