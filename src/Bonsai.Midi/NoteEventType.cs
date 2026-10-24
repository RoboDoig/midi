namespace Bonsai.Midi
{
    /// <summary>
    /// Specifies the type of a MIDI note event.
    /// </summary>
    public enum NoteEventType
    {
        /// <summary>
        /// A note on event, which starts a note.
        /// </summary>
        NoteOn,

        /// <summary>
        /// A note off event, which releases a note.
        /// </summary>
        NoteOff
    }
}
