using System;
using System.Threading;
using Avalonia.Threading;
using Xilium.CefGlue.Common.Handlers;

namespace Xilium.CefGlue.Avalonia
{
    internal class AvaloniaBrowserProcessHandler : BrowserProcessHandler
    {
        private Timer _current;
        private object _schedule = new object();

        protected override void OnScheduleMessagePumpWork(long delayMs)
        {
            lock (_schedule)
            {
                if (_current != null)
                {
                    _current.Dispose();
                }

                if (delayMs <= 0)
                {
                    delayMs = 1;
                }

                // System.Threading.Timer rather than DispatcherTimer: WM_TIMER can be starved by the hosting message loop
                _current = new Timer(_ => Dispatcher.UIThread.Post(CefRuntime.DoMessageLoopWork), null, delayMs, delayMs);
            }
        }
    }
}
