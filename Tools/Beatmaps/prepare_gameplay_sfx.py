import argparse, hashlib, json, subprocess
from pathlib import Path
import numpy as np
from scipy.io import wavfile

parser=argparse.ArgumentParser()
parser.add_argument("--project",type=Path,required=True)
parser.add_argument("--work",type=Path,required=True)
args=parser.parse_args()
args.work.mkdir(parents=True,exist_ok=True)
root=args.project/'Assets/WukongRhythmGame/Audio'
output=root/'Gameplay'
output.mkdir(exist_ok=True)
report=[]
for original, name, length, peak in [
    ('Staff_Impact.mp3','Staff_Impact_Short.wav',.30,.85),
    ('Rock_Shatter.mp3','Rock_Shatter_Short.wav',.45,.65),
    ('Fire_Impact.mp3','Fire_Impact_Short.wav',.45,.6),
    ('Staff_Whoosh.wav','Staff_Whoosh_Short.wav',.25,.55)]:
    decoded=args.work/'sfx-decoded.wav'
    subprocess.run(['afconvert',str(root/original),str(decoded),'-f','WAVE','-d','LEF32@44100','-c','1'],check=True,capture_output=True)
    sr,y=wavfile.read(decoded)
    start=max(0,int(np.flatnonzero(np.abs(y)>max(.02,np.abs(y).max()*.1))[0]) - int(.003*sr))
    segment=y[start:start+int(length*sr)].copy()
    segment*=peak/max(float(np.max(np.abs(segment))),1e-6)
    fade=int(.012*sr)
    segment[-fade:]*=np.linspace(1,0,fade)
    segment[:min(44,len(segment))]*=np.linspace(0,1,min(44,len(segment)))
    wavfile.write(output/name,sr,(np.clip(segment,-1,1)*32767).astype(np.int16))
    report.append(dict(source=original,sourceSha256=hashlib.sha256((root/original).read_bytes()).hexdigest(),asset=name,startSeconds=start/sr,duration=len(segment)/sr,peak=float(abs(segment).max())))
decoded.unlink()
(args.work/'sfx-audit.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
