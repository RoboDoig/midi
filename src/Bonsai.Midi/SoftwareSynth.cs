using System;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Melanchall.DryWetMidi.Core;

namespace Bonsai.Midi
{
    /// <summary>
    /// Represents an operator that plays all note events in the sequence using a simple
    /// polyphonic software synthesizer on the default audio output device.
    /// </summary>
    /// <remarks>
    /// Each event is rendered at a sample accurate position relative to the time at which
    /// it was received, after a fixed output latency, so the relative timing between events
    /// is preserved regardless of the audio buffer length. Audio output is currently only
    /// supported on Windows.
    /// </remarks>
    [Description("Plays all note events in the sequence using a simple polyphonic software synthesizer.")]
    public class SoftwareSynth : Sink<MidiEvent>
    {
        const int SampleRate = 48000;
        const int BufferCount = 4;

        /// <summary>
        /// Gets or sets the oscillator waveform.
        /// </summary>
        [Description("The oscillator waveform.")]
        public SynthWaveform Waveform { get; set; } = SynthWaveform.Triangle;

        /// <summary>
        /// Gets or sets the output gain applied to the mix of all voices.
        /// </summary>
        [Range(0, 1)]
        [Editor(DesignTypes.SliderEditor, DesignTypes.UITypeEditor)]
        [Description("The output gain applied to the mix of all voices.")]
        public double Gain { get; set; } = 0.25;

        /// <summary>
        /// Gets or sets the time, in seconds, for a note to reach full amplitude.
        /// </summary>
        [Description("The time, in seconds, for a note to reach full amplitude.")]
        public double Attack { get; set; } = 0.002;

        /// <summary>
        /// Gets or sets the time, in seconds, for a note to fade out after it is released.
        /// </summary>
        [Description("The time, in seconds, for a note to fade out after it is released.")]
        public double Release { get; set; } = 0.05;

        /// <summary>
        /// Gets or sets the number of samples in each audio buffer. Smaller buffers reduce
        /// output latency but are more susceptible to audio dropouts.
        /// </summary>
        [Description("The number of samples in each audio buffer. Smaller buffers reduce output latency but are more susceptible to audio dropouts.")]
        public int BufferLength { get; set; } = 480;

        /// <summary>
        /// Plays all note events in an observable sequence using the software synthesizer.
        /// </summary>
        /// <param name="source">A sequence of <see cref="MidiEvent"/> objects to play.</param>
        /// <returns>
        /// An observable sequence that is identical to the <paramref name="source"/>
        /// sequence but where there is an additional side effect of playing each
        /// note event on the software synthesizer.
        /// </returns>
        public override IObservable<MidiEvent> Process(IObservable<MidiEvent> source)
        {
            return Observable.Create<MidiEvent>(observer =>
            {
                var bufferLength = BufferLength;
                if (bufferLength <= 0)
                {
                    throw new InvalidOperationException("The audio buffer length must be a positive number of samples.");
                }

                // Schedule events two buffers ahead to absorb jitter in buffer notifications
                var engine = new SynthEngine(SampleRate, leadSamples: 2 * bufferLength);
                var syncObserver = Observer.Synchronize(observer);
                var output = new WaveOutStream(SampleRate, bufferLength, BufferCount, (buffer, timestamp) =>
                {
                    engine.Waveform = Waveform;
                    engine.Gain = Gain;
                    engine.Attack = Attack;
                    engine.Release = Release;
                    engine.Render(buffer, timestamp);
                }, syncObserver.OnError);

                var subscription = source.Do(engine.Enqueue).SubscribeSafe(syncObserver);
                return Disposable.Create(() =>
                {
                    subscription.Dispose();
                    output.Dispose();
                });
            });
        }
    }
}
