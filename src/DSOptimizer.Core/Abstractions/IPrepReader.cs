using System.Collections.Generic;
using DSOptimizer.Core.Prep;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Synchroniczne odczyty strony „Przygotowanie” (tylko odczyt); każdy może się zawiesić, dlatego loader daje im limit czasu.</summary>
public interface IPrepReader
{
    PrepProduct ReadProduct();

    ActivationInfo ReadActivation();

    IReadOnlyList<PrepGpu> ReadGraphics();

    IReadOnlyList<ProblemDevice> ReadProblemDevices();
}
