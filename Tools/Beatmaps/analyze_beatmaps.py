import argparse, hashlib, json, subprocess, sys
from pathlib import Path
import numpy as np
import librosa
from scipy.io import wavfile
from scipy.ndimage import median_filter, maximum_filter1d
from scipy.signal import find_peaks

parser = argparse.ArgumentParser(description="Track existing music into a shared beat timeline (macOS afconvert).")
parser.add_argument("--project", type=Path, required=True)
parser.add_argument("--export", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--song", action="append", default=[])
args = parser.parse_args()
ROOT, WORK = args.project.resolve(), args.output.resolve()
WORK.mkdir(parents=True, exist_ok=True)
rows = json.loads(args.export.read_text())
results = []
for s in rows:
    if args.song and not any(q.lower() in s['title'].lower() for q in args.song):
        continue
    wav = WORK / 'analysis.wav'
    hop, n_fft = 128, 1024
    # Suppress sustained melody before finding rhythm. A spectrally whitened
    # positive flux responds to fresh attacks rather than amplitude alone.
    cache = WORK/(Path(s['clip']).stem+'.npz')
    digest = hashlib.sha256((ROOT / s['clip']).read_bytes()).hexdigest()
    saved = np.load(cache) if cache.exists() else None
    if saved is not None and 'sourceSha256' in saved and str(saved['sourceSha256']) == digest:
        y, sr, onset = saved['waveform'], int(saved['sr']), saved['onset']
    else:
        subprocess.run(['afconvert', str(ROOT / s['clip']), str(wav), '-f', 'WAVE', '-d', 'LEF32@22050', '-c', '1'], check=True, capture_output=True)
        sr, y = wavfile.read(wav)
        spec = np.abs(librosa.stft(y, n_fft=n_fft, hop_length=hop))
        _, percussive = librosa.decompose.hpss(spec, kernel_size=31, margin=(1, 2))
        logspec = librosa.amplitude_to_db(percussive + 1e-7, ref=np.max)
        onset = librosa.onset.onset_strength(S=logspec, sr=sr, hop_length=hop, n_fft=n_fft)
    tempo, frames = librosa.beat.beat_track(onset_envelope=onset, sr=sr, hop_length=hop, start_bpm=120, tightness=600, trim=False)
    times = librosa.frames_to_time(frames, sr=sr, hop_length=hop)
    # Spectral-window latency: refine to the strongest local time-domain
    # attack near the tracked pulse; no quarter-beat rounding of random peaks.
    ehop = 44
    energy = np.sqrt(np.mean(np.pad(y*y, (0, (-len(y)) % ehop)).reshape(-1, ehop), axis=1))
    attack = np.maximum(0, np.diff(energy, prepend=0))
    at = (np.arange(len(attack)) + .5) * ehop / sr
    peaks, _ = find_peaks(attack, distance=int(.04 * sr / ehop), prominence=max(.0005, np.percentile(attack, 80) * .5))
    pt, pv = at[peaks], attack[peaks]
    refined = []
    for t in times:
        near = np.flatnonzero((pt >= t-.035) & (pt <= t+.010))
        if len(near):
            score = pv[near] * np.exp(-.5*((pt[near]-t+.012)/.015)**2)
            t = pt[near[np.argmax(score)]]
        refined.append(float(t))
    times = np.array(refined)
    assert len(times) > 8 and np.all(np.diff(times) > .2), s['title']
    # At >140 BPM, use every second pulse for readable single-staff play.
    tempo = float(np.median(60/np.diff(times)))
    stride = 2 if tempo > 140 else 1
    notes = []
    lanes = [0, 1, 0, -1, 0, 1, 0, -1]
    energies = np.interp(times, at, energy)
    floor = max(.003, np.percentile(energies, 60) * .12)
    for i in range(6, len(times)-1, stride):
        if energies[i] < floor or times[i] >= s['duration']-.25:
            continue
        if notes and times[i] - times[int(notes[-1]['beat'])] < .40:
            continue
        notes.append(dict(beat=float(i), lane=lanes[len(notes) % len(lanes)], type=1 if i % 16 == 0 else 0,
                          warningBeats=2., strength=1., timingOffsetSeconds=0.))
    oldtimes = np.array([s['offset'] + n['beat'] * 60/s['bpm'] + n.get('timingOffsetSeconds', 0) for n in s['notes']])
    residual = np.min(abs(oldtimes[:, None]-times[None, :]), axis=1)
    out = dict(path=s['path'], title=s['title'], audioSha256=digest, bpm=round(tempo, 4), beatOffsetSeconds=round(float(times[0]), 6),
               travelBeats=4., beatTimesSeconds=np.round(times, 6).tolist(), notes=notes,
               audit=dict(oldBpm=s['bpm'], beatCount=len(times), noteCount=len(notes), stride=stride,
                          oldNotesOffPulseOver70ms=int(sum(residual > .07)), oldNoteCount=len(oldtimes),
                          medianInterval=float(np.median(np.diff(times))), auditoryReviewed=False))
    np.savez(WORK/(Path(s['clip']).stem+'.npz'), waveform=y, sr=sr, beats=times, old=oldtimes, onset=onset, hop=hop, sourceSha256=digest)
    results.append(out)
    print(s['title'], out['audit'], 'bpm', out['bpm'], 'first', np.round(times[:12], 3).tolist(), flush=True)
(WORK/'beat-charts.json').write_text(json.dumps(results, indent=2, ensure_ascii=False))
wav.unlink(missing_ok=True)
