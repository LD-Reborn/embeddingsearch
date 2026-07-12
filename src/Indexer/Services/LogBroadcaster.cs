using System.Collections.Concurrent;
using System.Threading.Channels;
using Indexer.Models;

namespace Indexer.Services;

public class LogBroadcaster
{
    private readonly WorkerManager _workerManager;
    private readonly ILogger<LogBroadcaster> _logger;
    private readonly ConcurrentDictionary<string, WorkerSubscription> _subscriptions = new();
    private int _nextSubscriptionId;

    public LogBroadcaster(WorkerManager workerManager, ILogger<LogBroadcaster> logger)
    {
        _workerManager = workerManager;
        _logger = logger;
    }

    public record LogSubscription(Channel<LogEntry> Channel, int SubscriptionId);

    public LogSubscription Subscribe(string workerName, int afterSequenceId)
    {
        if (!_workerManager.Workers.TryGetValue(workerName, out Worker? worker))
            throw new ArgumentException($"Worker '{workerName}' not found.");

        WorkerSubscription subscription = _subscriptions.GetOrAdd(workerName, _ => new WorkerSubscription(worker));

        int subscriptionId = Interlocked.Increment(ref _nextSubscriptionId);
        Channel<LogEntry> channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions { SingleReader = true });

        subscription.AddChannel(subscriptionId, channel);

        // Seed with catch-up logs
        List<LogEntry> catchUpLogs = worker.GetLogsSinceSequence(afterSequenceId);
        foreach (LogEntry log in catchUpLogs)
        {
            channel.Writer.TryWrite(log);
        }

        _logger.LogDebug("SSE subscription {Id} created for worker '{Worker}' (catch-up: {Count} logs)", subscriptionId, workerName, catchUpLogs.Count);

        return new LogSubscription(channel, subscriptionId);
    }

    public void Unsubscribe(string workerName, int subscriptionId)
    {
        if (_subscriptions.TryGetValue(workerName, out WorkerSubscription? subscription))
        {
            subscription.RemoveChannel(subscriptionId);

            if (subscription.IsEmpty)
            {
                _subscriptions.TryRemove(workerName, out _);
                subscription.Dispose();
                _logger.LogDebug("SSE subscription cleaned up for worker '{Worker}'", workerName);
            }
        }
    }

    public void Dispose()
    {
        foreach (WorkerSubscription subscription in _subscriptions.Values)
        {
            subscription.Dispose();
        }
        _subscriptions.Clear();
    }

    private class WorkerSubscription : IDisposable
    {
        private readonly Worker _worker;
        private readonly ConcurrentDictionary<int, Channel<LogEntry>> _channels = new();
        private readonly Action<LogEntry> _onLogHandler;

        public bool IsEmpty => _channels.IsEmpty;

        public WorkerSubscription(Worker worker)
        {
            _worker = worker;
            _onLogHandler = OnLogReceived;
            _worker.ScriptContainer.ToolSet.OnLog += _onLogHandler;
        }

        public void AddChannel(int id, Channel<LogEntry> channel)
        {
            _channels[id] = channel;
        }

        public void RemoveChannel(int id)
        {
            if (_channels.TryRemove(id, out Channel<LogEntry>? channel))
            {
                channel.Writer.Complete();
            }
        }

        private void OnLogReceived(LogEntry logEntry)
        {
            foreach (Channel<LogEntry> channel in _channels.Values)
            {
                channel.Writer.TryWrite(logEntry);
            }
        }

        public void Dispose()
        {
            _worker.ScriptContainer.ToolSet.OnLog -= _onLogHandler;
            foreach (Channel<LogEntry> channel in _channels.Values)
            {
                channel.Writer.TryComplete();
            }
            _channels.Clear();
        }
    }
}
