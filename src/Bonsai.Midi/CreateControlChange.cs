using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Melanchall.DryWetMidi.Core;

namespace Bonsai.Midi
{
    /// <summary>
    /// Represents an operator that creates a MIDI control change event with the specified properties.
    /// </summary>
    /// <remarks>
    /// If the input is a sequence of numeric values, each value is linearly mapped from the
    /// range between <see cref="Min"/> and <see cref="Max"/> to a control value between 0 and
    /// 127, clamping values outside the range. An event is emitted only when the resulting
    /// control value changes. For any other input, an event with the specified
    /// <see cref="Value"/> is emitted for each notification.
    /// </remarks>
    [Description("Creates a MIDI control change event with the specified properties.")]
    public class CreateControlChange : Source<MidiEvent>
    {
        const int MaxControlValue = 127;

        /// <summary>
        /// Gets or sets the MIDI channel of the control change event, from 0 to 15.
        /// </summary>
        [Range(0, 15)]
        [Description("The MIDI channel of the control change event, from 0 to 15.")]
        public int Channel { get; set; }

        /// <summary>
        /// Gets or sets the controller number, from 0 to 119. Numbers 120 to 127 are
        /// reserved for channel mode messages, such as all notes off.
        /// </summary>
        [Range(0, 127)]
        [Description("The controller number, from 0 to 119. Numbers 120 to 127 are reserved for channel mode messages, such as all notes off.")]
        public int ControlNumber { get; set; } = 20;

        /// <summary>
        /// Gets or sets the control value, from 0 to 127, used when the input is not numeric.
        /// </summary>
        [Range(0, 127)]
        [Description("The control value, from 0 to 127, used when the input is not numeric.")]
        public int Value { get; set; }

        /// <summary>
        /// Gets or sets the numeric input value which is mapped to a control value of 0.
        /// </summary>
        [Description("The numeric input value which is mapped to a control value of 0.")]
        public double Min { get; set; }

        /// <summary>
        /// Gets or sets the numeric input value which is mapped to a control value of 127.
        /// </summary>
        [Description("The numeric input value which is mapped to a control value of 127.")]
        public double Max { get; set; } = 1;

        /// <summary>
        /// Generates an observable sequence that contains a single MIDI control change event.
        /// </summary>
        /// <returns>A sequence containing a single <see cref="MidiEvent"/> object.</returns>
        public override IObservable<MidiEvent> Generate()
        {
            return Observable.Defer(() => Observable.Return(CreateEvent(Value)));
        }

        /// <summary>
        /// Generates an observable sequence of MIDI control change events with the specified
        /// <see cref="Value"/>, where a new event is emitted whenever the source sequence emits
        /// a notification.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements in the <paramref name="source"/> sequence.</typeparam>
        /// <param name="source">The sequence containing the notifications used to emit new events.</param>
        /// <returns>A sequence of <see cref="MidiEvent"/> objects.</returns>
        public IObservable<MidiEvent> Generate<TSource>(IObservable<TSource> source)
        {
            return source.Select(_ => CreateEvent(Value));
        }

        /// <summary>
        /// Generates an observable sequence of MIDI control change events from a sequence of
        /// numeric values, emitting a new event whenever the mapped control value changes.
        /// </summary>
        /// <param name="source">A sequence of values to map to control values.</param>
        /// <returns>A sequence of <see cref="MidiEvent"/> objects.</returns>
        public IObservable<MidiEvent> Generate(IObservable<double> source)
        {
            return Observable.Defer(() =>
            {
                int? lastValue = null;
                return source
                    .Where(value => !double.IsNaN(value))
                    .Select(ToControlValue)
                    .Where(controlValue =>
                    {
                        if (controlValue == lastValue) return false;
                        lastValue = controlValue;
                        return true;
                    })
                    .Select(CreateEvent);
            });
        }

        /// <summary>
        /// Generates an observable sequence of MIDI control change events from a sequence of
        /// numeric values, emitting a new event whenever the mapped control value changes.
        /// </summary>
        /// <param name="source">A sequence of values to map to control values.</param>
        /// <returns>A sequence of <see cref="MidiEvent"/> objects.</returns>
        public IObservable<MidiEvent> Generate(IObservable<float> source)
        {
            return Generate(source.Select(value => (double)value));
        }

        /// <summary>
        /// Generates an observable sequence of MIDI control change events from a sequence of
        /// numeric values, emitting a new event whenever the mapped control value changes.
        /// </summary>
        /// <param name="source">A sequence of values to map to control values.</param>
        /// <returns>A sequence of <see cref="MidiEvent"/> objects.</returns>
        public IObservable<MidiEvent> Generate(IObservable<int> source)
        {
            return Generate(source.Select(value => (double)value));
        }

        int ToControlValue(double value)
        {
            var min = Min;
            var max = Max;
            if (min == max)
            {
                throw new InvalidOperationException("The minimum and maximum input values must be different.");
            }

            var normalized = Math.Max(0, Math.Min(1, (value - min) / (max - min)));
            return (int)Math.Round(normalized * MaxControlValue);
        }

        MidiEvent CreateEvent(int controlValue)
        {
            return new ControlChangeEvent(
                MidiValue.ToSevenBitNumber(ControlNumber, nameof(ControlNumber)),
                MidiValue.ToSevenBitNumber(controlValue, nameof(Value)))
            {
                Channel = MidiValue.ToFourBitNumber(Channel, nameof(Channel))
            };
        }
    }
}
