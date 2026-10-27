using System;
using System.Collections.Generic;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Catalog;

public sealed class LoadedCatalog
{
    public LoadedCatalog(IReadOnlyList<TweakDefinition> definitions, string hash)
    {
        Definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        Hash = hash ?? throw new ArgumentNullException(nameof(hash));
    }

    public IReadOnlyList<TweakDefinition> Definitions { get; }

    /// <summary>"sha256:&lt;hex&gt;" — zapisywany w journalu przy każdej zmianie.</summary>
    public string Hash { get; }
}
