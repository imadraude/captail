using System.Diagnostics;
using System.Globalization;
using Captail;

namespace ReplayPerformanceQa;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== Captail Replay Performance QA Harness ===");

        string targetScenario = "all";
        int warmupSeconds = 10;
        int sampleSeconds = 30;
        int repetitions = 3;
        bool quickMode = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--scenario", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                targetScenario = args[++i].ToLowerInvariant();
            else if (string.Equals(args[i], "--warmup-sec", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                int.TryParse(args[++i], out warmupSeconds);
            else if (string.Equals(args[i], "--sample-sec", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                int.TryParse(args[++i], out sampleSeconds);
            else if (string.Equals(args[i], "--reps", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                int.TryParse(args[++i], out repetitions);
            else if (string.Equals(args[i], "--quick", StringComparison.OrdinalIgnoreCase))
                quickMode = true;
        }

        if (quickMode)
        {
            warmupSeconds = 1;
            sampleSeconds = 3;
            repetitions = 1;
            Console.WriteLine("[Quick Mode] warmup=1s, sample=3s, reps=1");
        }
        else
        {
            Console.WriteLine($"[Settings] warmup={warmupSeconds}s, sample={sampleSeconds}s, repetitions={repetitions}, scenario={targetScenario}");
        }

        var scenarios = new List<string>();
        if (targetScenario is "all" or "baseline") scenarios.Add("baseline");
        if (targetScenario is "all" or "replay") scenarios.Add("replay");
        if (targetScenario is "all" or "record") scenarios.Add("record");
        if (targetScenario is "all" or "replay-record") scenarios.Add("replay-record");
        if (targetScenario is "all" or "save-replay") scenarios.Add("save-replay");
        if (targetScenario is "all" or "advanced-audio-1") scenarios.Add("advanced-audio-1");
        if (targetScenario is "all" or "advanced-audio-4") scenarios.Add("advanced-audio-4");
        if (targetScenario is "all" or "advanced-audio-10") scenarios.Add("advanced-audio-10");

        var allResults = new Dictionary<string, List<SampleMetrics>>();

        foreach (string scenario in scenarios)
        {
            Console.WriteLine($"\n------------------------------------------------------------");
            Console.WriteLine($"Running Scenario: {scenario}");
            Console.WriteLine($"------------------------------------------------------------");

            var samples = new List<SampleMetrics>();
            for (int rep = 1; rep <= repetitions; rep++)
            {
                Console.WriteLine($"  Repetition {rep}/{repetitions} starting (warmup: {warmupSeconds}s, sample: {sampleSeconds}s)...");
                SampleMetrics metric = await RunScenarioSampleAsync(scenario, warmupSeconds, sampleSeconds);
                samples.Add(metric);

                string perfLog = metric.Delta.ToPerfLogString(scenario, metric.Delta.WorkingSetDeltaBytes / (1024.0 * 1024.0));
                Console.WriteLine($"  {perfLog}");
                if (metric.RecordToFirstByteMs > 0)
                    Console.WriteLine($"    Record-to-first-byte: {metric.RecordToFirstByteMs:F1} ms");
                if (metric.StopToFileReadyMs > 0)
                    Console.WriteLine($"    Stop-to-file-ready: {metric.StopToFileReadyMs:F1} ms");
                if (metric.WriteThroughputMbSec > 0)
                    Console.WriteLine($"    Avg write throughput: {metric.WriteThroughputMbSec:F2} MB/s");
            }
            allResults[scenario] = samples;
        }

        PrintSummaryTable(allResults);
        ValidateAcceptanceBudget(allResults);

        return 0;
    }

    private static async Task<SampleMetrics> RunScenarioSampleAsync(string scenario, int warmupSec, int sampleSec)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Captail_PerfQa_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            if (scenario == "baseline")
            {
                await Task.Delay(TimeSpan.FromSeconds(warmupSec));
                using var proc = Process.GetCurrentProcess();
                proc.Refresh();
                DateTime startT = DateTime.UtcNow;
                TimeSpan startCpuT = proc.TotalProcessorTime;
                long startWsT = proc.WorkingSet64;

                await Task.Delay(TimeSpan.FromSeconds(sampleSec));
                proc.Refresh();
                DateTime endT = DateTime.UtcNow;
                TimeSpan endCpuT = proc.TotalProcessorTime;
                long endWsT = proc.WorkingSet64;

                var startSnap = new ReplayPerformanceSnapshot(startT, 0, 0, 0, 0, 0, startWsT, startCpuT, false, false);
                var endSnap = new ReplayPerformanceSnapshot(endT, 0, 0, 0, 0, 0, endWsT, endCpuT, false, false);
                return new SampleMetrics(ReplayPerformanceDelta.Calculate(startSnap, endSnap), 0, 0, 0);
            }

            var config = new Config
            {
                CaptureSource = "desktop",
                OutputDirectory = tempDir,
                BufferSeconds = 60,
                FrameRate = 60,
                BitrateMbps = 10,
                NvencMode = NvencModes.LowOverhead,
                Codec = "h264",
                ReplayEnabled = scenario is "replay" or "replay-record" or "save-replay",
                SuspendReplayDuringRecording = scenario != "replay-record",
            };

            if (scenario.StartsWith("advanced-audio-"))
            {
                int routeCount = scenario switch
                {
                    "advanced-audio-1" => 1,
                    "advanced-audio-4" => 4,
                    "advanced-audio-10" => 10,
                    _ => 1,
                };
                config.AudioRoutingMode = "advanced";
                config.ProcessAudioRoutes = Enumerable.Range(1, routeCount)
                    .Select(idx => new ProcessAudioRoute { Executable = $"process_{idx}.exe", Track = 2, Enabled = true })
                    .ToList();
            }

            using var scheduler = new SingleThreadTaskScheduler("PerfQaScheduler");
            Task<T> RunOnObs<T>(Func<T> action) =>
                Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, scheduler);
            Task RunOnObsAction(Action action) =>
                Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, scheduler);

            ObsReplayEngine? engine = null;
            double recordToFirstByte = 0;
            double stopToFileReady = 0;

            try
            {
                engine = await RunOnObs(() =>
                {
                    var eng = new ObsReplayEngine(config);
                    eng.Start();
                    return eng;
                });

                if (scenario.Contains("record"))
                {
                    var recordSw = Stopwatch.StartNew();
                    await RunOnObs(() => engine.StartRecordingAsync()).Unwrap();
                    // Measure time until first packet or frames registered
                    for (int attempt = 0; attempt < 50 && engine.RecordingOutputBytes == 0; attempt++)
                    {
                        await Task.Delay(10);
                    }
                    recordSw.Stop();
                    recordToFirstByte = recordSw.Elapsed.TotalMilliseconds;
                }

                // Warm-up phase
                await Task.Delay(TimeSpan.FromSeconds(warmupSec));

                // Take start snapshot
                ReplayPerformanceSnapshot startSnapshot = await RunOnObs(() => engine.CapturePerformanceSnapshot());

                // Run active sample
                if (scenario == "save-replay")
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, sampleSec / 2)));
                    var saveOp = await RunOnObs(() => engine.BeginSaveReplay());
                    await saveOp.Completion;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, sampleSec / 2)));
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(sampleSec));
                }

                // Take end snapshot
                ReplayPerformanceSnapshot endSnapshot = await RunOnObs(() => engine.CapturePerformanceSnapshot());

                if (scenario.Contains("record"))
                {
                    var stopSw = Stopwatch.StartNew();
                    await engine.StopRecordingAsync();
                    stopSw.Stop();
                    stopToFileReady = stopSw.Elapsed.TotalMilliseconds;
                }

                ReplayPerformanceDelta delta = ReplayPerformanceDelta.Calculate(startSnapshot, endSnapshot);
                double writeThroughput = delta.Duration.TotalSeconds > 0
                    ? ((delta.ReplayBytes + delta.RecordingBytes) / (1024.0 * 1024.0)) / delta.Duration.TotalSeconds
                    : 0;

                return new SampleMetrics(delta, recordToFirstByte, stopToFileReady, writeThroughput);
            }
            finally
            {
                if (engine is not null)
                {
                    await RunOnObsAction(engine.Dispose);
                }
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    private static void PrintSummaryTable(Dictionary<string, List<SampleMetrics>> results)
    {
        Console.WriteLine("\n==========================================================================================");
        Console.WriteLine("                                PERFORMANCE SUMMARY TABLE                                 ");
        Console.WriteLine("==========================================================================================");
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0,-18} | {1,10} | {2,8} | {3,8} | {4,10} | {5,12} | {6,12}",
            "Scenario", "Duration", "Rendered", "Lagged", "Lagged %", "CPU ms", "RAM Delta MB"));
        Console.WriteLine("------------------------------------------------------------------------------------------");

        foreach (var (scenario, samples) in results)
        {
            double avgDurationMs = samples.Average(s => s.Delta.Duration.TotalMilliseconds);
            double avgRendered = samples.Average(s => s.Delta.RenderedFrames);
            double avgLagged = samples.Average(s => s.Delta.LaggedFrames);
            double avgLaggedPct = samples.Average(s => s.Delta.LaggedFramePercent);
            double avgCpuMs = samples.Average(s => s.Delta.CpuTime.TotalMilliseconds);
            double avgRamMb = samples.Average(s => s.Delta.WorkingSetDeltaBytes / (1024.0 * 1024.0));

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-18} | {1,10:F0} | {2,8:F0} | {3,8:F1} | {4,9:F3}% | {5,10:F1} | {6,12:F2}",
                scenario, avgDurationMs, avgRendered, avgLagged, avgLaggedPct, avgCpuMs, avgRamMb));
        }
        Console.WriteLine("==========================================================================================\n");
    }

    private static void ValidateAcceptanceBudget(Dictionary<string, List<SampleMetrics>> results)
    {
        Console.WriteLine("=== Acceptance Budget Verification ===");
        bool allPassed = true;

        if (results.TryGetValue("replay", out var replaySamples))
        {
            double avgLaggedPct = replaySamples.Average(s => s.Delta.LaggedFramePercent);
            bool laggedOk = avgLaggedPct < 0.1;
            Console.WriteLine($"  [replay] Lagged frames < 0.1%: {(laggedOk ? "PASS" : "FAIL")} ({avgLaggedPct:F3}%)");
            if (!laggedOk) allPassed = false;
        }

        if (results.TryGetValue("record", out var recordSamples))
        {
            double avgRecordFirstByte = recordSamples.Average(s => s.RecordToFirstByteMs);
            double avgStopReady = recordSamples.Average(s => s.StopToFileReadyMs);

            bool recordFirstByteOk = avgRecordFirstByte < 250.0;
            Console.WriteLine($"  [record] Warm Record-to-first-byte < 250ms: {(recordFirstByteOk ? "PASS" : "FAIL")} ({avgRecordFirstByte:F1} ms)");
            if (!recordFirstByteOk) allPassed = false;

            bool stopReadyOk = avgStopReady < 2000.0;
            Console.WriteLine($"  [record] Stop-to-file-ready < 2000ms: {(stopReadyOk ? "PASS" : "FAIL")} ({avgStopReady:F1} ms)");
            if (!stopReadyOk) allPassed = false;
        }

        if (results.TryGetValue("replay-record", out var rrSamples))
        {
            double avgLaggedPct = rrSamples.Average(s => s.Delta.LaggedFramePercent);
            bool dropOk = avgLaggedPct < 0.1;
            Console.WriteLine($"  [replay-record] Zero / near-zero frame drops: {(dropOk ? "PASS" : "FAIL")} ({avgLaggedPct:F3}%)");
            if (!dropOk) allPassed = false;
        }

        Console.WriteLine(allPassed ? ">> ALL BUDGET CHECKS PASSED <<" : ">> SOME BUDGET CHECKS FAILED <<");
    }

    private sealed record SampleMetrics(
        ReplayPerformanceDelta Delta,
        double RecordToFirstByteMs,
        double StopToFileReadyMs,
        double WriteThroughputMbSec);
}
