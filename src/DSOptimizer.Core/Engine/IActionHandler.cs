using System;
using System.Threading;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Engine;

/// <summary>Akcja katalogu napisana w C# (pole <c>handler</c>) zamiast skryptu albo <c>launch</c>.</summary>
public interface IActionHandler
{
    string Name { get; }

    // progress: postęp długiej akcji (krótkie handlery ignorują). cancellationToken przy długich handlerach = „Zatrzymaj”:
    // sprawdzany tylko między krokami, nigdy nie trafia do IServicingToolRunner.
    ActionHandlerResult Run(TweakDefinition tweak, IProgress<string>? output, IProgress<ActionProgress>? progress, CancellationToken cancellationToken);
}
