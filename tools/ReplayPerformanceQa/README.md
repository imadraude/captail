# ReplayPerformanceQa

A repeatable automated performance QA harness for benchmarking Captail Instant Replay and manual recording workloads.

## Scenarios

1. `baseline` — Capture pipeline inactive / idle monitoring.
2. `replay` — Instant Replay buffer active only.
3. `record` — Manual recording active without Instant Replay.
4. `replay-record` — Simultaneous Instant Replay buffer and manual recording.
5. `save-replay` — Measure resource and I/O spikes during Instant Replay clip saves.
6. `advanced-audio-N` — Advanced audio routing across 1, 4, and 10 process routes.

## Test Matrix

| Resolution / FPS | Codec        | NVENC Mode   | Buffer Duration |
| ---------------- | ------------ | ------------ | --------------- |
| 1080p60          | H.264        | Balanced     | 5 min           |
| 1080p60          | H.264        | Low Overhead | 5 min           |
| 1440p120         | H.264 / HEVC | Low Overhead | 5 min           |
| 4K60             | HEVC / AV1   | Low Overhead | 5 min           |
| Source / 240 FPS | Supported    | Low Overhead | 1 min           |

Target Media:

- NVMe PCIe SSD
- SATA SSD
- Slow HDD (when testing backpressure)

## Metrics Collected

- **Captail CPU Time & Process Utilization**: In-process processor time delta (`cpuMs`).
- **Working Set**: Memory footprint delta and resident size (`workingSetMb`).
- **Pipeline Frames**: Rendered frames, lagged frames, and `LaggedFramePercent`.
- **Encoder Frames**: Encoded frame count and drops.
- **I/O Throughput**: Replay and recording bytes written, average write throughput (MB/s).
- **Latency**:
  - Warm Record-to-first-byte: Target `< 250 ms`.
  - Cold Record-to-first-byte: Target `< 1500 ms`.
  - Stop-to-file-ready: Target `< 2000 ms` on SSD.
- **Game Frame Time**: External p95 / p99 and 1% lows captured via PresentMon / ETW.
- **GPU Engine Utilization**: 3D, Copy, and Video Encode engine loads.

## Acceptance Budget

- `replay` vs. `baseline`:
  - p99 frame time: $\le +0.5\text{ ms}$
  - 1% low: $\ge -3\%$
  - Lagged frames: $< 0.1\%$
  - CPU: $< 2\%$ of one logical core at 1080p60
- `replay-record` vs. `replay`:
  - p99 frame time: $\le +0.25\text{ ms}$
  - Additional CPU: $< 1\%$
  - Zero dropped encoded frames
- Stop / Finalization p95: $< 2\text{ s}$ on NVMe/SSD

## Usage

```powershell
# Run all scenarios with default timing (10s warmup, 30s sample, 3 repetitions)
dotnet run --project tools/ReplayPerformanceQa/ReplayPerformanceQa.csproj -c Release

# Run specific scenario in quick smoke-test mode
dotnet run --project tools/ReplayPerformanceQa/ReplayPerformanceQa.csproj -c Release -- --scenario replay-record --quick

# Custom warm-up, sample duration, and repetitions
dotnet run --project tools/ReplayPerformanceQa/ReplayPerformanceQa.csproj -c Release -- --warmup-sec 10 --sample-sec 60 --reps 5
```
