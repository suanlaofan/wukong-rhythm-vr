# Offline beat timeline and gameplay audio

The player consumes serialized beatTimesSeconds; Python is used only offline. Original music/audio files are retained.

1. In Unity, export song path, title, bpm, offset, travel, clip asset path, duration and notes (the WukongBeatmapGenerator library contains the assets).
2. Create an isolated Python 3.13 environment and install requirements.txt. macOS afconvert is required for decoding these existing MP3s.
3. Run `python analyze_beatmaps.py --project <Unity-project> --export <songs.json> --output <audit-directory>`.
4. In Unity (Edit Mode), invoke `WukongBeatTimelineImporter.Import(<beat-charts.json path>)` through Unity MCP. It validates every row before persisting.
5. Run WukongRhythmValidation and both Play Mode validators, then listen to the charts in the actual game. Imported charts remain locked and auditoryReviewed=false until human approval.

Analysis uses percussive spectral flux, dynamic programming with a continuity penalty (tightness 600), bounded transient refinement, integer-pulse notes and a minimum 400 ms gap. Songs above 140 BPM use every second pulse. The full beat timeline drives spawn (four beats), warning (two beats), hit targets and HUD. This handles gradual drift without moving each note onto an unrelated loud transient.

`prepare_gameplay_sfx.py --project <Unity-project> --work <scratch-directory>` creates short attack samples from the original audio and records source hashes and trim offsets. Unity imports the Gameplay WAVs as preloaded PCM and binds them in SampleScene and the scene builder.

Reference: https://github.com/librosa/librosa/blob/main/librosa/beat.py
