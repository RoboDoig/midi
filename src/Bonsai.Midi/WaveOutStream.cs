using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Bonsai.Midi
{
    /// <summary>
    /// Streams mono 16-bit audio to the default Windows audio output device, pulling
    /// each block of samples from a render callback on a dedicated thread.
    /// </summary>
    sealed class WaveOutStream : IDisposable
    {
        const uint WAVE_MAPPER = 0xFFFFFFFF;
        const uint CALLBACK_EVENT = 0x00050000;
        const ushort WAVE_FORMAT_PCM = 1;
        const int WHDR_DONE = 0x00000001;
        const int MMSYSERR_NOERROR = 0;

        static readonly int HeaderSize = Marshal.SizeOf(typeof(WaveHeader));
        static readonly int FlagsOffset = (int)Marshal.OffsetOf(typeof(WaveHeader), nameof(WaveHeader.dwFlags));

        readonly IntPtr device;
        readonly IntPtr[] headers;
        readonly float[] renderBuffer;
        readonly short[] sampleBuffer;
        readonly AutoResetEvent bufferDone = new(false);
        readonly Action<float[], long?> render;
        readonly Action<Exception> error;
        readonly Thread thread;
        volatile bool stopRequested;

        public WaveOutStream(int sampleRate, int bufferLength, int bufferCount, Action<float[], long?> render, Action<Exception> error)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                throw new PlatformNotSupportedException("Audio output is currently only supported on Windows.");
            }

            this.render = render;
            this.error = error;
            renderBuffer = new float[bufferLength];
            sampleBuffer = new short[bufferLength];
            var format = new WaveFormatEx
            {
                wFormatTag = WAVE_FORMAT_PCM,
                nChannels = 1,
                nSamplesPerSec = (uint)sampleRate,
                wBitsPerSample = 16,
                nBlockAlign = 2,
                nAvgBytesPerSec = (uint)sampleRate * 2
            };

            var result = waveOutOpen(out device, WAVE_MAPPER, ref format, bufferDone.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
            ThrowOnError(result, nameof(waveOutOpen));

            headers = new IntPtr[bufferCount];
            for (int i = 0; i < headers.Length; i++)
            {
                var header = new WaveHeader
                {
                    lpData = Marshal.AllocHGlobal(bufferLength * sizeof(short)),
                    dwBufferLength = (uint)(bufferLength * sizeof(short))
                };
                headers[i] = Marshal.AllocHGlobal(HeaderSize);
                Marshal.StructureToPtr(header, headers[i], false);
                ThrowOnError(waveOutPrepareHeader(device, headers[i], (uint)HeaderSize), nameof(waveOutPrepareHeader));
            }

            thread = new Thread(RenderLoop);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.Highest;
            thread.Name = nameof(WaveOutStream);
            thread.Start();
        }

        void RenderLoop()
        {
            try
            {
                // Prime all buffers before waiting on device notifications
                foreach (var header in headers)
                {
                    WriteBuffer(header, timestamp: null);
                }

                var next = 0;
                while (!stopRequested)
                {
                    bufferDone.WaitOne(100);
                    var timestamp = Stopwatch.GetTimestamp();
                    while (!stopRequested && (Marshal.ReadInt32(headers[next], FlagsOffset) & WHDR_DONE) != 0)
                    {
                        WriteBuffer(headers[next], timestamp);
                        next = (next + 1) % headers.Length;
                    }
                }
            }
            catch (Exception ex)
            {
                error(ex);
            }
        }

        void WriteBuffer(IntPtr header, long? timestamp)
        {
            render(renderBuffer, timestamp);
            for (int i = 0; i < renderBuffer.Length; i++)
            {
                sampleBuffer[i] = (short)(Math.Max(-1f, Math.Min(1f, renderBuffer[i])) * short.MaxValue);
            }

            var data = Marshal.ReadIntPtr(header);
            Marshal.Copy(sampleBuffer, 0, data, sampleBuffer.Length);
            ThrowOnError(waveOutWrite(device, header, (uint)HeaderSize), nameof(waveOutWrite));
        }

        static void ThrowOnError(int result, string function)
        {
            if (result != MMSYSERR_NOERROR)
            {
                throw new InvalidOperationException($"Audio output error {result} in {function}.");
            }
        }

        public void Dispose()
        {
            stopRequested = true;
            bufferDone.Set();
            thread.Join();
            waveOutReset(device);
            foreach (var header in headers)
            {
                waveOutUnprepareHeader(device, header, (uint)HeaderSize);
                Marshal.FreeHGlobal(Marshal.ReadIntPtr(header));
                Marshal.FreeHGlobal(header);
            }
            waveOutClose(device);
            bufferDone.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WaveFormatEx
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WaveHeader
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll")]
        static extern int waveOutOpen(out IntPtr phwo, uint uDeviceID, ref WaveFormatEx pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

        [DllImport("winmm.dll")]
        static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

        [DllImport("winmm.dll")]
        static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

        [DllImport("winmm.dll")]
        static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, uint cbwh);

        [DllImport("winmm.dll")]
        static extern int waveOutReset(IntPtr hwo);

        [DllImport("winmm.dll")]
        static extern int waveOutClose(IntPtr hwo);
    }
}
