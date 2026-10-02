using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace BukaMusicDesktop.Core;

/// <summary>One file waiting to be uploaded.</summary>
public sealed class UploadItem
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public long Size { get; init; }

    /// <summary>Name + size, used to spot a file that is already queued.</summary>
    public string Key => $"{Name}|{Size}";
}

/// <summary>
/// The console's one and only upload pipeline.
///
/// The library page is rebuilt on every navigation, so the queue cannot live in
/// the page: picking files while a batch is running used to start a second loop
/// that fought the first one for the same progress bar, and leaving the page
/// mid-upload threw the progress and the text away. Here the state is shared,
/// each pick becomes its own batch, and a page that comes back simply re-reads
/// (and re-subscribes to) it.
/// </summary>
public sealed class UploadQueue
{
    private sealed class Batch
    {
        public BukaClient? Client;
        public List<UploadItem> Items = new();
    }

    public static UploadQueue Instance { get; } = new();

    private readonly object _gate = new();
    private readonly Queue<Batch> _batches = new();
    private readonly HashSet<string> _pendingKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _reasons = new();
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private DispatcherQueueTimer? _timer;

    private bool _running;
    private int _batchId;
    private int _index = 0;
    private int _total = 0;
    private int _ok = 0;
    private int _failed = 0;
    private string _current = "";
    private double _progress;
    private string _summary = "";
    private string _notice = "";

    /// <summary>Raised (on the UI thread) whenever the state below changes.</summary>
    public event Action? Changed;

    /// <summary>Raised once the queue has drained, so the library can refresh.</summary>
    public event Action? Drained;

    public bool IsBusy
    {
        get { lock (_gate) return _running; }
    }

    public bool Visible
    {
        get { lock (_gate) return _running || _summary.Length > 0; }
    }

    public double Progress
    {
        get { lock (_gate) return _progress; }
    }

    public string Status
    {
        get { lock (_gate) return ComposeLocked(); }
    }

    /// <summary>
    /// Line shown under the running status: the waiting files and the transient
    /// notices. It gets its own line because the status line is a clipped,
    /// scrolling marquee - text appended to it is simply not visible.
    /// </summary>
    public string SecondaryStatus
    {
        get { lock (_gate) return SecondaryLocked(); }
    }

    /// <summary>
    /// Adds one pick to the queue. Duplicates of files that are still waiting
    /// are skipped with the same wording the web page uses.
    /// </summary>
    public void Enqueue(BukaClient client, IReadOnlyList<UploadItem> items)
    {
        if (items == null || items.Count == 0) return;
        bool startLoop = false;
        var batch = new Batch { Client = client };
        lock (_gate)
        {
            foreach (UploadItem item in items)
            {
                if (!_pendingKeys.Add(item.Key))
                {
                    _notice = Loc.Current.Text("{0}：已在列表中", item.Name);
                    LogBus.Warn(_notice);
                    continue;
                }
                batch.Items.Add(item);
            }
            if (batch.Items.Count > 0)
            {
                _batches.Enqueue(batch);
                _summary = "";
                if (!_running)
                {
                    _running = true;
                    startLoop = true;
                }
            }
        }

        if (batch.Items.Count == 0)
        {
            // Nothing new: just show the notice for a moment.
            ScheduleClear();
        }
        if (startLoop) _ = RunAsync();
        RaiseChanged();
    }

    private async Task RunAsync()
    {
        while (true)
        {
            Batch? batch;
            lock (_gate)
            {
                if (_batches.Count == 0)
                {
                    _running = false;
                    _current = "";
                    _progress = 0;
                    break;
                }
                batch = _batches.Dequeue();
                _index = 0;
                _total = batch.Items.Count;
                _ok = 0;
                _failed = 0;
                _reasons.Clear();
                _progress = 0;
                _summary = "";
                _batchId++;
            }
            RaiseChanged();

            double share = 100d / Math.Max(1, batch.Items.Count);
            int batchId = _batchId;
            foreach (UploadItem item in batch.Items)
            {
                lock (_gate)
                {
                    _current = item.Name;
                }
                RaiseChanged();

                int index = _index;
                var progress = new Progress<double>(fraction =>
                {
                    lock (_gate)
                    {
                        // A late callback from the previous batch must not move
                        // the bar of the one that is running now.
                        if (!_running || _batchId != batchId) return;
                        _progress = Math.Min(100d, index * share + fraction * share);
                    }
                    RaiseChanged();
                });

                UploadOutcome outcome = await batch.Client!.UploadAsync(item.Path, progress);
                lock (_gate)
                {
                    _pendingKeys.Remove(item.Key);
                    if (outcome.Ok)
                    {
                        _ok++;
                    }
                    else
                    {
                        _failed++;
                        if (!string.IsNullOrWhiteSpace(outcome.Message)) _reasons.Add(outcome.Message);
                    }
                    _index++;
                    _progress = Math.Min(100d, _index * share);
                }
                RaiseChanged();
            }

            string summary = Loc.Current.Text("全部完成：成功 {0}，失败 {1}", _ok, _failed);
            // The web page pops one toast per failure; the console has a single
            // status line, so the reason of the last failure goes with the count.
            if (_reasons.Count > 0) summary += " · " + _reasons[^1];
            lock (_gate)
            {
                _summary = summary;
            }
            LogBus.Success(summary);
            RaiseChanged();
            ScheduleClear();
        }

        RaiseChanged();
        Drained?.Invoke();
    }

    /// <summary>Clears the finished/notice text five seconds after it appeared.</summary>
    private void ScheduleClear()
    {
        DispatcherQueue? dispatcher = _dispatcher;
        if (dispatcher == null) return;
        _timer ??= dispatcher.CreateTimer();
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(5);
        _timer.IsRepeating = false;
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        sender.Tick -= OnTimerTick;
        lock (_gate)
        {
            // While a batch is running the progress text stays; only the
            // finished summary and the transient notices are cleared.
            _summary = "";
            _notice = "";
        }
        RaiseChanged();
    }

    private string ComposeLocked()
        => _running
                ? Loc.Current.Text("正在上传 ({0}/{1})：{2}",
                        Math.Min(_index + 1, Math.Max(1, _total)), _total, _current)
                : _summary;

    private string SecondaryLocked()
    {
        string text = _notice;
        string waiting = WaitingLocked();
        if (waiting.Length > 0)
        {
            text = text.Length == 0 ? waiting : text + " · " + waiting;
        }
        return text;
    }

    /// <summary>
    /// "第一个待上传的文件名… 等待上传" - the queue is shown as one name plus an
    /// ellipsis so the running batch's own progress keeps the room.
    /// </summary>
    private string WaitingLocked()
    {
        if (_batches.Count == 0) return "";
        string first = "";
        int count = 0;
        foreach (Batch batch in _batches)
        {
            foreach (UploadItem item in batch.Items)
            {
                if (first.Length == 0) first = item.Name;
                count++;
            }
        }
        if (first.Length == 0) return "";
        return count <= 1
                ? Loc.Current.Text("{0}… 等待上传", first)
                : Loc.Current.Text("{0}… 共 {1} 个文件等待上传", first, count);
    }

    private void RaiseChanged() => Changed?.Invoke();
}
