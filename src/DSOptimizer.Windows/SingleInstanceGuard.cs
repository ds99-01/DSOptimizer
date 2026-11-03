using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace DSOptimizer.Windows;

/// <summary>
/// Blokada jednej instancji programu. Ten sam nazwany mutex dzielą okno, polecenia CLI zmieniające system
/// i instalator (<c>CheckForMutexes</c> w <c>tools/release/DSOptimizer.iss</c>), więc dwie kopie
/// (przenośna i zainstalowana) nigdy nie zapisują tych samych danych naraz. Nazwa jest w przestrzeni
/// <c>Global\</c>, bo dane w <c>%ProgramData%</c> są wspólne dla wszystkich sesji; obiekt w <c>Global\</c>
/// może utworzyć tylko proces z <c>SeCreateGlobalPrivilege</c>, a program zawsze działa jako administrator.
/// Własność mutexa jest per wątek: <see cref="Dispose"/> trzeba wołać na wątku, który wszedł.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string ProgramMutexName = @"Global\DSOptimizer.SingleInstance";

    public const string RelaunchedArgument = "--relaunched";

    public static readonly TimeSpan StartWait = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan RelaunchWait = TimeSpan.FromSeconds(30);

    private Mutex? _mutex;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>Zwraca strażnika z posiadanym mutexem albo <c>null</c> (zajęty po <paramref name="wait"/> lub nazwy nie da się otworzyć).</summary>
    public static SingleInstanceGuard? TryEnter(string name, TimeSpan wait)
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(false, name);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            return null;
        }

        try
        {
            if (mutex.WaitOne(wait))
            {
                return new SingleInstanceGuard(mutex);
            }
        }
        catch (AbandonedMutexException)
        {
            // Poprzedni właściciel zakończył się bez zwolnienia: mutex jest już nasz.
            return new SingleInstanceGuard(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }

        mutex.Dispose();
        return null;
    }

    /// <summary><c>true</c> wyłącznie dla jednego argumentu równego <see cref="RelaunchedArgument"/> (z uwzględnieniem wielkości liter).</summary>
    public static bool IsRelaunch(IReadOnlyList<string> args)
    {
        return args.Count == 1 && string.Equals(args[0], RelaunchedArgument, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        var mutex = _mutex;
        if (mutex is null)
        {
            return;
        }

        _mutex = null;
        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Wołane nie z wątku właściciela: nic do zwolnienia.
        }

        mutex.Dispose();
    }
}
