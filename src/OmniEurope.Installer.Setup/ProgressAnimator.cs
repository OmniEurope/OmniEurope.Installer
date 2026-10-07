using System;
using System.Windows.Forms;

namespace OmniEurope.Installer.Setup;

/// <summary>Drives the bar from the reported percentage, then runs it to 100 % once the operation succeeded.</summary>
internal sealed class ProgressAnimator : IDisposable
{
    private const int FinishStep = 4;
    private readonly Timer _timer = new() { Interval = 50 };
    private readonly Action<int?> _render;
    private readonly Action _finished;
    private int? _target;
    private int? _display;
    private bool _finishing;

    public ProgressAnimator(Action<int?> render, Action finished)
    {
        _render = render;
        _finished = finished;
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        _target = null;
        _display = null;
        _finishing = false;
        _render(null);
        _timer.Start();
    }

    public void Report(int? percent)
    {
        if (percent is { } value && value > (_target ?? 0))
        {
            _target = value;
        }
    }

    /// <summary>The operation succeeded: animate to 100 %, then call the finished callback.</summary>
    public void Finish() => _finishing = true;

    public void Stop() => _timer.Stop();

    public void Dispose() => _timer.Dispose();

    private void Tick()
    {
        if (_finishing)
        {
            _display = Math.Min(100, (_display ?? 0) + FinishStep);
            _render(_display);
            if (_display >= 100)
            {
                _timer.Stop();
                _finished();
            }

            return;
        }

        _display = ProgressAnimation.Step(_display, _target);
        _render(_display);
    }
}
