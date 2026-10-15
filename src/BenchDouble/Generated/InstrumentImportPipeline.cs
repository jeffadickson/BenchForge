using System.Threading.Tasks.Dataflow;

namespace BenchDouble.Generated;

/// <summary>
/// Bounded TPL Dataflow processing of imported schema-v2 definitions.
/// Validation and downstream work run with explicit concurrency and backpressure.
/// </summary>
public static class InstrumentImportPipeline
{
    /// <summary>
    /// Validate JSON definitions concurrently and return results in submission order.
    /// No instrument I/O or generated code execution occurs in this pipeline.
    /// </summary>
    /// <param name="definitions">JSON definitions to validate.</param>
    /// <param name="capacity">Maximum number of buffered or executing definitions.</param>
    /// <param name="parallelism">Maximum number of concurrent validators.</param>
    /// <param name="cancellationToken">Cancellation for all stages.</param>
    /// <returns>Validated definitions in input order.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Capacity or parallelism is invalid.</exception>
    /// <exception cref="AggregateException">A definition failed validation.</exception>
    public static async Task<IReadOnlyList<ImportedInstrumentDefinition>> ValidateAsync(
        IEnumerable<string> definitions, int capacity = 16, int parallelism = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (parallelism < 1) throw new ArgumentOutOfRangeException(nameof(parallelism));

        var transform = new TransformBlock<(int Index, string Json), (int Index, ImportedInstrumentDefinition Definition)>(
            item => (item.Index, ImportedInstrumentDefinition.Parse(item.Json)),
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = capacity,
                MaxDegreeOfParallelism = parallelism,
                EnsureOrdered = true,
                CancellationToken = cancellationToken
            });
        var results = new List<(int Index, ImportedInstrumentDefinition Definition)>();
        var sink = new ActionBlock<(int Index, ImportedInstrumentDefinition Definition)>(
            item => results.Add(item),
            new ExecutionDataflowBlockOptions { BoundedCapacity = capacity, MaxDegreeOfParallelism = 1, CancellationToken = cancellationToken });
        transform.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });

        try
        {
            int index = 0;
            foreach (var json in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await transform.SendAsync((index++, json), cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException("Import pipeline rejected a definition.");
            }
            transform.Complete();
            await sink.Completion.ConfigureAwait(false);
            return results.OrderBy(x => x.Index).Select(x => x.Definition).ToArray();
        }
        catch
        {
            ((IDataflowBlock)transform).Fault(new OperationCanceledException("Import pipeline stopped."));
            try { await sink.Completion.ConfigureAwait(false); } catch { }
            throw;
        }
    }
}
