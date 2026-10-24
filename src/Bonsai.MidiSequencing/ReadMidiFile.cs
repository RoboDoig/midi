using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Melanchall.DryWetMidi.Core;

namespace Bonsai.MidiSequencing
{
    /// <summary>
    /// Represents an operator that reads a MIDI sequence from a standard MIDI file.
    /// </summary>
    [Description("Reads a MIDI sequence from a standard MIDI file.")]
    public class ReadMidiFile : Source<MidiFile>
    {
        /// <summary>
        /// Gets or sets the name of the MIDI file to read.
        /// </summary>
        [FileNameFilter("MIDI Files (*.mid;*.midi)|*.mid;*.midi|All Files|*.*")]
        [Editor("Bonsai.Design.OpenFileNameEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
        [Description("The name of the MIDI file to read.")]
        public string? FileName { get; set; }

        /// <summary>
        /// Generates an observable sequence containing the MIDI file read from disk.
        /// </summary>
        /// <returns>
        /// A sequence containing a single <see cref="MidiFile"/> object.
        /// </returns>
        public override IObservable<MidiFile> Generate()
        {
            return Observable.Defer(() =>
            {
                var fileName = FileName;
                if (string.IsNullOrEmpty(fileName))
                {
                    throw new InvalidOperationException("A valid MIDI file name must be specified.");
                }

                return Observable.Return(MidiFile.Read(fileName));
            });
        }
    }
}
