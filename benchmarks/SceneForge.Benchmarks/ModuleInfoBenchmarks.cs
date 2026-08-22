using BenchmarkDotNet.Attributes;
using SceneForge.Core;

namespace SceneForge.Benchmarks;

[MemoryDiagnoser]
public class ModuleInfoBenchmarks
{
    private readonly string _moduleName = ModuleInfo.Name;

    [Benchmark]
    public int ReadModuleNameLength() => _moduleName.Length;
}
