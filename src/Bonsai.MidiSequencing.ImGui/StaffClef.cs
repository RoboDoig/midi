namespace Bonsai.MidiSequencing.ImGui
{
    /// <summary>
    /// Specifies the staves and clefs used to display musical notation.
    /// </summary>
    public enum StaffClef
    {
        /// <summary>
        /// Chooses a treble staff, a bass staff or a grand staff based on the range of the notes.
        /// </summary>
        Auto,

        /// <summary>
        /// Displays all notes on a single staff with a treble clef.
        /// </summary>
        Treble,

        /// <summary>
        /// Displays all notes on a single staff with a bass clef.
        /// </summary>
        Bass,

        /// <summary>
        /// Displays notes from middle C upwards on a treble staff, and notes below middle C
        /// on a bass staff.
        /// </summary>
        Grand
    }
}
