using System.Collections.Generic;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

public sealed record GuiCategory(string Id, string Name, IReadOnlyList<TweakDefinition> Items);