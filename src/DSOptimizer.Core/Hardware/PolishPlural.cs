namespace DSOptimizer.Core.Hardware;

/// <summary>Wybór formy liczebnikowej: 1 → <c>one</c>, 2–4 (poza 12–14) → <c>few</c>, reszta → <c>many</c>.</summary>
public static class PolishPlural
{
    public static string Choose(long count, string one, string few, string many)
    {
        if (count == 1)
        {
            return one;
        }

        var lastTwo = count % 100;
        var last = count % 10;
        return last is >= 2 and <= 4 && lastTwo is < 12 or > 14 ? few : many;
    }
}
