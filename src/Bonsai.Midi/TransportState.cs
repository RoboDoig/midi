using System;

namespace Bonsai.Midi
{
    /// <summary>
    /// Represents the musical position of a transport at a specific clock tick.
    /// </summary>
    public readonly struct TransportState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TransportState"/> structure.
        /// </summary>
        /// <param name="time">The elapsed clock time.</param>
        /// <param name="beat">The musical position, in beats.</param>
        /// <param name="tempo">The current tempo, in beats per minute.</param>
        public TransportState(TimeSpan time, double beat, double tempo)
        {
            Time = time;
            Beat = beat;
            Tempo = tempo;
        }

        /// <summary>
        /// Gets the elapsed clock time.
        /// </summary>
        public TimeSpan Time { get; }

        /// <summary>
        /// Gets the musical position, in beats, where each beat is a quarter note.
        /// </summary>
        public double Beat { get; }

        /// <summary>
        /// Gets the current tempo, in beats per minute.
        /// </summary>
        public double Tempo { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{{Time = {Time}, Beat = {Beat:F3}, Tempo = {Tempo}}}";
        }
    }
}
