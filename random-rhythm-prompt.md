I would like to generate an operator in the MidiSequencing library that generates randomised rhythmic sequences. Either immediately, or in response to an input trigger (input observable sequence) the operator should generate a random rhythm that can then be played as with other sequences in Bonsai.MidiSequencing.

## Properties
The operator should have the following properties:
- measures per bar (i.e. the '6' in 6/8 time or the '2' in 2/4 time)
- measure length (i.e. the '8' in 6/8 time or the '4' in 2/4 time)
- minChanges: integer specifying the minimum number of changes that should be attempted on the sequence
- maxXhanges: integer specifying the maximum number of changes that should be attempted on the sequence
- pitch: the pitch note of the rhythm

## Method
The process for generating a random rhythm is as follows:
Initiation: 
- The rhythm begins as a note covering the entire length of the bar, e.g. in 4/4 it would begin with a full semibreve.
- The number of changes is chosen at random from between minChanges:maxChanges


Then:
A note within the bar is chosen at random, the note can be either deleted or split. If deletion results in no notes left in the bar the process ends. If the note is split it is divided into two equal notes (e.g. semibreve --> 2 minims). If the length of the note is unequal (e.g. 5 measure note in 5/4) it splits into the next lowest subdivision but adds a dot to one of the notes (e.g. dotted minim + minim or minim + dotted minim). 

This continues until the number of changes chosen has been satisfied (or no notes are left in the bar).

An example in 4/4 with 4 changes:
. = Note initiation
_ = Hold note
- = rest

1. |._______|
2. |.___.___|
3. |._._.___|
4. |._--.___| 
5. |._--._._|