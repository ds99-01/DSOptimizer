using System;
using System.Diagnostics;
using System.Threading;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Blokada jednej instancji. Drugi „proces” to osobny wątek, bo własność mutexa jest per wątek; żadnego nowego procesu.</summary>
[Trait("Category", "Windows")]
public class SingleInstanceGuardTests
{
    private static string NewLocalName() => @"Local\DSOptimizer.Tests." + Guid.NewGuid().ToString("N");

    /// <summary>Wątek wchodzi do mutexa i trzyma go do sygnału; zwraca po potwierdzeniu wejścia.</summary>
    private static Thread StartHolder(string name, ManualResetEventSlim release, bool disposeOnRelease = true)
    {
        using var entered = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            var guard = SingleInstanceGuard.TryEnter(name, TimeSpan.FromSeconds(5));
            Assert.NotNull(guard);
            entered.Set();
            release.Wait();
            if (disposeOnRelease)
            {
                guard!.Dispose();
            }
        });
        thread.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        return thread;
    }

    /// <summary>Wykonuje akcję na osobnym wątku (właściciel mutexa musi go zwolnić na tym samym wątku).</summary>
    private static void OnOwnThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (error is not null)
        {
            throw new InvalidOperationException("Wątek testowy zgłosił wyjątek.", error);
        }
    }

    [Fact]
    public void TryEnter_FreeName_ReturnsGuard()
    {
        var name = NewLocalName();
        OnOwnThread(() =>
        {
            using var guard = SingleInstanceGuard.TryEnter(name, TimeSpan.FromMilliseconds(200));
            Assert.NotNull(guard);
        });
    }

    [Fact]
    public void TryEnter_HeldByOtherThread_ReturnsNullAfterWait()
    {
        var name = NewLocalName();
        using var release = new ManualResetEventSlim(false);
        var holder = StartHolder(name, release);
        try
        {
            OnOwnThread(() =>
            {
                var watch = Stopwatch.StartNew();
                var guard = SingleInstanceGuard.TryEnter(name, TimeSpan.FromMilliseconds(200));
                watch.Stop();
                Assert.Null(guard);
                Assert.True(watch.ElapsedMilliseconds >= 150, $"Czekanie trwało {watch.ElapsedMilliseconds} ms.");
            });
        }
        finally
        {
            release.Set();
            holder.Join();
        }
    }

    [Fact]
    public void TryEnter_ReleasedDuringWait_ReturnsGuard()
    {
        var name = NewLocalName();
        using var release = new ManualResetEventSlim(false);
        var holder = StartHolder(name, release);
        var releaser = new Thread(() =>
        {
            Thread.Sleep(100);
            release.Set();
        });
        releaser.Start();
        try
        {
            OnOwnThread(() =>
            {
                using var guard = SingleInstanceGuard.TryEnter(name, TimeSpan.FromSeconds(5));
                Assert.NotNull(guard);
            });
        }
        finally
        {
            release.Set();
            releaser.Join();
            holder.Join();
        }
    }

    [Fact]
    public void TryEnter_OwnerThreadEndedWithoutRelease_ReturnsGuard()
    {
        var name = NewLocalName();
        using var release = new ManualResetEventSlim(false);
        var holder = StartHolder(name, release, disposeOnRelease: false);
        release.Set();
        holder.Join();

        OnOwnThread(() =>
        {
            using var guard = SingleInstanceGuard.TryEnter(name, TimeSpan.FromSeconds(2));
            Assert.NotNull(guard);
        });
    }

    [Fact]
    public void Dispose_LetsNextEnterSucceed_AndSecondDisposeIsNoOp()
    {
        var name = NewLocalName();
        OnOwnThread(() =>
        {
            var first = SingleInstanceGuard.TryEnter(name, TimeSpan.FromMilliseconds(200));
            Assert.NotNull(first);
            first!.Dispose();
            first.Dispose();

            using var second = SingleInstanceGuard.TryEnter(name, TimeSpan.FromMilliseconds(200));
            Assert.NotNull(second);
        });
    }

    [Fact]
    public void TryEnter_NameTakenByEvent_ReturnsNull()
    {
        var name = NewLocalName();
        using var other = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        OnOwnThread(() =>
        {
            Assert.Null(SingleInstanceGuard.TryEnter(name, TimeSpan.FromMilliseconds(200)));
        });
    }

    [Fact]
    public void TryEnter_GlobalName_WorksInElevatedSession()
    {
        var name = @"Global\DSOptimizer.Tests." + Guid.NewGuid().ToString("N");
        OnOwnThread(() =>
        {
            using var guard = SingleInstanceGuard.TryEnter(name, TimeSpan.FromMilliseconds(200));
            Assert.NotNull(guard);
        });
    }

    [Fact]
    public void ProgramMutexName_IsTheOneTheInstallerChecks()
    {
        Assert.Equal(@"Global\DSOptimizer.SingleInstance", SingleInstanceGuard.ProgramMutexName);
    }

    [Fact]
    public void Waits_HaveAgreedLengths()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), SingleInstanceGuard.StartWait);
        Assert.Equal(TimeSpan.FromSeconds(30), SingleInstanceGuard.RelaunchWait);
        Assert.Equal("--relaunched", SingleInstanceGuard.RelaunchedArgument);
    }

    [Theory]
    [InlineData(new[] { "--relaunched" }, true)]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--relaunched", "--detect" }, false)]
    [InlineData(new[] { "--RELAUNCHED" }, false)]
    [InlineData(new[] { "--detect" }, false)]
    public void IsRelaunch_OnlyForTheSingleExactArgument(string[] args, bool expected)
    {
        Assert.Equal(expected, SingleInstanceGuard.IsRelaunch(args));
    }
}
