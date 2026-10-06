"""
しゃくとりの森の音をすべてプログラムで合成する（numpy のみ）。
  python tools/make_audio.py
出力: unity/Assets/Audio/*.wav
 - music_forest.wav   : 80BPM・ヘ長調のやさしいループ曲（パッド + カリンバ + チェレスタ + ベース）
 - ambience_forest.wav: 風・葉ずれ・小鳥のさえずり（つなぎ目のないループ）
 - 効果音: 足音、しずく、発見、クリック、糸、着地、クリア
"""
import os
import wave

import numpy as np

SR = 44100
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "unity", "Assets", "Audio")
rng = np.random.default_rng(20261006)


def write(name, data):
    data = np.asarray(data, dtype=np.float64)
    if data.ndim == 1:
        data = np.stack([data, data], axis=1)
    peak = np.max(np.abs(data)) + 1e-9
    data = data / peak * 0.89
    pcm = (np.clip(data, -1, 1) * 32767).astype(np.int16)
    path = os.path.join(OUT, name)
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print("wrote", name, f"{len(pcm) / SR:.1f}s")


def t_axis(sec):
    return np.arange(int(sec * SR)) / SR


def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12)


def shaped_noise(n, color=1.0, lo=20.0, hi=20000.0):
    """周波数領域で色付けしたノイズ（FFT 合成なのでそのままループする）"""
    spec = rng.normal(size=n // 2 + 1) + 1j * rng.normal(size=n // 2 + 1)
    f = np.fft.rfftfreq(n, 1 / SR)
    f[0] = 1.0
    mag = 1.0 / f ** (color / 2)
    mag *= 1 / (1 + (f / hi) ** 4)
    mag *= 1 / (1 + (lo / f) ** 4)
    x = np.fft.irfft(spec * mag, n)
    return x / (np.max(np.abs(x)) + 1e-9)


def circular_reverb(x, seconds=2.4, mix=0.3, damp=3.0):
    """循環畳み込みのリバーブ（ループのつなぎ目にも残響が回り込む）"""
    n = len(x)
    ir_len = int(seconds * SR)
    out = np.zeros_like(x)
    for ch in range(x.shape[1]):
        ir = rng.normal(size=ir_len) * np.exp(-np.linspace(0, damp * 2.5, ir_len))
        # 高域を少し落とす
        ir = np.convolve(ir, np.ones(8) / 8, mode="same")
        ir /= np.sqrt(np.sum(ir ** 2))
        irp = np.zeros(n)
        irp[: min(ir_len, n)] = ir[: min(ir_len, n)]
        wet = np.fft.irfft(np.fft.rfft(x[:, ch]) * np.fft.rfft(irp), n)
        out[:, ch] = x[:, ch] * (1 - mix) + wet * mix * 1.4
    return out


def env_adsr(n, a, d, s, r, total=None):
    total = total or n
    e = np.ones(total) * s
    ai = int(a * SR)
    di = int(d * SR)
    ri = int(r * SR)
    ai = max(ai, 1)
    e[:ai] = np.linspace(0, 1, ai)
    e[ai:ai + di] = np.linspace(1, s, len(e[ai:ai + di]))
    if ri > 0:
        e[-ri:] *= np.linspace(1, 0, ri)
    return e


def add(buf, start, sig, pan=0.0, gain=1.0, wrap=True):
    """ステレオバッファに信号を加える（循環）。pan: -1..1"""
    n = len(buf)
    l = np.cos((pan + 1) * np.pi / 4) * gain
    r = np.sin((pan + 1) * np.pi / 4) * gain
    idx = (np.arange(len(sig)) + start)
    if wrap:
        idx %= n
    else:
        m = idx < n
        idx = idx[m]
        sig = sig[: len(idx)]
    np.add.at(buf[:, 0], idx, sig * l)
    np.add.at(buf[:, 1], idx, sig * r)


# ---------------------------------------------------------------------------
# 楽器
# ---------------------------------------------------------------------------
def kalimba(freq, dur=1.6, bright=1.0):
    t = t_axis(dur)
    partials = [(1.0, 1.0, 2.2), (2.0, 0.12 * bright, 4.0), (5.4, 0.08 * bright, 9.0), (8.9, 0.03 * bright, 14.0)]
    s = np.zeros_like(t)
    for mul, amp, dec in partials:
        s += amp * np.sin(2 * np.pi * freq * mul * t + rng.uniform(0, 0.3)) * np.exp(-t * dec)
    att = np.minimum(t / 0.004, 1.0)
    click = rng.normal(size=len(t)) * np.exp(-t * 400) * 0.08
    return (s * att + click) * 0.6


def celesta(freq, dur=2.2):
    t = t_axis(dur)
    s = np.sin(2 * np.pi * freq * t) * np.exp(-t * 1.6)
    s += 0.25 * np.sin(2 * np.pi * freq * 2 * t) * np.exp(-t * 3.0)
    s += 0.12 * np.sin(2 * np.pi * freq * 3.98 * t) * np.exp(-t * 5.0)
    s += 0.05 * np.sin(2 * np.pi * freq * 6.1 * t) * np.exp(-t * 8.0)
    return s * np.minimum(t / 0.003, 1.0) * 0.5


def pad(freqs, dur):
    t = t_axis(dur)
    s = np.zeros_like(t)
    for f in freqs:
        for det in (-0.8, 0.0, 0.9):
            ff = f * 2 ** (det / 1200 * 6)
            s += np.sin(2 * np.pi * ff * t + rng.uniform(0, 6.28)) * 0.5
            s += 0.18 * np.sin(2 * np.pi * ff * 2 * t + rng.uniform(0, 6.28))
            s += 0.06 * np.sin(2 * np.pi * ff * 3 * t + rng.uniform(0, 6.28))
    e = env_adsr(len(t), 0.8, 0.5, 0.85, 1.2)
    trem = 1 + 0.06 * np.sin(2 * np.pi * 0.35 * t)
    return s * e * trem / (len(freqs) * 3)


def bass(freq, dur):
    t = t_axis(dur)
    s = np.sin(2 * np.pi * freq * t) + 0.25 * np.sin(2 * np.pi * freq * 2 * t)
    return s * env_adsr(len(t), 0.02, 0.4, 0.6, 0.4) * 0.5


def flute(freq, dur):
    t = t_axis(dur)
    vib = 1 + 0.006 * np.sin(2 * np.pi * 5.0 * t) * np.minimum(t / 0.4, 1)
    ph = 2 * np.pi * freq * np.cumsum(vib) / SR
    s = np.sin(ph) + 0.18 * np.sin(2 * ph) + 0.05 * np.sin(3 * ph)
    breath = shaped_noise(len(t), 0.0, 1500, 6000) * 0.03
    e = env_adsr(len(t), 0.08, 0.2, 0.8, min(0.35, dur * 0.4))
    return (s + breath) * e * 0.35


# ---------------------------------------------------------------------------
# BGM
# ---------------------------------------------------------------------------
NOTE = {"C": 0, "C#": 1, "Db": 1, "D": 2, "Eb": 3, "E": 4, "F": 5, "F#": 6, "Gb": 6, "G": 7, "Ab": 8, "A": 9, "Bb": 10, "B": 11}


def chord_notes(name):
    minor = name.endswith("m")
    root = NOTE[name[:-1] if minor else name]
    third = 3 if minor else 4
    return root, [root, root + third, root + 7]


def make_music():
    bpm = 80
    beat = 60 / bpm
    bar = beat * 4
    prog = ["F", "C", "Dm", "Bb", "F", "C", "Bb", "C",
            "Dm", "Am", "Bb", "F", "Gm", "C", "F", "F",
            "Bb", "C", "Am", "Dm", "Gm", "Bb", "C", "C"]
    total = bar * len(prog)
    n = int(total * SR)
    buf = np.zeros((n, 2))
    pent = [0, 2, 4, 7, 9]  # ヘ長調ペンタトニック（F G A C D）
    key = 65  # F4
    mel_prev = 72

    for i, ch in enumerate(prog):
        root, tones = chord_notes(ch)
        start = int(i * bar * SR)
        # パッド
        freqs = [midi(48 + t + 12) for t in tones]
        add(buf, start, pad(freqs, bar + 1.0), pan=0.0, gain=0.55)
        # ベース
        broot = 36 + root if root < 6 else 24 + root
        add(buf, start, bass(midi(broot + 12), beat * 1.8), gain=0.5)
        add(buf, start + int(beat * 2 * SR), bass(midi(broot + 12), beat * 1.8), gain=0.38)
        # カリンバのアルペジオ（8分音符）
        arp = [tones[0], tones[1], tones[2], tones[1] + 12, tones[2], tones[1], tones[0] + 12, tones[2]]
        if i % 4 == 3:
            arp = [tones[0], tones[2], tones[1] + 12, tones[2] + 12, tones[0] + 24, tones[2] + 12, tones[1] + 12, tones[2]]
        for k, note in enumerate(arp):
            if rng.random() < 0.12 and k not in (0, 4):
                continue
            pos = start + int(k * beat / 2 * SR) + int(rng.normal(0, 0.004) * SR)
            vel = 0.55 if k % 2 == 0 else 0.4
            add(buf, pos, kalimba(midi(60 + note), 1.6), pan=-0.35 + 0.1 * (k % 3), gain=vel)
        # チェレスタのメロディ（ところどころ）
        section = i // 8
        if section >= 1:
            steps = [0, 1.5, 2, 3] if i % 2 == 0 else [0, 1, 2.5]
            for s_ in steps:
                cands = [key + 12 + p + o for p in pent for o in (-12, 0, 12) if 67 <= key + 12 + p + o <= 91]
                # 和音の音を優先して近い音へ
                weights = []
                for c in cands:
                    w = 1.0 / (1 + abs(c - mel_prev) ** 1.3)
                    if (c - root) % 12 in [t % 12 for t in tones]:
                        w *= 2.5
                    weights.append(w)
                weights = np.array(weights) / np.sum(weights)
                note = int(rng.choice(cands, p=weights))
                mel_prev = note
                pos = start + int(s_ * beat * SR)
                add(buf, pos, celesta(midi(note), 2.2), pan=0.3, gain=0.42)
        # フルート（後半の盛り上がり）
        if 16 <= i < 22 and i % 2 == 0:
            note = key + 12 + [7, 9, 12, 9, 7, 4][(i - 16)] if (i - 16) < 6 else key + 12
            add(buf, start + int(beat * SR), flute(midi(note), beat * 2.8), pan=0.15, gain=0.5)

    buf = circular_reverb(buf, seconds=2.8, mix=0.32)
    write("music_forest.wav", buf)


# ---------------------------------------------------------------------------
# 環境音
# ---------------------------------------------------------------------------
def bird_chirp(f0, f1, dur, vib=0.0, harm=0.15):
    t = t_axis(dur)
    k = t / dur
    f = f0 + (f1 - f0) * (np.sin(k * np.pi / 2) ** 2)
    f = f * (1 + vib * np.sin(2 * np.pi * 38 * t))
    ph = 2 * np.pi * np.cumsum(f) / SR
    s = np.sin(ph) + harm * np.sin(2 * ph)
    e = np.sin(np.pi * k) ** 1.5
    return s * e


def bird_song(kind):
    parts = []
    if kind == 0:  # ちゅんちゅん
        for _ in range(rng.integers(3, 6)):
            parts.append(bird_chirp(rng.uniform(3200, 3800), rng.uniform(4200, 5200), rng.uniform(0.05, 0.09)))
            parts.append(np.zeros(int(rng.uniform(0.05, 0.12) * SR)))
    elif kind == 1:  # ひーよ
        parts.append(bird_chirp(2600, 3600, 0.25, vib=0.01))
        parts.append(np.zeros(int(0.08 * SR)))
        parts.append(bird_chirp(3500, 2500, 0.3, vib=0.01))
    elif kind == 2:  # トリル
        for _ in range(rng.integers(8, 14)):
            parts.append(bird_chirp(4200, 5000, 0.035, harm=0.05))
            parts.append(np.zeros(int(0.018 * SR)))
    else:  # 遠くのホーホー
        parts.append(bird_chirp(720, 680, 0.35, harm=0.05))
        parts.append(np.zeros(int(0.25 * SR)))
        parts.append(bird_chirp(700, 660, 0.5, harm=0.05))
    return np.concatenate(parts)


def make_ambience():
    total = 48.0
    n = int(total * SR)
    buf = np.zeros((n, 2))
    t = np.arange(n) / SR
    # 風（ゆっくりうねる）
    for ch, seed_shift in ((0, 0.0), (1, 1.3)):
        w = shaped_noise(n, 1.6, 40, 700)
        lfo = 0.55 + 0.25 * np.sin(2 * np.pi * t * (3 / total) + seed_shift) + 0.2 * np.sin(2 * np.pi * t * (7 / total) + 2 * seed_shift)
        buf[:, ch] += w * lfo * 0.5
    # 葉ずれ
    rustle = shaped_noise(n, 0.5, 1800, 7000)
    gust = np.clip(np.sin(2 * np.pi * t * (5 / total)) * 0.6 + np.sin(2 * np.pi * t * (11 / total) + 1) * 0.4, 0, 1) ** 2
    buf[:, 0] += rustle * gust * 0.12
    buf[:, 1] += np.roll(rustle, SR // 3) * gust * 0.12
    # 小鳥
    birds = np.zeros((n, 2))
    time = 0.5
    while time < total - 0.5:
        kind = rng.choice([0, 0, 1, 2, 3], p=[0.3, 0.2, 0.25, 0.15, 0.1])
        song = bird_song(kind)
        gain = rng.uniform(0.15, 0.5) * (0.5 if kind == 3 else 1.0)
        add(birds, int(time * SR), song, pan=rng.uniform(-0.8, 0.8), gain=gain)
        time += rng.uniform(1.2, 4.0)
    birds = circular_reverb(birds, seconds=1.6, mix=0.35)
    buf += birds * 0.6
    write("ambience_forest.wav", buf)


# ---------------------------------------------------------------------------
# 効果音
# ---------------------------------------------------------------------------
def fade_tail(x, sec=0.02):
    k = int(sec * SR)
    x[-k:] *= np.linspace(1, 0, k)
    return x


def make_sfx():
    # 足音（ふわっと小さく）
    for i in range(3):
        t = t_axis(0.09)
        thump = np.sin(2 * np.pi * (220 + i * 25) * t * (1 - t * 3)) * np.exp(-t * 60)
        tick = shaped_noise(len(t), 0.3, 1500 + i * 400, 6000) * np.exp(-t * 120) * 0.35
        write(f"step_{i + 1}.wav", fade_tail(thump * 0.5 + tick))

    # しずくを取った（きらめくアルペジオ）
    total = 1.6
    buf = np.zeros((int(total * SR), 2))
    for k, note in enumerate([84, 88, 91, 96]):
        add(buf, int(k * 0.065 * SR), celesta(midi(note), 1.4), pan=-0.3 + k * 0.2, gain=0.7, wrap=False)
    t = t_axis(total)
    shimmer = shaped_noise(len(t), 0.0, 6000, 14000) * np.exp(-t * 4) * 0.05
    buf[:, 0] += shimmer
    buf[:, 1] += np.roll(shimmer, 200)
    # 水の「ぽちゃん」
    tp = t_axis(0.25)
    plop = np.sin(2 * np.pi * (600 + 1400 * tp * 4) * tp) * np.exp(-tp * 25) * 0.4
    add(buf, 0, plop, gain=0.6, wrap=False)
    write("collect.wav", buf)

    # 発見（ハープのグリッサンド + 和音）
    total = 4.0
    buf = np.zeros((int(total * SR), 2))
    scale = [65, 67, 69, 72, 74, 77, 79, 81, 84, 86, 89]
    for k, note in enumerate(scale):
        add(buf, int(k * 0.055 * SR), kalimba(midi(note), 2.5, bright=0.7), pan=-0.6 + k * 0.12, gain=0.55, wrap=False)
    chord_t = int(0.7 * SR)
    add(buf, chord_t, pad([midi(65), midi(69), midi(72), midi(77)], 3.0), gain=1.4, wrap=False)
    for k, note in enumerate([77, 81, 84]):
        add(buf, chord_t + int(k * 0.02 * SR), celesta(midi(note + 12), 2.5), pan=0.2, gain=0.5, wrap=False)
    buf = circular_reverb(buf, seconds=1.8, mix=0.3)
    buf[-int(0.3 * SR):] *= np.linspace(1, 0, int(0.3 * SR))[:, None]
    write("discover.wav", buf)

    # クリック（木の実をつつくような音）
    t = t_axis(0.08)
    click = np.sin(2 * np.pi * 1400 * t) * np.exp(-t * 90) + 0.4 * np.sin(2 * np.pi * 2300 * t) * np.exp(-t * 140)
    click += shaped_noise(len(t), 0.0, 2000, 8000) * np.exp(-t * 300) * 0.3
    write("click.wav", fade_tail(click * 0.6))

    # 糸（しゅるる…）
    t = t_axis(0.8)
    k = t / 0.8
    sweep = np.sin(2 * np.pi * np.cumsum(2400 - 1600 * k) / SR) * np.exp(-t * 3) * 0.15
    swish = shaped_noise(len(t), 0.2, 2500, 9000) * np.sin(np.pi * k) ** 2 * 0.5
    write("silk.wav", fade_tail(sweep + swish))

    # 着地（ぽふっ）
    t = t_axis(0.35)
    thud = np.sin(2 * np.pi * 140 * t * (1 - t)) * np.exp(-t * 18)
    leaves = shaped_noise(len(t), 0.4, 1200, 6000) * np.exp(-t * 14) * 0.3
    write("land.wav", fade_tail(thud * 0.6 + leaves))

    # クリア
    total = 6.0
    buf = np.zeros((int(total * SR), 2))
    seq = [(0.0, [65, 69, 72]), (0.6, [67, 71, 74]), (1.2, [69, 72, 77]), (2.0, [65, 69, 72, 77, 81])]
    for start, notes in seq:
        for j, nte in enumerate(notes):
            add(buf, int((start + j * 0.04) * SR), kalimba(midi(nte), 2.5), pan=-0.4 + j * 0.2, gain=0.5, wrap=False)
            add(buf, int((start + j * 0.04) * SR), celesta(midi(nte + 12), 2.5), pan=0.4 - j * 0.2, gain=0.3, wrap=False)
    add(buf, int(2.0 * SR), pad([midi(53), midi(65), midi(69), midi(72)], 4.0), gain=1.2, wrap=False)
    buf = circular_reverb(buf, seconds=2.2, mix=0.3)
    buf[-int(0.5 * SR):] *= np.linspace(1, 0, int(0.5 * SR))[:, None]
    write("complete.wav", buf)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    make_sfx()
    make_ambience()
    make_music()
