"""
しゃくとりの森の音をすべてプログラムで合成する（numpy のみ）。
  python tools/make_audio.py
出力: unity/Assets/Audio/*.wav
 - music_forest.wav   : 80BPM・ヘ長調のやさしいループ曲（パッド + カリンバ + チェレスタ + ベース）
 - music_river.wav    : 72BPM・6/8 拍子・ニ長調の流れるループ曲（ハープのアルペジオ + 水のしずくのチェレスタ + 木の笛）
 - music_park.wav     : 104BPM・ハ長調の、はずむループ曲（ウクレレ + 鉄琴のメロディ + はじくベース + シェイカー + 口笛）
 - ambience_forest.wav: 風・葉ずれ・小鳥のさえずり（つなぎ目のないループ）
 - ambience_river.wav : せせらぎ・遠くの滝・カエル（川辺）
 - 効果音: 足音、しずく、発見、クリック、糸、着地、クリア、いきもの発見、エリア移動、きせかえ解放、カラス
 - music_mountain.wav  : 66BPM・ト長調の、ひろびろとしたループ曲（角笛のメロディ + ハープ + 高いチェレスタ）
 - ambience_mountain.wav: 高い所の風・遠くのウグイス・ナキウサギ。山の湧き水・尾根の風のループ、ヒグラシ・ナキウサギの声
 - エリアの音: 公園の環境音、滝・浅瀬・カエルの合唱・ほら穴のしずく・森の葉ずれ（場所で聞こえるループ）、
   キツツキ・どんぐり・ホコリタケ・ブランコ・シーソー・水飲み場・自転車のベル・魚・金属・砂の音
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


def harp(freq, dur=2.4):
    """ハープ（はじいた弦）：倍音ほど早く消え、少しだけ音程がずれた倍音でやわらかく"""
    t = t_axis(dur)
    s = np.zeros_like(t)
    for k in range(1, 7):
        f = freq * k * (1 + 0.0004 * k * k)
        s += (1.0 / k ** 1.3) * np.sin(2 * np.pi * f * t + rng.uniform(0, 0.4)) * np.exp(-t * (1.1 + 0.9 * k))
    att = np.minimum(t / 0.006, 1.0)
    return s * att * 0.45


def ukulele(freq, dur=0.9):
    """ウクレレ（ナイロン弦を短くはじく）：明るく、すぐ消える"""
    t = t_axis(dur)
    s = np.zeros_like(t)
    for k in range(1, 6):
        s += (0.9 / k ** 1.1) * np.sin(2 * np.pi * freq * k * t + rng.uniform(0, 0.3)) * np.exp(-t * (3.5 + 2.2 * k))
    att = np.minimum(t / 0.003, 1.0)
    return s * att * 0.4


def glock(freq, dur=1.6):
    """鉄琴：高く澄んだ音（チェレスタより明るい）"""
    t = t_axis(dur)
    s = np.sin(2 * np.pi * freq * t) * np.exp(-t * 2.2)
    s += 0.35 * np.sin(2 * np.pi * freq * 2.76 * t) * np.exp(-t * 5.0)
    s += 0.15 * np.sin(2 * np.pi * freq * 5.4 * t) * np.exp(-t * 9.0)
    return s * np.minimum(t / 0.002, 1.0) * 0.42


def pizz_bass(freq, dur=0.5):
    """はじくベース（短く、はずむ）"""
    t = t_axis(dur)
    s = np.sin(2 * np.pi * freq * t) + 0.3 * np.sin(2 * np.pi * freq * 2 * t)
    return s * np.exp(-t * 7.0) * np.minimum(t / 0.004, 1.0) * 0.6


def shaker(dur=0.09, accent=1.0):
    """シェイカー（高い音のさらさら）"""
    n = int(dur * SR)
    t = np.arange(n) / SR
    env = np.minimum(t / 0.01, 1.0) * np.exp(-t * 45)
    return shaped_noise(n, 0.0, 5000, 12000) * env * 0.25 * accent


def whistle(freq, dur):
    """口笛（すんだ正弦波に、ゆれと息）"""
    t = t_axis(dur)
    vib = 1 + 0.01 * np.sin(2 * np.pi * 5.5 * t) * np.minimum(t / 0.3, 1)
    ph = 2 * np.pi * freq * np.cumsum(vib) / SR
    s = np.sin(ph) + 0.04 * np.sin(2 * ph)
    breath = shaped_noise(len(t), 0.0, 2500, 8000) * 0.02
    e = env_adsr(len(t), 0.05, 0.15, 0.85, min(0.25, dur * 0.4))
    return (s + breath) * e * 0.3


def pick_melody(cands, prev, tones, root, weights_rng):
    """和音の音を優先して、前の音に近い音をえらぶ"""
    w = []
    for c in cands:
        x = 1.0 / (1 + abs(c - prev) ** 1.3)
        if (c - root) % 12 in [t % 12 for t in tones]:
            x *= 2.5
        w.append(x)
    w = np.array(w) / np.sum(w)
    return int(weights_rng.choice(cands, p=w))


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


def make_music_river():
    """川辺：6/8 拍子でゆったり流れる。ハープのアルペジオが水の流れ、高いチェレスタが水のしずく、木の笛が歌う"""
    r = np.random.default_rng(72)
    bpm = 72                      # 付点 4 分音符 = 72
    eighth = 60 / bpm / 3
    bar = eighth * 6
    prog = ["D", "Bm", "G", "A", "D", "Em", "G", "A",
            "Bm", "G", "D", "A", "G", "D", "Em", "A",
            "G", "A", "F#m", "Bm", "Em", "G", "A", "A",
            "D", "Bm", "G", "A", "G", "A", "D", "D"]
    total = bar * len(prog)
    buf = np.zeros((int(total * SR), 2))
    pent = [0, 2, 4, 7, 9]   # ニ長調ペンタトニック（D E F# A B）
    key = 62                 # D4
    mel_prev = 74
    for i, ch in enumerate(prog):
        root, tones = chord_notes(ch)
        start = int(i * bar * SR)
        add(buf, start, pad([midi(50 + t + 12) for t in tones], bar + 1.2), gain=0.42)
        broot = 38 + ((root - 2) % 12)
        add(buf, start, bass(midi(broot), bar * 0.9), gain=0.42)
        # ハープ：上って下りるアルペジオ（流れ）
        up = [tones[0], tones[1], tones[2], tones[0] + 12, tones[1] + 12, tones[2] + 12]
        arp = up if i % 2 == 0 else list(reversed(up))
        for k, note in enumerate(arp):
            pos = start + int(k * eighth * SR) + int(r.normal(0, 0.003) * SR)
            add(buf, pos, harp(midi(55 + note), 2.4), pan=-0.4 + 0.16 * k, gain=0.5 if k == 0 else 0.36)
        # 水のしずく：高いチェレスタが、ときどき 2 つ続けて落ちる
        if r.random() < 0.7:
            k = int(r.integers(1, 6))
            n1 = key + 24 + pent[int(r.integers(0, 5))]
            add(buf, start + int(k * eighth * SR), celesta(midi(n1), 1.6), pan=r.uniform(-0.6, 0.6), gain=0.22)
            add(buf, start + int((k + 0.5) * eighth * SR), celesta(midi(n1 + 5), 1.4), pan=r.uniform(-0.6, 0.6), gain=0.15)
        # 木の笛のメロディ（2 小節目から、2 小節で 1 フレーズ）
        if i % 8 >= 1 and i % 2 == 1:
            cands = [key + 12 + p + o for p in pent for o in (-12, 0, 12) if 66 <= key + 12 + p + o <= 88]
            for b_ in (0, 3):
                note = pick_melody(cands, mel_prev, tones, root, r)
                mel_prev = note
                add(buf, start + int(b_ * eighth * SR), flute(midi(note), eighth * 2.8), pan=0.2, gain=0.42)
    buf = circular_reverb(buf, seconds=3.2, mix=0.36)
    write("music_river.wav", buf)


def make_music_park():
    """公園：ひだまりの、はずむ曲。ウクレレのきざみ・はじくベース・シェイカーに、鉄琴と口笛のメロディ"""
    r = np.random.default_rng(104)
    bpm = 104
    beat = 60 / bpm
    bar = beat * 4
    prog = ["C", "G", "Am", "F", "C", "G", "F", "G",
            "Am", "Em", "F", "C", "Dm", "G", "C", "C",
            "F", "G", "Em", "Am", "Dm", "G", "C", "G"]
    total = bar * len(prog)
    buf = np.zeros((int(total * SR), 2))
    pent = [0, 2, 4, 7, 9]   # ハ長調ペンタトニック
    key = 60
    mel_prev = 76
    for i, ch in enumerate(prog):
        root, tones = chord_notes(ch)
        start = int(i * bar * SR)
        add(buf, start, pad([midi(48 + t + 12) for t in tones], bar + 0.8), gain=0.25)
        # はじくベース：1 拍目と 3 拍目、あいだに 5 度
        broot = 36 + root
        for b_, n_ in ((0, broot), (1.5, broot + 7), (2, broot), (3.5, broot + 7)):
            add(buf, start + int(b_ * beat * SR), pizz_bass(midi(n_ + 12), 0.45), gain=0.5 if b_ in (0, 2) else 0.32)
        # ウクレレ：裏拍のきざみ（ジャカジャン）
        for b_ in (0.5, 1, 1.5, 2.5, 3, 3.5):
            pos = start + int(b_ * beat * SR)
            strum = [tones[0] + 12, tones[1] + 12, tones[2] + 12, tones[0] + 24]
            for j, note in enumerate(strum):
                add(buf, pos + int(j * 0.012 * SR), ukulele(midi(48 + note), 0.6), pan=-0.3, gain=0.22 if b_ % 1 else 0.3)
        # シェイカー（8 分音符、表拍が強い）
        for k in range(8):
            add(buf, start + int(k * beat / 2 * SR), shaker(0.09, 1.0 if k % 2 == 1 else 0.6), pan=0.35, gain=0.6)
        # 鉄琴のメロディ（はずむリズム）
        if i >= 2:
            rhythm = [0, 0.75, 1.5, 2, 3] if i % 2 == 0 else [0, 1, 1.5, 2.5]
            cands = [key + 12 + p + o for p in pent for o in (0, 12) if 72 <= key + 12 + p + o <= 93]
            for b_ in rhythm:
                if r.random() < 0.12:
                    continue
                note = pick_melody(cands, mel_prev, tones, root, r)
                mel_prev = note
                add(buf, start + int(b_ * beat * SR), glock(midi(note), 1.4), pan=0.25, gain=0.36)
        # 口笛（後半、2 小節ごと）
        if 16 <= i < 23 and i % 2 == 0:
            phrase = [7, 9, 12, 9]
            for j, p in enumerate(phrase):
                add(buf, start + int(j * beat * SR), whistle(midi(key + 12 + p), beat * 0.95), pan=0.1, gain=0.38)
    buf = circular_reverb(buf, seconds=2.2, mix=0.26)
    write("music_park.wav", buf)


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


# ---------------------------------------------------------------------------
# 川辺（追加分）
# ---------------------------------------------------------------------------
def bubble(f0, dur):
    """せせらぎの泡（音程が上がる短い音）"""
    t = t_axis(dur)
    f = f0 * (1 + 2.2 * t / dur)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-t * (5.0 / dur)) * np.sin(np.pi * np.minimum(t / 0.004, 1) / 2)


def make_river_ambience():
    total = 48.0
    n = int(total * SR)
    buf = np.zeros((n, 2))
    t = np.arange(n) / SR
    # 遠くの滝（低いざーっという音）
    for ch in range(2):
        fall = shaped_noise(n, 1.2, 80, 2400)
        buf[:, ch] += fall * (0.32 + 0.04 * np.sin(2 * np.pi * t * (2 / total) + ch))
    # 水面のさらさら
    hiss = shaped_noise(n, 0.4, 900, 6000)
    flow = 0.6 + 0.4 * np.sin(2 * np.pi * t * (5 / total)) ** 2
    buf[:, 0] += hiss * flow * 0.12
    buf[:, 1] += np.roll(hiss, SR // 5) * flow * 0.12
    # ちょろちょろ（泡の粒）
    bub = np.zeros((n, 2))
    count = int(total * 26)
    for _ in range(count):
        start = int(rng.uniform(0, total) * SR)
        b = bubble(rng.uniform(350, 1300), rng.uniform(0.012, 0.045))
        add(bub, start, b, pan=rng.uniform(-0.7, 0.7), gain=rng.uniform(0.05, 0.22))
    bub = circular_reverb(bub, seconds=0.8, mix=0.25)
    buf += bub
    # ときどき小鳥とカエル
    time = 2.0
    while time < total - 1.0:
        if rng.uniform() < 0.6:
            song = bird_song(int(rng.choice([0, 2])))
            add(buf, int(time * SR), song, pan=rng.uniform(-0.8, 0.8), gain=rng.uniform(0.05, 0.14))
        else:
            for k in range(int(rng.integers(2, 4))):
                tk = t_axis(0.09)
                kero = np.sign(np.sin(2 * np.pi * 180 * tk)) * np.sin(2 * np.pi * 22 * tk) ** 2 * np.exp(-tk * 18)
                kero = np.convolve(kero, np.ones(12) / 12, mode="same")
                add(buf, int((time + k * 0.16) * SR), kero, pan=rng.uniform(-0.6, 0.6), gain=0.12)
        time += rng.uniform(3.0, 7.0)
    write("ambience_river.wav", buf)


def crow_caw(dur=0.36, f0=560.0):
    """カラスの「かあ」：倍音の多い声 + かすれ"""
    t = t_axis(dur)
    k = t / dur
    f = f0 * (1.05 - 0.18 * k) * (1 + 0.012 * np.sin(2 * np.pi * 31 * t))
    ph = 2 * np.pi * np.cumsum(f) / SR
    s = np.zeros_like(t)
    for h in range(1, 14):
        fh = f0 * h
        formant = np.exp(-((fh - 1250) / 520) ** 2) + 0.6 * np.exp(-((fh - 2500) / 700) ** 2) + 0.25
        s += np.sin(h * ph) * formant / h ** 0.6
    rasp = shaped_noise(len(t), 0.2, 900, 3500) * 0.35
    env = np.sin(np.pi * np.minimum(k / 0.12, 1) / 2) * np.exp(-np.maximum(k - 0.35, 0) * 4)
    return (s / 6 + rasp) * env


def make_motion_sfx():
    # 落ちる（ひゅるる…と下がる笛のような音 + 風）
    total = 0.9
    t = t_axis(total)
    k = t / total
    whistle = np.sin(2 * np.pi * np.cumsum(1500 - 900 * k) / SR) * np.exp(-k * 1.5) * 0.25
    wind = shaped_noise(len(t), 0.6, 600, 4000) * np.sin(np.pi * k) * 0.5
    write("fall.wav", fade_tail(whistle + wind))

    # 水に落ちる（ぽちゃん + しぶき）
    total = 1.1
    t = t_axis(total)
    plop = np.sin(2 * np.pi * np.cumsum(260 + 900 * np.exp(-t * 18)) / SR) * np.exp(-t * 9) * 0.8
    spray = shaped_noise(len(t), 0.2, 1500, 9000) * np.exp(-t * 6) * 0.35
    bub = np.zeros_like(t)
    for _ in range(14):
        st = int(rng.uniform(0.05, 0.6) * SR)
        b = bubble(rng.uniform(500, 1400), rng.uniform(0.02, 0.05))
        bub[st:st + len(b)] += b[: len(bub) - st] * rng.uniform(0.1, 0.25)
    write("splash.wav", fade_tail(plop + spray + bub))

    # レアないきもの（きらきらのファンファーレ）
    total = 3.2
    buf = np.zeros((int(total * SR), 2))
    for k2, note in enumerate([72, 76, 79, 84, 88, 91, 96]):
        add(buf, int(k2 * 0.06 * SR), celesta(midi(note), 1.8), pan=-0.6 + k2 * 0.2, gain=0.55, wrap=False)
    for j, note in enumerate([60, 64, 67, 72]):
        add(buf, int((0.5 + j * 0.03) * SR), kalimba(midi(note), 2.4, bright=0.9), pan=0.1 * j, gain=0.5, wrap=False)
    t = t_axis(total)
    shimmer = shaped_noise(len(t), 0.0, 7000, 15000) * np.exp(-t * 1.8) * 0.08
    buf[:, 0] += shimmer
    buf[:, 1] += np.roll(shimmer, 180)
    buf = circular_reverb(buf, seconds=1.8, mix=0.3)
    buf[-int(0.4 * SR):] *= np.linspace(1, 0, int(0.4 * SR))[:, None]
    write("rare.wav", buf)


def make_creature_calls():
    # スズメの「ちゅん、ちゅん」
    total = 0.9
    buf = np.zeros((int(total * SR), 2))
    t0 = 0.0
    for _ in range(3):
        add(buf, int(t0 * SR), bird_chirp(rng.uniform(3300, 3700), rng.uniform(4300, 5000), rng.uniform(0.06, 0.08)), pan=0.1, gain=0.8, wrap=False)
        t0 += rng.uniform(0.16, 0.24)
    buf = circular_reverb(buf, seconds=0.6, mix=0.2)
    buf[-int(0.1 * SR):] *= np.linspace(1, 0, int(0.1 * SR))[:, None]
    write("chirp.wav", buf)

    # アマガエルの「けろけろ」
    total = 1.0
    buf = np.zeros((int(total * SR), 2))
    for k in range(4):
        tk = t_axis(0.11)
        kero = np.sign(np.sin(2 * np.pi * 190 * tk)) * np.sin(2 * np.pi * 18 * tk) ** 2 * np.exp(-tk * 14)
        kero = np.convolve(kero, np.ones(10) / 10, mode="same")
        add(buf, int(k * 0.19 * SR), kero, pan=0.0, gain=0.7, wrap=False)
    buf = circular_reverb(buf, seconds=0.5, mix=0.18)
    buf[-int(0.1 * SR):] *= np.linspace(1, 0, int(0.1 * SR))[:, None]
    write("croak.wav", buf)


def make_area_sfx():
    # いきものを見つけた（ぴこん♪）
    total = 1.8
    buf = np.zeros((int(total * SR), 2))
    for k, note in enumerate([79, 84, 91]):
        add(buf, int(k * 0.09 * SR), kalimba(midi(note), 1.4, bright=1.2), pan=-0.2 + k * 0.2, gain=0.7, wrap=False)
    add(buf, int(0.27 * SR), celesta(midi(96), 1.3), pan=0.3, gain=0.4, wrap=False)
    buf = circular_reverb(buf, seconds=1.0, mix=0.22)
    buf[-int(0.2 * SR):] *= np.linspace(1, 0, int(0.2 * SR))[:, None]
    write("creature.wav", buf)

    # エリア移動（木の根のトンネルをくぐる：ふわっと吸い込まれる音）
    total = 2.2
    t = t_axis(total)
    k = t / total
    whoosh = shaped_noise(len(t), 0.8, 200, 5000)
    sweep = np.sin(2 * np.pi * np.cumsum(220 + 900 * k ** 2) / SR) * 0.15
    env = np.sin(np.pi * k) ** 2
    buf = np.zeros((len(t), 2))
    buf[:, 0] = (whoosh * 0.6 + sweep) * env
    buf[:, 1] = (np.roll(whoosh, 300) * 0.6 + sweep) * env
    for j, note in enumerate([72, 76, 79, 84]):
        add(buf, int((0.9 + j * 0.08) * SR), celesta(midi(note), 1.2), pan=-0.3 + j * 0.2, gain=0.35, wrap=False)
    buf = circular_reverb(buf, seconds=1.4, mix=0.3)
    buf[-int(0.3 * SR):] *= np.linspace(1, 0, int(0.3 * SR))[:, None]
    write("travel.wav", buf)

    # きせかえ解放（きらきら）
    total = 2.4
    buf = np.zeros((int(total * SR), 2))
    for k, note in enumerate([77, 81, 84, 89, 93]):
        add(buf, int(k * 0.07 * SR), celesta(midi(note), 1.6), pan=-0.5 + k * 0.25, gain=0.6, wrap=False)
    t = t_axis(total)
    shimmer = shaped_noise(len(t), 0.0, 7000, 15000) * np.exp(-t * 2.5) * 0.06
    buf[:, 0] += shimmer
    buf[:, 1] += np.roll(shimmer, 150)
    buf = circular_reverb(buf, seconds=1.5, mix=0.28)
    buf[-int(0.3 * SR):] *= np.linspace(1, 0, int(0.3 * SR))[:, None]
    write("unlock.wav", buf)

    # カラス（かあ、かあ）
    total = 1.4
    buf = np.zeros((int(total * SR), 2))
    add(buf, 0, crow_caw(0.36, 560), pan=0.1, gain=0.8, wrap=False)
    add(buf, int(0.5 * SR), crow_caw(0.4, 530), pan=0.1, gain=0.75, wrap=False)
    buf = circular_reverb(buf, seconds=1.2, mix=0.25)
    buf[-int(0.15 * SR):] *= np.linspace(1, 0, int(0.15 * SR))[:, None]
    write("caw.wav", buf)


# ---------------------------------------------------------------------------
# エリアの音（場所で聞こえるループと、できごとの音）
# ---------------------------------------------------------------------------
def plink(f0, dur=0.35):
    """水のしずくが落ちる「ぽちゃ」（音程が上がって消える）"""
    t = t_axis(dur)
    f = f0 * (1 + 0.9 * (1 - np.exp(-t * 40)))
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-t * 14) * np.minimum(t / 0.002, 1)


def wood_knock(f0=900.0, dur=0.08):
    t = t_axis(dur)
    s = np.sin(2 * np.pi * f0 * t) + 0.5 * np.sin(2 * np.pi * f0 * 2.7 * t)
    click = shaped_noise(len(t), 0.0, 1500, 8000) * 0.5
    return (s * 0.6 + click) * np.exp(-t * 60)


def frog_call(f0, n=3, gap=0.17):
    out = []
    for k in range(n):
        tk = t_axis(0.1)
        kero = np.sign(np.sin(2 * np.pi * f0 * tk)) * np.sin(2 * np.pi * 20 * tk) ** 2 * np.exp(-tk * 15)
        out.append(np.convolve(kero, np.ones(10) / 10, mode="same"))
        out.append(np.zeros(int(gap * SR) - len(tk)))
    return np.concatenate(out)


def make_area_sounds():
    # 公園の環境音：そよ風・スズメ・遠くの自転車のベル・木の葉
    total = 40.0
    n = int(total * SR)
    t = np.arange(n) / SR
    buf = np.zeros((n, 2))
    for ch in range(2):
        w = shaped_noise(n, 1.4, 60, 900)
        buf[:, ch] += w * (0.4 + 0.15 * np.sin(2 * np.pi * t * (3 / total) + ch)) * 0.45
    leaves = shaped_noise(n, 0.4, 2000, 7000)
    buf[:, 0] += leaves * 0.05
    buf[:, 1] += np.roll(leaves, SR // 4) * 0.05
    birds = np.zeros((n, 2))
    time = 0.4
    while time < total - 0.4:
        add(birds, int(time * SR), bird_song(int(rng.choice([0, 0, 2]))), pan=rng.uniform(-0.8, 0.8), gain=rng.uniform(0.12, 0.35))
        time += rng.uniform(1.0, 3.2)
    for bt in (9.0, 27.5):
        tb = t_axis(1.2)
        bell = (np.sin(2 * np.pi * 2350 * tb) + 0.6 * np.sin(2 * np.pi * 3180 * tb)) * np.exp(-tb * 4) * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 9 * tb)))
        add(birds, int(bt * SR), bell, pan=rng.uniform(-0.6, 0.6), gain=0.05)
    buf += circular_reverb(birds, seconds=1.4, mix=0.3) * 0.7
    write("ambience_park.wav", buf)

    # 滝のごうごう（近くで聞こえるループ）
    total = 12.0
    n = int(total * SR)
    t = np.arange(n) / SR
    buf = np.zeros((n, 2))
    for ch in range(2):
        roar = shaped_noise(n, 1.0, 50, 5000)
        buf[:, ch] += roar * (0.75 + 0.1 * np.sin(2 * np.pi * t * (3 / total) + ch * 1.7))
    hiss = shaped_noise(n, 0.2, 3000, 12000)
    buf[:, 0] += hiss * 0.18
    buf[:, 1] += np.roll(hiss, 900) * 0.18
    write("loop_waterfall.wav", buf)

    # 浅瀬のさらさら（とびいしの瀬）
    total = 10.0
    n = int(total * SR)
    buf = np.zeros((n, 2))
    for _ in range(int(total * 60)):
        add(buf, int(rng.uniform(0, total) * SR), bubble(rng.uniform(500, 1800), rng.uniform(0.01, 0.035)), pan=rng.uniform(-0.8, 0.8), gain=rng.uniform(0.05, 0.25))
    hiss = shaped_noise(n, 0.5, 1200, 7000)
    buf[:, 0] += hiss * 0.1
    buf[:, 1] += np.roll(hiss, 400) * 0.1
    write("loop_shallows.wav", circular_reverb(buf, seconds=0.5, mix=0.2))

    # カエルの合唱（水たまり・よどみ）
    total = 16.0
    n = int(total * SR)
    buf = np.zeros((n, 2))
    for k in range(5):
        f0 = 150 + k * 22
        time = rng.uniform(0, 3)
        pan = -0.8 + k * 0.4
        while time < total:
            add(buf, int(time * SR), frog_call(f0, int(rng.integers(2, 5))), pan=pan, gain=rng.uniform(0.25, 0.5))
            time += rng.uniform(1.6, 3.6)
    write("loop_frogs.wav", circular_reverb(buf, seconds=1.2, mix=0.3))

    # ほら穴のしずく（ぽちゃん、と遠くでひびく）
    total = 12.0
    n = int(total * SR)
    buf = np.zeros((n, 2))
    time = 0.3
    while time < total:
        add(buf, int(time * SR), plink(rng.uniform(900, 1700)), pan=rng.uniform(-0.6, 0.6), gain=rng.uniform(0.3, 0.7))
        time += rng.uniform(0.6, 2.2)
    write("loop_cave_drip.wav", circular_reverb(buf, seconds=2.6, mix=0.5, damp=1.6))

    # 森の葉ずれ（風が強いと大きくする）
    total = 16.0
    n = int(total * SR)
    t = np.arange(n) / SR
    buf = np.zeros((n, 2))
    for ch in range(2):
        r = shaped_noise(n, 0.6, 900, 8000)
        flutter = 0.6 + 0.4 * np.abs(np.sin(2 * np.pi * t * (13 / total) + ch))
        buf[:, ch] += r * flutter
    write("loop_canopy.wav", buf)

    # キツツキ（遠くで、とととと…）
    total = 1.6
    buf = np.zeros((int(total * SR), 2))
    for k in range(14):
        add(buf, int(k * 0.055 * SR), wood_knock(720 + rng.uniform(-20, 20)), pan=0.3, gain=1.0 - k * 0.05, wrap=False)
    buf = circular_reverb(buf, seconds=1.4, mix=0.4)
    buf[-int(0.2 * SR):] *= np.linspace(1, 0, int(0.2 * SR))[:, None]
    write("woodpecker.wav", buf)

    # どんぐりが落ちる（ころん）
    total = 0.7
    buf = np.zeros((int(total * SR), 2))
    for k, (dt, g) in enumerate(((0.0, 1.0), (0.18, 0.55), (0.3, 0.3), (0.37, 0.15))):
        add(buf, int(dt * SR), wood_knock(1300 - k * 60, 0.06), gain=g, wrap=False)
    buf[-int(0.02 * SR):] *= np.linspace(1, 0, int(0.02 * SR))[:, None]
    write("knock_acorn.wav", buf)

    # ホコリタケ（ぽふっ）
    tq = t_axis(0.5)
    puff = shaped_noise(len(tq), 1.4, 80, 1800) * np.exp(-tq * 9) * np.minimum(tq / 0.01, 1)
    thump = np.sin(2 * np.pi * 110 * tq) * np.exp(-tq * 25) * 0.6
    write("puff.wav", fade_tail(puff + thump))

    # ブランコのきしみ（きいっ）
    tq = t_axis(0.6)
    f = 1500 + 500 * np.sin(np.pi * tq / 0.6)
    ph = 2 * np.pi * np.cumsum(f) / SR
    squeak = (np.sin(ph) + 0.4 * np.sin(2 * ph) + 0.2 * np.sin(3.01 * ph)) * np.sin(np.pi * tq / 0.6) ** 2
    squeak *= 0.7 + 0.3 * np.sin(2 * np.pi * 37 * tq)
    write("creak.wav", fade_tail(squeak))

    # シーソーが地面に当たる（ごとん）
    tq = t_axis(0.45)
    clunk = (np.sin(2 * np.pi * 95 * tq) + 0.5 * np.sin(2 * np.pi * 190 * tq)) * np.exp(-tq * 14)
    clunk += shaped_noise(len(tq), 1.0, 100, 2500) * np.exp(-tq * 40) * 0.5
    write("clunk.wav", fade_tail(clunk))

    # 水飲み場のしずく（ぽちゃ）
    write("plink.wav", fade_tail(plink(1250.0, 0.4)))

    # 自転車のベル（ちりん）
    tq = t_axis(1.0)
    bell = (np.sin(2 * np.pi * 2350 * tq) + 0.6 * np.sin(2 * np.pi * 3180 * tq) + 0.3 * np.sin(2 * np.pi * 5100 * tq)) * np.exp(-tq * 4.5)
    write("bell.wav", fade_tail(bell))

    # 魚がはねる（ぱしゃっ）
    tq = t_axis(0.5)
    sp = shaped_noise(len(tq), 0.3, 500, 9000) * np.exp(-tq * 18)
    drop = np.zeros_like(tq)
    for k in range(5):
        st = int(rng.uniform(0.08, 0.3) * SR)
        b = bubble(rng.uniform(600, 1400), 0.03)
        drop[st:st + len(b)] += b[: len(drop) - st] * 0.4
    write("fish_jump.wav", fade_tail(sp + drop))

    # 金属をふむ（ちん。ジャングルジム・すべり台）
    tq = t_axis(0.35)
    ting = (np.sin(2 * np.pi * 1850 * tq) + 0.5 * np.sin(2 * np.pi * 4630 * tq)) * np.exp(-tq * 16)
    write("ting.wav", fade_tail(ting))

    # 砂をふむ（しゃり）
    tq = t_axis(0.18)
    sand = shaped_noise(len(tq), 0.1, 2500, 11000) * np.exp(-tq * 22) * np.minimum(tq / 0.004, 1)
    write("sand_step.wav", fade_tail(sand))


# ---------------------------------------------------------------------------
# 山：曲・環境音・湧き水・ヒグラシ・ナキウサギ
# ---------------------------------------------------------------------------
def alphorn(freq, dur):
    """やわらかい角笛（山にひびく、息の多い低めの管の音）"""
    t = t_axis(dur)
    s = np.zeros_like(t)
    for k, g in ((1, 1.0), (2, 0.45), (3, 0.22), (4, 0.1)):
        s += g * np.sin(2 * np.pi * freq * k * t * (1 + 0.003 * np.sin(2 * np.pi * 4.5 * t)))
    breath = shaped_noise(len(t), 0.5, 300, 3000) * 0.04
    e = env_adsr(len(t), 0.12, 0.2, 0.8, min(0.5, dur * 0.4))
    return (s + breath) * e * 0.25


def make_music_mountain():
    """山：ひろびろとした、ゆったりの曲（66BPM・ト長調）。角笛のメロディ・ハープ・高いチェレスタの星"""
    r = np.random.default_rng(66)
    bpm = 66
    beat = 60 / bpm
    bar = beat * 4
    prog = ["G", "D", "Em", "C", "G", "C", "D", "D",
            "Em", "C", "G", "D", "C", "G", "Am", "D"]
    total = bar * len(prog)
    buf = np.zeros((int(total * SR), 2))
    pent = [0, 2, 4, 7, 9]   # ト長調ペンタトニック
    key = 67
    mel_prev = 74
    for i, ch in enumerate(prog):
        root, tones = chord_notes(ch)
        start = int(i * bar * SR)
        add(buf, start, pad([midi(48 + t + 12) for t in tones] + [midi(36 + root)], bar + 1.2), gain=0.32)
        add(buf, start, bass(midi(36 + root), bar * 0.9), gain=0.35)
        # ハープ：ゆっくり上がっていくアルペジオ
        arp = [tones[0], tones[1], tones[2], tones[0] + 12, tones[1] + 12, tones[2] + 12]
        for j, note in enumerate(arp):
            add(buf, start + int(j * beat * 0.66 * SR), harp(midi(55 + note), 2.6), pan=-0.35 + 0.12 * j, gain=0.22)
        # 角笛のメロディ（2 小節に 1 つのフレーズ）
        if i % 2 == 0 and i >= 2:
            cands = [key + p + o for p in pent for o in (-12, 0) if 60 <= key + p + o <= 79]
            for b_ in (0, 1.5, 2.5):
                note = pick_melody(cands, mel_prev, tones, root, r)
                mel_prev = note
                add(buf, start + int(b_ * beat * SR), alphorn(midi(note), beat * (1.4 if b_ else 1.6)), pan=0.15, gain=0.5)
        # 高いチェレスタ：雲の上の星のように、ぽつりぽつり
        for b_ in (1, 3):
            if r.random() < 0.55:
                note = 84 + pent[int(r.integers(0, 5))] + 7
                add(buf, start + int(b_ * beat * SR), celesta(midi(note), 2.4), pan=r.uniform(-0.6, 0.6), gain=0.16)
    buf = circular_reverb(buf, seconds=3.8, mix=0.42)
    write("music_mountain.wav", buf)


def higurashi_call(dur=3.2):
    """ヒグラシの「カナカナカナ…」：高い声が、ふるえながら、だんだん小さく低くなる"""
    t = t_axis(dur)
    pulse = 0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 7.5 * t))   # カ・ナ・カ・ナ
    pulse = np.convolve(pulse, np.ones(400) / 400, mode="same")
    f = 4400 * (1 - 0.06 * t / dur)
    ph = 2 * np.pi * np.cumsum(f) / SR
    tone = np.sin(ph) + 0.4 * np.sin(2 * ph) + 0.25 * np.sin(ph * 1.5)
    buzz = 0.5 + 0.5 * np.sin(2 * np.pi * 180 * t)
    env = np.minimum(t / 0.15, 1.0) * np.exp(-t * 0.55)
    return tone * buzz * pulse * env * 0.5


def pika_call():
    """ナキウサギの「ピチッ」：とても短い、高い声"""
    t = t_axis(0.12)
    f = 5200 - 2400 * (t / 0.12)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return (np.sin(ph) + 0.2 * np.sin(2 * ph)) * np.sin(np.pi * t / 0.12) ** 1.5


def make_mountain_sounds():
    make_music_mountain()
    # 山の環境音：高い所の風（ひゅうひゅう）・遠くのウグイスとホシガラス・遠くのナキウサギ
    total = 44.0
    n = int(total * SR)
    t = np.arange(n) / SR
    buf = np.zeros((n, 2))
    for ch in range(2):
        w = shaped_noise(n, 1.8, 50, 900)
        lfo = 0.5 + 0.3 * np.sin(2 * np.pi * t * (4 / total) + ch * 1.4) + 0.2 * np.sin(2 * np.pi * t * (9 / total) + ch)
        buf[:, ch] += w * lfo * 0.55
        whoosh = shaped_noise(n, 0.8, 500, 2400)
        buf[:, ch] += whoosh * np.clip(np.sin(2 * np.pi * t * (5 / total) + ch), 0, 1) ** 3 * 0.12
    birds = np.zeros((n, 2))
    time = 1.0
    while time < total - 2.0:
        if rng.random() < 0.5:
            # ウグイス「ホー…ホケキョ」
            song = np.concatenate([bird_chirp(1250, 1300, 0.7, harm=0.05), np.zeros(int(0.12 * SR)),
                                   bird_chirp(2200, 2600, 0.12), bird_chirp(2400, 1800, 0.18), bird_chirp(2000, 2100, 0.25)])
            add(birds, int(time * SR), song, pan=rng.uniform(-0.8, 0.8), gain=rng.uniform(0.18, 0.32))
        else:
            add(birds, int(time * SR), pika_call(), pan=rng.uniform(-0.8, 0.8), gain=rng.uniform(0.06, 0.12))
        time += rng.uniform(3.0, 7.0)
    buf += circular_reverb(birds, seconds=2.4, mix=0.45) * 0.7
    write("ambience_mountain.wav", buf)

    # 湧き水（こぽこぽ、ちょろちょろ）
    total = 10.0
    n = int(total * SR)
    buf = np.zeros((n, 2))
    for _ in range(int(total * 35)):
        add(buf, int(rng.uniform(0, total) * SR), bubble(rng.uniform(380, 1300), rng.uniform(0.015, 0.05)), pan=rng.uniform(-0.6, 0.6), gain=rng.uniform(0.08, 0.3))
    trickle = shaped_noise(n, 0.4, 1500, 8000)
    buf[:, 0] += trickle * 0.12
    buf[:, 1] += np.roll(trickle, 700) * 0.12
    write("loop_spring.wav", circular_reverb(buf, seconds=0.8, mix=0.25))

    # 尾根の風（高い所ほど大きく鳴らす）
    total = 14.0
    n = int(total * SR)
    t = np.arange(n) / SR
    buf = np.zeros((n, 2))
    for ch in range(2):
        w = shaped_noise(n, 1.2, 120, 2500)
        buf[:, ch] += w * (0.6 + 0.4 * np.sin(2 * np.pi * t * (3 / total) + ch * 2.1))
    write("loop_ridge_wind.wav", buf)

    # ヒグラシ・ナキウサギ
    buf = np.zeros((int(3.6 * SR), 2))
    add(buf, 0, higurashi_call(3.4), pan=0.0, gain=0.8, wrap=False)
    buf = circular_reverb(buf, seconds=1.2, mix=0.3)
    buf[-int(0.2 * SR):] *= np.linspace(1, 0, int(0.2 * SR))[:, None]
    write("higurashi.wav", buf)
    buf = np.zeros((int(0.5 * SR), 2))
    add(buf, 0, pika_call(), pan=0.0, gain=0.8, wrap=False)
    add(buf, int(0.2 * SR), pika_call() * 0.5, pan=0.0, gain=0.8, wrap=False)
    buf = circular_reverb(buf, seconds=0.4, mix=0.25)
    buf[-int(0.05 * SR):] *= np.linspace(1, 0, int(0.05 * SR))[:, None]
    write("pika.wav", buf)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    make_sfx()
    make_ambience()
    make_music()
    make_river_ambience()
    make_area_sfx()
    make_motion_sfx()
    make_creature_calls()
    make_music_river()
    make_music_park()
    make_area_sounds()
    make_mountain_sounds()
