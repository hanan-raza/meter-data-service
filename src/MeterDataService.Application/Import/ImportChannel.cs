using System.Threading.Channels;

namespace MeterDataService.Application.Import;

/// <summary>
/// Queue between the upload endpoint and <see cref="ImportBackgroundService"/>, registered as a singleton.
/// </summary>
/// <remarks>
/// The default channel is bounded: when imports arrive faster than they are processed, <see cref="TryEnqueue"/>
/// fails instead of letting queued file contents grow memory without limit, and the endpoint can answer
/// "try again later". It has a single reader because jobs of one market location must not be processed out
/// of order once they are persisted.
/// </remarks>
public sealed class ImportChannel
{
    public const int DefaultCapacity = 100;

    private readonly Channel<ImportJob> _channel;

    public ImportChannel(int capacity = DefaultCapacity)
        : this(Channel.CreateBounded<ImportJob>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        }))
    {
    }

    /// <summary>Wraps an existing channel, e.g. an unbounded one in tests.</summary>
    public ImportChannel(Channel<ImportJob> channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _channel = channel;
    }

    public ChannelReader<ImportJob> Reader => _channel.Reader;

    /// <summary>Jobs waiting to be picked up; 0 if the underlying channel can't count.</summary>
    public int Count => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

    /// <returns><c>false</c> if the queue is full or completed; the job was not queued.</returns>
    public bool TryEnqueue(ImportJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return _channel.Writer.TryWrite(job);
    }
}
