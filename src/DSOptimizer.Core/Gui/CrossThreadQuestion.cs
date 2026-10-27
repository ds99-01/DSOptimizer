using System;
using System.Threading;
using System.Threading.Tasks;

namespace DSOptimizer.Core.Gui;

/// <summary>
/// Pytanie zadawane z wątku silnika (np. pod blokadą <see cref="GuiSession"/>) oknu na wątku UI: <c>post</c> przekazuje pracę na
/// wątek UI, wątek silnika czeka na odpowiedź najwyżej <c>timeout</c>. Limit, anulowanie (zamykanie programu), wyjątek przy
/// przekazaniu albo w oknie = „Nie” (false) – wątek silnika nigdy nie wisi z blokadą.
/// </summary>
public static class CrossThreadQuestion
{
    /// <summary>Domyślny limit czekania na odpowiedź użytkownika.</summary>
    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromMinutes(10);

    public static bool Ask(Action<Action> post, Func<Task<bool>> question, TimeSpan timeout, CancellationToken cancel)
    {
        if (post is null)
        {
            throw new ArgumentNullException(nameof(post));
        }

        if (question is null)
        {
            throw new ArgumentNullException(nameof(question));
        }

        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            post(() =>
            {
                try
                {
                    question().ContinueWith(
                        task => answer.TrySetResult(task.Status == TaskStatus.RanToCompletion && task.Result),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
                catch (Exception)
                {
                    answer.TrySetResult(false);
                }
            });
        }
        catch (Exception)
        {
            return false;
        }

        try
        {
            var milliseconds = (int)Math.Max(0, Math.Min(timeout.TotalMilliseconds, int.MaxValue));
            return answer.Task.Wait(milliseconds, cancel) && answer.Task.Result;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
