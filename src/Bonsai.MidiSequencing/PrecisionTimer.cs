using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Bonsai.MidiSequencing
{
    /// <summary>
    /// Provides sub-millisecond blocking waits for the current thread. On Windows this uses
    /// a high resolution waitable timer when available, falling back to raising the system
    /// timer resolution to 1 ms. On other platforms it falls back to <see cref="Thread.Sleep(int)"/>.
    /// </summary>
    sealed class PrecisionTimer : IDisposable
    {
        const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
        const uint TIMER_ALL_ACCESS = 0x1F0003;
        const uint INFINITE = 0xFFFFFFFF;

        readonly IntPtr timerHandle;
        readonly bool timePeriodRaised;

        public PrecisionTimer()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return;

            timerHandle = CreateWaitableTimerExW(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
            if (timerHandle == IntPtr.Zero)
            {
                // High resolution timers require Windows 10 1803 or later
                timerHandle = CreateWaitableTimerExW(IntPtr.Zero, null, 0, TIMER_ALL_ACCESS);
                timePeriodRaised = timeBeginPeriod(1) == 0;
            }
        }

        public void Wait(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero)
                return;

            if (timerHandle != IntPtr.Zero)
            {
                // Negative due time specifies a relative interval in 100 ns units
                var dueTime = -interval.Ticks;
                if (SetWaitableTimer(timerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
                {
                    WaitForSingleObject(timerHandle, INFINITE);
                    return;
                }
            }

            Thread.Sleep(Math.Max(1, (int)interval.TotalMilliseconds));
        }

        public void Dispose()
        {
            if (timerHandle != IntPtr.Zero)
                CloseHandle(timerHandle);
            if (timePeriodRaised)
                timeEndPeriod(1);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateWaitableTimerExW(IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetWaitableTimer(IntPtr hTimer, ref long pDueTime, int lPeriod, IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, [MarshalAs(UnmanagedType.Bool)] bool fResume);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("winmm.dll")]
        static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        static extern uint timeEndPeriod(uint uPeriod);
    }
}
