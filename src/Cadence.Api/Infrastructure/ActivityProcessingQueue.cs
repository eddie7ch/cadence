using System.Threading.Channels;
using Cadence.Application.Handlers;

namespace Cadence.Api.Infrastructure;

/// <summary>
/// The hand-off between the upload request and the parser. Registered as a
/// singleton so the writer (a request thread) and the reader (the background
/// worker) share one channel.
/// </summary>
public sealed class ActivityProcessingQueue
{
    /// <remarks>
    /// Bounded on purpose. An unbounded channel would let a burst of uploads grow
    /// the backlog until the process runs out of memory, and every queued id would
    /// be lost on restart anyway. A full queue instead makes the upload request
    /// wait, which is backpressure the client can actually feel.
    /// </remarks>
    private const int Capacity = 256;

    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    public ValueTask EnqueueAsync(Guid activityId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(activityId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Drains <see cref="ActivityProcessingQueue"/>. This is the asynchronous half of
/// ingestion: the upload endpoint returns 202 as soon as the bytes are on disk and
/// parsing happens here, off the request thread.
/// </summary>
public sealed class ActivityProcessingWorker(
    ActivityProcessingQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ActivityProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Activity processing worker started.");

        try
        {
            await foreach (var activityId in queue.DequeueAllAsync(stoppingToken))
            {
                await ProcessOneAsync(activityId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ordinary shutdown.
        }

        logger.LogInformation("Activity processing worker stopped.");
    }

    private async Task ProcessOneAsync(Guid activityId, CancellationToken cancellationToken)
    {
        // A fresh scope per item is not a stylistic choice. The handler depends on
        // scoped services - the DbContext and the repositories built on it - while
        // this worker is a singleton and therefore has no scope of its own.
        // Resolving them from the root provider would captive-dependency a single
        // DbContext into the lifetime of the process: it would accumulate tracked
        // entities from every activity ever queued, hold one connection open
        // forever, and surface one activity's failed change-tracker state as a
        // corrupt save on the next. Disposing the scope per item also returns the
        // connection to the pool between files.
        await using var scope = scopeFactory.CreateAsyncScope();

        try
        {
            var handler = scope.ServiceProvider.GetRequiredService<ProcessActivityHandler>();
            var result = await handler.ExecuteAsync(activityId, cancellationToken);

            if (result.IsSuccess)
            {
                logger.LogInformation("Processed activity {ActivityId}.", activityId);
            }
            else
            {
                // A file the parser rejects is a normal outcome: the handler records
                // the failure on the activity row, and the athlete sees it in the UI.
                logger.LogWarning(
                    "Activity {ActivityId} could not be processed: {Error}",
                    activityId,
                    result.Error!.Message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One poisoned file must not stop the pipeline for every later upload.
            logger.LogError(ex, "Processing activity {ActivityId} threw an unhandled exception.", activityId);
        }
    }
}
