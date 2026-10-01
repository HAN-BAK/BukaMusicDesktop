using System;
using System.Threading;
using System.Threading.Tasks;

namespace BukaMusicDesktop.Core;

/// <summary>Live session with one device: shared state + polling.</summary>
public sealed class Session : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private bool _started;

    public BukaClient Client { get; }
    public DeviceInfo Device => Client.Device;
    public DeviceState State { get; } = new();
    public bool Connected { get; private set; }
    /// <summary>When the state was last refreshed (used to extrapolate position).</summary>
    public DateTime StateUpdatedAt { get; private set; } = DateTime.MinValue;

    public event Action? StateUpdated;
    public event Action<bool>? ConnectionChanged;

    public Session(BukaClient client)
    {
        Client = client;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                var state = await Client.GetStateAsync(_cts.Token).ConfigureAwait(false);
                bool connected = state != null;
                if (connected && state != null)
                {
                    Apply(state);
                }
                if (connected != Connected)
                {
                    Connected = connected;
                    ConnectionChanged?.Invoke(connected);
                }
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(connected ? 1000 : 2500), _cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, _cts.Token);
    }

    private void Apply(DeviceState state)
    {
        State.Source = state.Source;
        State.Playing = state.Playing;
        State.Title = state.Title;
        State.Artist = state.Artist;
        State.Album = state.Album;
        State.Path = state.Path;
        State.PositionMs = state.PositionMs;
        State.DurationMs = state.DurationMs;
        State.Mode = state.Mode;
        State.StatusText = state.StatusText;
        State.ClientName = state.ClientName;
        State.HasCover = state.HasCover;
        State.Volume = state.Volume;
        State.VolumeMax = state.VolumeMax;
        StateUpdatedAt = DateTime.Now;
        StateUpdated?.Invoke();
    }

    /// <summary>Estimated playback position, extrapolated between polls.</summary>
    public int EstimatedPositionMs()
    {
        var position = State.PositionMs;
        if (!State.Playing || StateUpdatedAt == DateTime.MinValue) return position;
        var elapsed = (DateTime.Now - StateUpdatedAt).TotalMilliseconds;
        if (elapsed <= 0 || elapsed > 4000) return position;
        var estimate = position + (int)elapsed;
        return State.DurationMs > 0 ? Math.Min(estimate, State.DurationMs) : estimate;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
