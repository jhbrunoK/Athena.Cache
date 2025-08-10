/*
using BenchmarkDotNet.Running;
using Athena.Invalidation.Tests.Benchmarks;

namespace Athena.Invalidation.Tests;

/// <summary>
/// 벤치마크 실행 프로그램 - 임시 비활성화
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--benchmark")
        {
            Console.WriteLine("🚀 Starting Athena.Invalidation Phase 3 Performance Benchmarks...");
            var summary = BenchmarkRunner.Run<Phase3PerformanceBenchmarks>();
            
            Console.WriteLine("\n📊 Benchmark Results Summary:");
            Console.WriteLine($"Total benchmarks run: {summary.Reports.Length}");
            Console.WriteLine($"Fastest: {summary.Reports.OrderBy(r => r.ResultStatistics?.Mean).First().BenchmarkCase.Descriptor.DisplayInfo}");
            Console.WriteLine($"Environment: {summary.HostEnvironmentInfo.RuntimeVersion} on {summary.HostEnvironmentInfo.OsVersion}");
        }
        else
        {
            Console.WriteLine("🧪 Athena.Invalidation Phase 3 Test Runner");
            Console.WriteLine("");
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --benchmark     Run performance benchmarks");
            Console.WriteLine("  dotnet test               Run unit and integration tests");
            Console.WriteLine("");
            Console.WriteLine("Phase 3 Features Tested:");
            Console.WriteLine("  ✅ Distributed Event Bus (Redis-based)");
            Console.WriteLine("  ✅ Real-time Metrics Collection");
            Console.WriteLine("  ✅ Background Processing & Batch Optimization");
            Console.WriteLine("  ✅ Circuit Breaker & Retry Patterns");
            Console.WriteLine("  ✅ Health Checks & Monitoring");
            Console.WriteLine("");
            Console.WriteLine("Example benchmark run:");
            Console.WriteLine("  dotnet run --benchmark");
        }
    }
}
*/