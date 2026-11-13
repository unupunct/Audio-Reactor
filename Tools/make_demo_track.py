"""Generates an original synthetic demo track (120 BPM) for testing/screenshots. No copyrighted material."""
import math, random, struct, sys, wave

SR = 44100
BPM = 120
BEAT = 60 / BPM
BARS = 16
N = int(SR * BEAT * 4 * BARS)
random.seed(7)
L = [0.0] * N
R = [0.0] * N

def add(buf, start, sig, gain=1.0):
    for i, v in enumerate(sig):
        j = start + i
        if j >= N: break
        buf[j] += v * gain

def kick():
    n = int(SR * 0.30); out = []; ph = 0.0
    for i in range(n):
        t = i / SR; f = 45 + 110 * math.exp(-t * 28); ph += 2 * math.pi * f / SR
        out.append(math.sin(ph) * math.exp(-t * 9))
    return out

def snare():
    n = int(SR * 0.20); return [(random.uniform(-1, 1) * 0.7 + math.sin(2 * math.pi * 190 * i / SR) * 0.4) * math.exp(-i / SR * 22) for i in range(n)]

def hat():
    n = int(SR * 0.05); prev = 0; out = []
    for i in range(n):
        x = random.uniform(-1, 1); out.append((x - prev) * math.exp(-i / SR * 70)); prev = x
    return out

def tone(f, dur, wave_fn=math.sin, decay=3.0):
    n = int(SR * dur); return [wave_fn(2 * math.pi * f * i / SR) * math.exp(-i / SR * decay) for i in range(n)]

K, S, H = kick(), snare(), hat()
bass_notes = [55.0, 55.0, 65.41, 49.0]  # A1 A1 C2 G1
chords = [[220.0, 261.63, 329.63], [220.0, 261.63, 329.63], [261.63, 329.63, 392.0], [196.0, 246.94, 392.0]]
for bar in range(BARS):
    for beat in range(4):
        t0 = int(SR * BEAT * (bar * 4 + beat))
        if bar >= 1:
            add(L, t0, K, 0.9); add(R, t0, K, 0.9)
        if bar >= 2 and beat in (1, 3):
            add(L, t0, S, 0.55); add(R, t0, S, 0.55)
        if bar >= 3:
            for sub in range(2):
                ts = t0 + int(SR * BEAT * sub / 2)
                add(L, ts, H, 0.35 if sub else 0.2); add(R, ts, H, 0.3 if sub else 0.25)
        if bar >= 1:
            bn = tone(bass_notes[bar % 4], BEAT * 0.95, lambda x: math.sin(x) + 0.4 * math.sin(2 * x), 2.0)
            add(L, t0, bn, 0.35); add(R, t0, bn, 0.35)
        if bar >= 4:
            for k, f in enumerate(chords[bar % 4]):
                ts = t0 + int(SR * BEAT * k / 3)
                arp = tone(f * 2, BEAT * 0.8, lambda x: math.sin(x) + 0.25 * math.sin(3 * x), 4.0)
                add(L, ts, arp, 0.12 if k % 2 else 0.18); add(R, ts, arp, 0.18 if k % 2 else 0.12)

peak = max(max(abs(x) for x in L), max(abs(x) for x in R)) or 1
out = sys.argv[1] if len(sys.argv) > 1 else "demo.wav"
with wave.open(out, "wb") as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
    frames = bytearray()
    for l, r in zip(L, R):
        frames += struct.pack("<hh", int(max(-1, min(1, l / peak * 0.9)) * 32767), int(max(-1, min(1, r / peak * 0.9)) * 32767))
    w.writeframes(bytes(frames))
print("wrote", out, N / SR, "s")
