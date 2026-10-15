using System.Threading.Tasks.Dataflow;
using BenchDouble.Oscilloscopes;

namespace BenchDouble.Oscilloscopes;

/// <summary>
/// Bounded, hardware-free waveform processing pipeline. Acquisitions are completed
/// before samples enter the pipeline; no SCPI session is held while consumers run.
/// </summary>
public static class WaveformProcessingPipeline
{
    /// <summary>Calculate RMS voltage from immutable waveform captures with bounded parallel processing.</summary>
    /// <param name="captures">Completed waveform captures; caller owns acquisition.</param>
    /// <param name="capacity">Maximum buffered work items per stage.</param>
    /// <param name="parallelism">Maximum parallel RMS computations.</param>
    /// <param name="cancellationToken">Cancellation of pending work.</param>
    /// <returns>Results in capture submission order.</returns>
    public static async Task<IReadOnlyList<WaveformRmsResult>> CalculateRmsAsync(
        IEnumerable<WaveformCapture> captures, int capacity = 8, int parallelism = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captures);
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (parallelism < 1) throw new ArgumentOutOfRangeException(nameof(parallelism));
        var transform = new TransformBlock<WaveformCapture, WaveformRmsResult>(
            capture =>
            {
                if (capture.SampleCount == 0) throw new InvalidDataException("Cannot calculate RMS of empty waveform.");
                double sum = 0;
                for (int i = 0; i < capture.SampleCount; i++)
                {
                    double v = capture.VoltageAt(i).Volts;
                    sum += v * v;
                }
                return new WaveformRmsResult(capture.AcquisitionId, capture.Channel,
                    Math.Sqrt(sum / capture.SampleCount), capture.SampleCount);
            },
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = capacity,
                MaxDegreeOfParallelism = parallelism,
                EnsureOrdered = true,
                CancellationToken = cancellationToken
            });
        var results = new List<WaveformRmsResult>();
        var sink = new ActionBlock<WaveformRmsResult>(x => results.Add(x),
            new ExecutionDataflowBlockOptions { BoundedCapacity = capacity, MaxDegreeOfParallelism = 1, CancellationToken = cancellationToken });
        transform.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        try
        {
            foreach (var capture in captures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await transform.SendAsync(capture, cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException("Waveform processing rejected a capture.");
            }
            transform.Complete();
            await sink.Completion.ConfigureAwait(false);
            return results;
        }
        catch
        {
            ((IDataflowBlock)transform).Fault(new OperationCanceledException("Waveform processing stopped."));
            try { await sink.Completion.ConfigureAwait(false); } catch { }
            throw;
        }
    }
}

/// <summary>Unit-labeled RMS result for a completed oscilloscope acquisition.</summary>
/// <param name="AcquisitionId">Source acquisition identity.</param>
/// <param name="Channel">One-based channel number.</param>
/// <param name="RmsVolts">Root-mean-square voltage in volts.</param>
/// <param name="SampleCount">Number of samples processed.</param>
public sealed record WaveformRmsResult(Guid AcquisitionId, int Channel, double RmsVolts, int SampleCount);
