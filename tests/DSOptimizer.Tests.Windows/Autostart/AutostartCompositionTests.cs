using System;
using System.Collections.Generic;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using DSOptimizer.Windows.Autostart;
using Xunit;

namespace DSOptimizer.Tests.Windows.Autostart;

[Trait("Category", "Windows")]
public class AutostartCompositionTests
{
    // Tylko składanie: Read nie jest wołane, więc ani rejestr, ani skrypt AppX, ani pliki pomiaru nie są czytane.
    [Fact]
    public void CreateReader_FromAppComposition_BuildsScanner()
    {
        using var temp = new TempDirectory();
        var composition = AppComposition.Create(temp.AppRoot, null);

        Assert.IsType<AutostartScanner>(AutostartComposition.CreateReader(composition));
    }

    // Wbudowana baza znanych pozycji wczytana tym samym walidatorem co w programie.
    [Fact]
    public void LoadCatalog_LoadsBuiltInEntries()
    {
        var catalog = AutostartComposition.LoadCatalog();

        Assert.Equal(16, catalog.Entries.Count);
        Assert.Contains(catalog.Entries, entry => entry.Verdict == AutostartVerdict.Protected);
    }

    // Usługi, które rzucają przy każdym wywołaniu i liczą wywołania: składanie nie dotyka rejestru, skryptów ani dziennika.
    [Fact]
    public void CreateReader_TouchesNothingUntilRead()
    {
        var calls = new List<string>();

        var reader = AutostartComposition.CreateReader(new TrapRegistry(calls), new TrapRunner(calls), new TrapLog(calls));

        Assert.IsType<AutostartScanner>(reader);
        Assert.Empty(calls);
    }

    private sealed class TrapRegistry : IRegistry
    {
        private readonly List<string> _calls;

        public TrapRegistry(List<string> calls) => _calls = calls;

        public RegistryReadResult Read(string path, string name) => throw Trap(nameof(Read));

        public void Write(string path, string name, RegValue value) => throw Trap(nameof(Write));

        public ReadStatus KeyStatus(string path) => throw Trap(nameof(KeyStatus));

        public IReadOnlyList<string> SubKeyNames(string path) => throw Trap(nameof(SubKeyNames));

        public IReadOnlyList<string> ValueNames(string path) => throw Trap(nameof(ValueNames));

        public bool DeleteKeyIfEmpty(string path) => throw Trap(nameof(DeleteKeyIfEmpty));

        public void DeleteKeyTree(string path) => throw Trap(nameof(DeleteKeyTree));

        private InvalidOperationException Trap(string name)
        {
            _calls.Add("registry." + name);
            return new InvalidOperationException(name);
        }
    }

    private sealed class TrapRunner : IScriptRunner
    {
        private readonly List<string> _calls;

        public TrapRunner(List<string> calls) => _calls = calls;

        public ScriptResult Run(ScriptRequest request, IProgress<string>? output, CancellationToken cancellationToken)
        {
            _calls.Add("scripts.Run");
            throw new InvalidOperationException(nameof(Run));
        }
    }

    private sealed class TrapLog : ILog
    {
        private readonly List<string> _calls;

        public TrapLog(List<string> calls) => _calls = calls;

        public void Info(string component, string message) => _calls.Add("log.Info");

        public void Warn(string component, string message) => _calls.Add("log.Warn");

        public void Fail(string component, string message, Exception? exception = null) => _calls.Add("log.Fail");
    }
}
