using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace Bonsai.Midi
{
    /// <summary>
    /// Represents an operator that generates a high resolution clock signal for driving
    /// MIDI sequence playback.
    /// </summary>
    /// <remarks>
    /// Each subscription runs a dedicated high priority thread and emits the elapsed
    /// time since the clock started. Tick deadlines are computed from the clock origin,
    /// so timer jitter never accumulates into drift. To keep multiple sequences
    /// synchronized, share a single clock, or a single <see cref="Transport"/> driven
    /// by it, between all sequence players, e.g. using a publish subject.
    /// </remarks>
    [Description("Generates a high resolution clock signal for driving MIDI sequence playback.")]
    public class SequencerClock : Source<TimeSpan>
    {
        /// <summary>
        /// Gets or sets the interval between clock ticks.
        /// </summary>
        [XmlIgnore]
        [Description("The interval between clock ticks.")]
        public TimeSpan Period { get; set; } = TimeSpan.FromMilliseconds(1);

        /// <summary>
        /// Gets or sets an XML representation of the period for serialization.
        /// </summary>
        [Browsable(false)]
        [XmlElement(nameof(Period))]
        public string PeriodXml
        {
            get => XmlConvert.ToString(Period);
            set => Period = XmlConvert.ToTimeSpan(value);
        }

        /// <summary>
        /// Generates an observable sequence of clock ticks, where each value is the
        /// elapsed time since the clock started.
        /// </summary>
        /// <returns>
        /// A sequence of <see cref="TimeSpan"/> values representing the elapsed time
        /// at each clock tick.
        /// </returns>
        public override IObservable<TimeSpan> Generate()
        {
            return Observable.Create<TimeSpan>(observer =>
            {
                var period = Period;
                if (period <= TimeSpan.Zero)
                {
                    throw new InvalidOperationException("The clock period must be a positive time interval.");
                }

                var cancellation = new CancellationTokenSource();
                var thread = new Thread(() =>
                {
                    using var timer = new PrecisionTimer();
                    var token = cancellation.Token;
                    var stopwatch = Stopwatch.StartNew();
                    var tick = 0L;
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            var elapsed = stopwatch.Elapsed;
                            observer.OnNext(elapsed);

                            // Schedule the next deadline relative to the clock origin to avoid drift,
                            // skipping any ticks that were missed while processing
                            tick = Math.Max(tick + 1, elapsed.Ticks / period.Ticks + 1);
                            var deadline = TimeSpan.FromTicks(tick * period.Ticks);
                            timer.Wait(deadline - stopwatch.Elapsed);
                        }
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                });
                thread.IsBackground = true;
                thread.Priority = ThreadPriority.Highest;
                thread.Name = nameof(SequencerClock);
                thread.Start();
                return cancellation.Cancel;
            });
        }
    }
}
