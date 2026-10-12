using System.Windows.Threading;

namespace Yptv;

// A timer that fires once per Restart() and is stopped otherwise, so nothing ticks while idle.
sealed class OneShot
{
    readonly DispatcherTimer _timer;

    public OneShot(TimeSpan delay, Action action)
    {
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            action();
        };
    }

    public TimeSpan Delay { set => _timer.Interval = value; }

    public void Restart()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();
}
