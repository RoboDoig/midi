using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Melanchall.DryWetMidi.Core;

namespace Bonsai.MidiSequencing
{
    /// <summary>
    /// Renders a simple polyphonic synthesizer into blocks of audio samples.
    /// </summary>
    /// <remarks>
    /// Events are timestamped when they are received and rendered at a sample accurate
    /// position after a fixed latency, so block based rendering does not add timing jitter.
    /// The mapping between the event clock and the audio sample clock is continuously
    /// smoothed to absorb jitter in buffer callbacks and drift between the two clocks.
    /// </remarks>
    sealed class SynthEngine
    {
        const int MaxVoices = 32;
        const double ClockSmoothing = 0.01;

        readonly int sampleRate;
        readonly int leadSamples;
        readonly Voice[] voices = new Voice[MaxVoices];
        readonly ConcurrentQueue<(long Timestamp, MidiEvent Event)> eventQueue = new();
        long renderedSamples;
        long voiceCounter;
        double? clockOffset;

        public SynthEngine(int sampleRate, int leadSamples)
        {
            this.sampleRate = sampleRate;
            this.leadSamples = leadSamples;
        }

        public SynthWaveform Waveform { get; set; }

        public double Gain { get; set; }

        public double Attack { get; set; }

        public double Release { get; set; }

        public void Enqueue(MidiEvent midiEvent)
        {
            Enqueue(Stopwatch.GetTimestamp(), midiEvent);
        }

        public void Enqueue(long timestamp, MidiEvent midiEvent)
        {
            eventQueue.Enqueue((timestamp, midiEvent));
        }

        /// <summary>
        /// Renders the next block of samples.
        /// </summary>
        /// <param name="buffer">The buffer to fill with audio samples.</param>
        /// <param name="timestamp">
        /// The <see cref="Stopwatch"/> timestamp at which the audio device requested
        /// this block, or <see langword="null"/> if the request was not paced by the
        /// device, e.g. while priming output buffers.
        /// </param>
        public void Render(float[] buffer, long? timestamp)
        {
            if (timestamp.HasValue)
            {
                var measuredOffset = (double)timestamp.Value / Stopwatch.Frequency - (double)renderedSamples / sampleRate;
                clockOffset = clockOffset.HasValue
                    ? clockOffset + ClockSmoothing * (measuredOffset - clockOffset.Value)
                    : measuredOffset;
            }

            Array.Clear(buffer, 0, buffer.Length);
            var position = 0;
            while (eventQueue.TryPeek(out var item))
            {
                var eventPosition = GetSamplePosition(item.Timestamp);
                if (eventPosition >= buffer.Length) break;

                eventPosition = Math.Max(position, (int)eventPosition);
                RenderVoices(buffer, position, (int)eventPosition);
                position = (int)eventPosition;
                ProcessEvent(item.Event);
                eventQueue.TryDequeue(out _);
            }

            RenderVoices(buffer, position, buffer.Length);
            var gain = (float)Gain;
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (float)Math.Tanh(buffer[i] * gain);
            }
            renderedSamples += buffer.Length;
        }

        long GetSamplePosition(long timestamp)
        {
            if (!clockOffset.HasValue)
                return 0;

            var eventTime = (double)timestamp / Stopwatch.Frequency - clockOffset.Value;
            return (long)Math.Round(eventTime * sampleRate) + leadSamples - renderedSamples;
        }

        void ProcessEvent(MidiEvent midiEvent)
        {
            switch (midiEvent)
            {
                case NoteOnEvent noteOn when noteOn.Velocity > 0:
                    NoteOn(noteOn.Channel, noteOn.NoteNumber, noteOn.Velocity);
                    break;
                case NoteOnEvent noteOn:
                    NoteOff(noteOn.Channel, noteOn.NoteNumber);
                    break;
                case NoteOffEvent noteOff:
                    NoteOff(noteOff.Channel, noteOff.NoteNumber);
                    break;
            }
        }

        void NoteOn(int channel, int noteNumber, int velocity)
        {
            var index = -1;
            for (int i = 0; i < voices.Length; i++)
            {
                if (voices[i].Stage == EnvelopeStage.Idle)
                {
                    if (index < 0) index = i;
                }
                else if (voices[i].Channel == channel && voices[i].NoteNumber == noteNumber)
                {
                    // Retrigger the existing voice from its current level
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                // Steal the oldest voice
                index = 0;
                for (int i = 1; i < voices.Length; i++)
                {
                    if (voices[i].Age < voices[index].Age) index = i;
                }
            }

            ref var voice = ref voices[index];
            var frequency = 440.0 * Math.Pow(2, (noteNumber - 69) / 12.0);
            voice.Channel = channel;
            voice.NoteNumber = noteNumber;
            voice.Age = voiceCounter++;
            voice.Amplitude = velocity / 127.0;
            voice.PhaseIncrement = frequency / sampleRate;
            voice.AttackRate = Attack > 0 ? 1.0 / (Attack * sampleRate) : 1.0;
            voice.ReleaseRate = Release > 0 ? 1.0 / (Release * sampleRate) : 1.0;
            voice.Stage = EnvelopeStage.Attack;
        }

        void NoteOff(int channel, int noteNumber)
        {
            for (int i = 0; i < voices.Length; i++)
            {
                ref var voice = ref voices[i];
                if (voice.Stage != EnvelopeStage.Idle &&
                    voice.Stage != EnvelopeStage.Release &&
                    voice.Channel == channel &&
                    voice.NoteNumber == noteNumber)
                {
                    voice.Stage = EnvelopeStage.Release;
                }
            }
        }

        void RenderVoices(float[] buffer, int start, int end)
        {
            var waveform = Waveform;
            for (int v = 0; v < voices.Length; v++)
            {
                ref var voice = ref voices[v];
                for (int i = start; i < end && voice.Stage != EnvelopeStage.Idle; i++)
                {
                    switch (voice.Stage)
                    {
                        case EnvelopeStage.Attack:
                            voice.Envelope += voice.AttackRate;
                            if (voice.Envelope >= 1)
                            {
                                voice.Envelope = 1;
                                voice.Stage = EnvelopeStage.Sustain;
                            }
                            break;
                        case EnvelopeStage.Release:
                            voice.Envelope -= voice.ReleaseRate;
                            if (voice.Envelope <= 0)
                            {
                                voice.Envelope = 0;
                                voice.Stage = EnvelopeStage.Idle;
                            }
                            break;
                    }

                    buffer[i] += (float)(Oscillator(waveform, voice.Phase) * voice.Envelope * voice.Amplitude);
                    voice.Phase += voice.PhaseIncrement;
                    if (voice.Phase >= 1) voice.Phase -= 1;
                }
            }
        }

        static double Oscillator(SynthWaveform waveform, double phase)
        {
            return waveform switch
            {
                SynthWaveform.Triangle => 1 - 4 * Math.Abs(phase - 0.5),
                SynthWaveform.Sawtooth => 2 * phase - 1,
                SynthWaveform.Square => phase < 0.5 ? 1 : -1,
                _ => Math.Sin(2 * Math.PI * phase)
            };
        }

        enum EnvelopeStage
        {
            Idle,
            Attack,
            Sustain,
            Release
        }

        struct Voice
        {
            public EnvelopeStage Stage;
            public int Channel;
            public int NoteNumber;
            public long Age;
            public double Amplitude;
            public double Phase;
            public double PhaseIncrement;
            public double Envelope;
            public double AttackRate;
            public double ReleaseRate;
        }
    }
}
