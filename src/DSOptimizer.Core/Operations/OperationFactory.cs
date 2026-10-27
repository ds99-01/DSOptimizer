using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Operations;

/// <summary>Buduje operacje tweaka: rejestr → usługi → skrypt. HKCU mapuje na HKU\&lt;SID zalogowanego&gt;.</summary>
public sealed class OperationFactory
{
    private readonly IRegistry _registry;
    private readonly IServiceManager _services;
    private readonly IScriptRunner _scripts;
    private readonly IUserContext _user;

    public OperationFactory(IRegistry registry, IServiceManager services, IScriptRunner scripts, IUserContext user)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
        _user = user ?? throw new ArgumentNullException(nameof(user));
    }

    public IReadOnlyList<IOperation> Create(TweakDefinition tweak, IProgress<string>? output = null)
    {
        if (tweak is null)
        {
            throw new ArgumentNullException(nameof(tweak));
        }

        var operations = new List<IOperation>();
        foreach (var entry in tweak.Registry)
        {
            operations.Add(new RegistryValueOperation(entry with { Path = MapPath(entry.Path) }, _registry));
        }

        foreach (var service in tweak.Services)
        {
            operations.Add(new ServiceStartupOperation(service, _services, ServiceStartupOperation.DefaultServiceTimeout));
        }

        if (tweak.Script is { } script)
        {
            operations.Add(new ScriptOperation(script, _scripts, _user.Sid, tweak.Timeout, output));
        }

        return operations;
    }

    public string MapPath(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        return path.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase)
            ? @"HKU\" + _user.Sid + path.Substring(4)
            : path;
    }
}
