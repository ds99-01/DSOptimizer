namespace DSOptimizer.Core.Hardware;

/// <summary>Ustalenie pamięci karty graficznej: <c>AdapterRAM</c> jest 32-bitowe, więc nasyca się na 4 GB.</summary>
public static class GpuMemory
{
    // Sterownik NVIDIA zgłasza 0xFFF00000 dla kart > 4 GB (rzeczywiste odczyty z hosta), nie tylko uint.MaxValue.
    private const uint SaturatedAdapterRam = 0xFFF00000;

    public static ulong? Resolve(ulong? qwMemorySize, uint? adapterRam)
    {
        if (qwMemorySize is > 0)
        {
            return qwMemorySize;
        }

        return adapterRam is > 0 and < SaturatedAdapterRam ? adapterRam : null;
    }
}
