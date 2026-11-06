// Now Playing visualizer: animated bars in the album art's colors.
// Spotify's audio can't be analyzed in the browser (DRM, and the audio-analysis
// API is closed to new apps), so by default the motion is generated: layered
// sine "noise", a steady kick at a tempo picked from the track id, and slow
// swells. It rises while playing and settles when paused.
// Mic mode instead listens through the microphone and draws what it hears.
// The audio is only measured in the browser, never recorded or sent anywhere.
//
// Styles: bars along the bottom, a ring around the album cover, flowing waves,
// particles bursting from the cover, a tunnel of squares flying outwards, bars
// mirrored top and bottom, or the cover itself shattering on the beat.
// The synced lyrics' timestamps give the one piece of real song structure
// available: lines that repeat are treated as the chorus, and the visualizer gets
// bigger and brighter there, with a flash as each chorus begins.

// Spotify green, violet, hot pink
const FALLBACK = [[29, 185, 84], [127, 90, 240], [255, 64, 160]];

const STYLES = ["bars", "ring", "wave", "particles", "tunnel", "mirror", "shatter"];

let canvas, ctx, resizeObserver, frame;
let playing = false;
let level = 0;              // 0 paused … 1 playing, eased
let bpm = 118;
let bars = [];
let ringBars = [];
let style = "bars";
let colors = FALLBACK.map(c => [...c]);
let targetColors = FALLBACK;
let imageUrl;
let lastNow = 0;
let huePhase = 0;           // hue wave position; advances faster in a chorus

// Shared levels (bass in the middle, treble at the ends) and beat, for the styles
// that don't keep bars of their own
let bands = makeBars(32);
let beatPrev = 0;
let bassAvg = 0;
let lastBeatAt = 0;
let chorusHit = false;      // a chorus just started

// Per-style state
let particles = [];
let tunnelRings = [];
let shards = [];
let shatterPush = 0;
let coverImg;               // the cover, for the shatter style

// Song position, counted on from Spotify's last report
let positionMs = 0;
let positionSyncedAt = 0;   // Date.now() of that report

// Lyric timing: [{ time (ms), chorus (bool) }]
let lines = [];
let lineIndex = -1;
let chorus = 0;             // 0 verse … 1 chorus, eased
let flash = 0;              // burst when a line or chorus starts, fades out

// Mic mode
let mic;                    // { stream, audio, analyser, bins }
let micPeak = 0.2;          // running loudness peak, so quiet and loud rooms both fill the bars

const reducedMotion = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

export function start(el, options) {
    stop();

    canvas = el;
    style = getStyle();
    ctx = canvas.getContext("2d");

    resizeObserver = new ResizeObserver(resize);
    resizeObserver.observe(canvas);
    resize();

    update(options);
    frame = requestAnimationFrame(draw);
}

// options: { imageUrl, playing, trackId, positionMs, syncedAtMs }
export function update(options) {
    playing = !!options.playing;

    if (options.syncedAtMs) {
        positionMs = options.positionMs ?? 0;
        positionSyncedAt = options.syncedAtMs;
    }

    if (options.trackId)
        bpm = 90 + (hash(options.trackId) % 50); // 90–139, stable per track

    if (options.imageUrl && options.imageUrl !== imageUrl) {
        imageUrl = options.imageUrl;
        loadPalette(imageUrl).then(p => { if (imageUrl === options.imageUrl) targetColors = p; });

        coverImg = new Image();
        coverImg.crossOrigin = "anonymous"; // same request as the palette's, so it's cached
        coverImg.src = imageUrl;
    }
}

export function stop() {
    cancelAnimationFrame(frame);
    resizeObserver?.disconnect();
    canvas = ctx = resizeObserver = undefined;
    stopMic();
    setCover(null);
}

// One of STYLES; remembered per browser
export function getStyle() {
    try {
        const saved = localStorage.getItem("visualizer-style");
        return STYLES.includes(saved) ? saved : "bars";
    } catch {
        return "bars";
    }
}

export function setStyle(name) {
    style = STYLES.includes(name) ? name : "bars";
    try { localStorage.setItem("visualizer-style", style); } catch { }

    particles = [];
    tunnelRings = [];
    shards = [];
    setCover(null);
}

// The current song's timed lyric lines: [{ time (ms), text }]. A line counts as
// chorus when its text repeats in the song and so does a line next to it, which
// skips one-off repeats like a single echoed phrase in a verse.
export function setLyrics(timed) {
    // Compare words only: ignore case, punctuation and ad-libs like "(oh)"
    const norm = s => (s ?? "").toLowerCase().replace(/\([^)]*\)/g, "").replace(/[^\p{L}\p{N} ]/gu, "").replace(/\s+/g, " ").trim();
    const keys = (timed ?? []).map(l => norm(l.text));

    const counts = new Map();
    keys.forEach(k => { if (k) counts.set(k, (counts.get(k) ?? 0) + 1); });
    const repeats = keys.map(k => !!k && counts.get(k) > 1);

    lines = (timed ?? []).map((l, i) => ({
        time: l.time,
        chorus: repeats[i] && (repeats[i - 1] || repeats[i + 1])
    }));
    lineIndex = -1;
}

// Turns mic mode on or off. Returns "on", "off", "denied" (permission refused)
// or "unavailable" (no mic, or the page isn't on https/localhost).
export async function setMic(on) {
    if (!on) {
        stopMic();
        return "off";
    }

    if (mic) return "on";
    if (!navigator.mediaDevices?.getUserMedia) return "unavailable";

    try {
        // Processing meant for voice calls would flatten the music, so turn it off
        const stream = await navigator.mediaDevices.getUserMedia({
            audio: { echoCancellation: false, noiseSuppression: false, autoGainControl: false }
        });

        const audio = new AudioContext();
        const analyser = audio.createAnalyser();
        analyser.fftSize = 1024;
        analyser.smoothingTimeConstant = 0.6;
        audio.createMediaStreamSource(stream).connect(analyser);

        mic = { stream, audio, analyser, bins: new Uint8Array(analyser.frequencyBinCount) };
        micPeak = 0.2;
        return "on";
    } catch (e) {
        return e?.name === "NotAllowedError" ? "denied" : "unavailable";
    }
}

function stopMic() {
    if (!mic) return;

    // Stopping the tracks releases the mic, so the browser's recording indicator goes away
    mic.stream.getTracks().forEach(track => track.stop());
    mic.audio.close();
    mic = undefined;
}

// Remembers on/off choices per browser; storage can be unavailable (private mode)
export function isEnabled(name = "visualizer") {
    try { return localStorage.getItem(name) === "on"; } catch { return false; }
}

export function setEnabled(on, name = "visualizer") {
    try { localStorage.setItem(name, on ? "on" : "off"); } catch { }
}

function resize() {
    if (!canvas) return;

    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(canvas.clientWidth * dpr);
    canvas.height = Math.round(canvas.clientHeight * dpr);

    // About one bar per 18 CSS px
    bars = makeBars(Math.max(24, Math.min(96, Math.round(canvas.clientWidth / 18))));
}

// Bars each with their own random wobble
function makeBars(count) {
    return Array.from({ length: count }, () => ({
        value: 0,
        f: [0.6 + Math.random() * 0.9, 1.7 + Math.random() * 1.6, 3.5 + Math.random() * 3],
        p: [Math.random() * 6.28, Math.random() * 6.28, Math.random() * 6.28]
    }));
}

// Styles the album cover: { scale } pulses it, { hidden } fades it out (shatter draws
// it in pieces instead); null puts it back. Inline styles: Blazor doesn't render a style
// attribute on the cover, so it won't overwrite them. The transform transition is
// dropped so the pulse isn't smoothed away.
function setCover(state) {
    const art = document.querySelector(".np-art");
    if (!art) return;

    const transform = state?.scale ? `scale(${state.scale.toFixed(4)})` : "";
    const opacity = state?.hidden ? "0" : "";
    const transition = state ? "opacity 0.4s ease" : "";

    if (art.style.transform !== transform) art.style.transform = transform;
    if (art.style.opacity !== opacity) art.style.opacity = opacity;
    if (art.style.transition !== transition) art.style.transition = transition;
}

// Moves to the lyric line at the current position; a new line gives a small flash,
// the first line of a chorus a big one
function followLyrics() {
    if (lines.length === 0) {
        chorus += (0 - chorus) * 0.03;
        return;
    }

    const elapsed = playing ? Date.now() - positionSyncedAt : 0;
    const now = positionMs + elapsed + 300; // same 300ms lead as the lyrics panel

    let index = -1;
    for (let i = 0; i < lines.length && lines[i].time <= now; i++) index = i;

    if (index !== lineIndex) {
        const line = lines[index];
        const wasChorus = lines[lineIndex]?.chorus;
        if (line && playing) {
            const starts = line.chorus && !wasChorus;
            flash = Math.max(flash, starts ? 1 : 0.25);
            if (starts) chorusHit = true;
        }
        lineIndex = index;
    }

    const target = playing && lines[lineIndex]?.chorus ? 1 : 0;
    chorus += (target - chorus) * 0.03;
}

function draw(now) {
    frame = requestAnimationFrame(draw);
    if (!ctx || bars.length === 0) return;

    const t = now / 1000;
    const dt = Math.min(0.1, lastNow ? (now - lastNow) / 1000 : 0);
    lastNow = now;
    const calm = reducedMotion();
    const speed = calm ? 0.35 : 1;

    level += ((playing ? 1 : 0) - level) * 0.04;

    followLyrics();
    flash *= 0.93;
    if (calm) flash = 0;

    // Chorus: everything bigger, brighter and quicker; a flash as each line lands
    const boost = 1 + 0.35 * chorus + 0.25 * flash;
    huePhase += dt * speed * 0.4 * (1 + 1.5 * chorus);

    // Ease colors toward the current album's palette
    for (let i = 0; i < 3; i++)
        for (let c = 0; c < 3; c++)
            colors[i][c] += (targetColors[i][c] - colors[i][c]) * 0.03;

    // Kick: a sharp pulse on each beat, harder in a chorus. Swell: slow 16-second build and release.
    const beat = (t * bpm / 60) % 1;
    const kick = calm ? 0 : Math.pow(1 - beat, 6) * (1 + 0.6 * chorus);
    const swell = 0.7 + 0.3 * Math.sin(t * 2 * Math.PI / 16);

    const dpr = window.devicePixelRatio || 1;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.globalCompositeOperation = "source-over";
    ctx.globalAlpha = 1;
    ctx.shadowBlur = 22 * dpr * (1 + 0.6 * chorus + 0.5 * flash);

    if (mic) {
        mic.analyser.getByteFrequencyData(mic.bins);
        // Let the peak decay slowly so the bars rescale when the room gets quieter
        micPeak = Math.max(micPeak * 0.997, 0.08);
    }

    const fs = { t, dt, speed, kick, swell, boost, calm, dpr, palette: colors.map(rgbToHsl) };

    // Visible area: the Now Playing page runs on behind the player bar, so stop at its top
    const box = canvas.getBoundingClientRect();
    const playerBar = document.querySelector(".playerbar")?.getBoundingClientRect();
    const floor = Math.min(box.bottom, playerBar?.top ?? innerHeight, innerHeight);
    fs.w = canvas.width;
    fs.h = Math.max(0, Math.min(canvas.height, (floor - box.top) * dpr));

    // Shared levels and beat
    bands.forEach((band, i) => stepBar(band, (i + 0.5) / bands.length * 2 - 1, fs));
    fs.bass = bandLevel(0, 0.2);
    fs.onBeat = detectBeat(t, beat, fs.bass);
    fs.chorusHit = chorusHit;
    chorusHit = false;

    fs.cover = coverBox(document.querySelector(".np-art"), box, dpr, fs);

    let cover = null;
    switch (style) {
        case "ring": cover = drawRing(fs); break;
        case "wave": drawWave(fs); break;
        case "particles": drawParticles(fs); break;
        case "tunnel": cover = drawTunnel(fs); break;
        case "mirror": drawBars(fs, true); break;
        case "shatter": cover = drawShatter(fs); break;
        default: drawBars(fs, false);
    }
    setCover(cover);
}

// Where the album cover is, in canvas pixels (size ignores the pulse's scale)
function coverBox(art, box, dpr, fs) {
    if (art && art.offsetWidth > 0) {
        const rect = art.getBoundingClientRect();
        return {
            cx: (rect.left + rect.width / 2 - box.left) * dpr,
            cy: (rect.top + rect.height / 2 - box.top) * dpr,
            size: art.offsetWidth * dpr,
            found: true
        };
    }
    return { cx: fs.w / 2, cy: fs.h * 0.4, size: Math.min(fs.w, fs.h) * 0.3, found: false };
}

// Average shared level between distances from..to (0 bass … 1 treble)
function bandLevel(from, to) {
    let sum = 0, n = 0;
    bands.forEach((band, i) => {
        const d = Math.abs((i + 0.5) / bands.length * 2 - 1);
        if (d >= from && d < to) { sum += band.value; n++; }
    });
    return n ? sum / n : 0;
}

// Shared level at distance d (0 bass … 1 treble), smoothed between bands
function energyAt(d) {
    const half = bands.length / 2;
    const f = Math.min(half - 1, Math.max(0, d * half - 0.5));
    const i = Math.floor(f);
    const a = bands[half + i].value;
    const b = bands[half + Math.min(half - 1, i + 1)].value;
    return a + (b - a) * (f - i);
}

// True on a beat: the generated kick, or with the mic a jump in the bass
function detectBeat(t, beat, bass) {
    let hit;
    if (mic) {
        hit = bass > bassAvg * 1.35 + 0.05 && t - lastBeatAt > 0.25;
        bassAvg += (bass - bassAvg) * 0.05;
    } else {
        hit = beat < beatPrev && level > 0.5;
    }
    beatPrev = beat;
    if (hit) lastBeatAt = t;
    return hit;
}

// Moves a bar toward its next height. x runs -1 … 1 with 0 as the bass end.
function stepBar(bar, x, { t, speed, kick, swell, boost }) {
    const center = Math.pow(1 - Math.abs(x), 1.5);

    let target;
    if (mic) {
        target = micLevel(Math.abs(x));
    } else {
        const noise =
            0.5 * Math.sin(t * speed * bar.f[0] * 2 + bar.p[0]) +
            0.3 * Math.sin(t * speed * bar.f[1] * 2 + bar.p[1]) +
            0.2 * Math.sin(t * speed * bar.f[2] * 2 + bar.p[2]);

        target = level * swell * (
            (0.25 + 0.45 * center) * (0.55 + 0.45 * noise) +
            kick * (0.15 + 0.45 * center)
        );
    }
    target = Math.min(1.25, target * boost);

    // Rise fast, fall slowly, like a real meter
    bar.value += (target - bar.value) * (target > bar.value ? 0.35 : 0.08);
}

// Color for a bar at distance d from the bass end: the palette spread outwards, then a hue
// wave that drifts across the bars over time. Taller bars glow brighter. Full saturation keeps it vivid.
function barColor(palette, d, value) {
    const [h1, s1, l1] = d < 0.5
        ? mixHsl(palette[0], palette[1], d * 2)
        : mixHsl(palette[1], palette[2], (d - 0.5) * 2);
    const hue = h1 + 50 * d + 35 * Math.sin(huePhase + d * 4);
    const sat = Math.max(s1, 0.85);
    const light = Math.min(0.62, Math.max(0.45, l1)) + 0.1 * Math.min(1, value) + 0.08 * chorus + 0.08 * flash;
    return { hue, sat, light };
}

function fillBar(x, base, w, len, r, hue, sat, light) {
    const fill = ctx.createLinearGradient(0, base, 0, base - len);
    fill.addColorStop(0, hsla(hue, sat, light, 0.95));
    fill.addColorStop(1, hsla(hue + 40, sat, Math.min(0.85, light + 0.18), 0.9));

    ctx.fillStyle = fill;
    ctx.shadowColor = hsla(hue, sat, light, 0.75);
    roundedBar(x, base - len, w, len, r);
}

// Bars standing on the bottom edge; mirrored also hangs a copy from the top edge
function drawBars(fs, mirrored) {
    const { w, h, dpr } = fs;
    const maxH = h * (mirrored ? 0.3 : 0.42);
    const slot = w / bars.length;
    const barW = slot * 0.62;
    const r = Math.min(barW / 2, 6 * dpr);

    bars.forEach((bar, i) => {
        // The middle bars act like bass, louder and kicked harder
        const x = (i + 0.5) / bars.length * 2 - 1;
        stepBar(bar, x, fs);

        const len = Math.max(2 * dpr, bar.value * maxH);
        const bx = i * slot + (slot - barW) / 2;
        const { hue, sat, light } = barColor(fs.palette, Math.abs(x), bar.value);

        fillBar(bx, h, barW, len, r, hue, sat, light);

        if (mirrored) {
            ctx.save();
            ctx.scale(1, -1); // flipped, so "up" from y = 0 hangs down from the top
            fillBar(bx, 0, barW, len * 0.8, r, hue + 30, sat, light);
            ctx.restore();
        }
    });
}

// Bars pointing outwards from a circle around the album cover, bass at the bottom and
// treble at the top, mirrored left and right. The cover itself pulses with the bass.
function drawRing(fs) {
    const { dpr, cover } = fs;
    const { cx, cy, size } = cover;
    const radius = size * 0.72 + 8 * dpr;                 // clears the square's corners
    const maxLen = Math.min(size * 0.95, 280 * dpr);

    // About one bar per 9 CSS px of circumference
    const count = Math.max(48, Math.min(140, Math.round(2 * Math.PI * radius / dpr / 9)));
    if (ringBars.length !== count) ringBars = makeBars(count);

    const barW = Math.max(2 * dpr, (2 * Math.PI * radius / count) * 0.55);
    let bass = 0;

    ringBars.forEach((bar, i) => {
        // i = 0 at the top going clockwise; x = 0 lands at the bottom
        const angle = -Math.PI / 2 + (i + 0.5) / count * 2 * Math.PI;
        const x = (i + 0.5) / count * 2 - 1;
        stepBar(bar, x, fs);
        if (Math.abs(x) < 0.15) bass = Math.max(bass, bar.value);

        const len = Math.max(2 * dpr, bar.value * maxLen);
        const { hue, sat, light } = barColor(fs.palette, Math.abs(x), bar.value);

        ctx.save();
        ctx.translate(cx, cy);
        ctx.rotate(angle + Math.PI / 2);
        fillBar(-barW / 2, -radius, barW, len, Math.min(barW / 2, 4 * dpr), hue, sat, light);
        ctx.restore();
    });

    // Soft halo just inside the ring, brighter on the beat
    const [h0, s0] = fs.palette[0];
    const glow = ctx.createRadialGradient(cx, cy, radius * 0.6, cx, cy, radius * 1.05);
    glow.addColorStop(0, hsla(h0, Math.max(s0, 0.85), 0.55, 0));
    glow.addColorStop(1, hsla(h0, Math.max(s0, 0.85), 0.55, 0.12 + 0.25 * bass + 0.2 * flash));
    ctx.shadowBlur = 0;
    ctx.fillStyle = glow;
    ctx.beginPath();
    ctx.arc(cx, cy, radius * 1.05, 0, 2 * Math.PI);
    ctx.fill();

    return { scale: fs.calm ? 1 : 1 + 0.035 * Math.min(1, bass) * (0.5 + 0.5 * chorus) + 0.02 * flash };
}

// Three glowing lines flowing across the lower part of the page; each point rises with
// the level at its spot (bass in the middle), and the front line has a soft fill below
function drawWave(fs) {
    const { w, h, dpr, t, speed, palette } = fs;
    const base = h * 0.8;
    const maxA = h * 0.17;
    const step = 6 * dpr;

    ctx.lineCap = "round";
    ctx.lineJoin = "round";

    for (let k = 2; k >= 0; k--) {
        const phase = t * speed * (0.7 + 0.3 * k) + k * 2.1;
        const cycles = 1.5 + 0.7 * k;

        ctx.beginPath();
        for (let x = 0; x <= w + step; x += step) {
            const u = x / w;
            const e = energyAt(Math.abs(u * 2 - 1));
            const shape = 0.7 * Math.sin(u * cycles * 2 * Math.PI + phase) +
                          0.3 * Math.sin(u * cycles * 5.3 * Math.PI - phase * 1.7);
            const y = base - maxA * ((0.15 + e) * shape + 0.5 * e);
            if (x === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
        }

        // Colors across the width: the palette from the middle out, like the bars
        const stroke = ctx.createLinearGradient(0, 0, w, 0);
        [0, 0.25, 0.5, 0.75, 1].forEach(s => {
            const c = barColor(palette, Math.abs(s * 2 - 1), 0.6);
            stroke.addColorStop(s, hsla(c.hue + 25 * k, c.sat, c.light, 1));
        });
        const mid = barColor(palette, 0, 0.6);

        ctx.globalAlpha = 1 - 0.28 * k;
        ctx.strokeStyle = stroke;
        ctx.lineWidth = (4 - k) * dpr * (1 + 0.5 * chorus + 0.5 * flash);
        ctx.shadowColor = hsla(mid.hue + 25 * k, mid.sat, mid.light, 0.9);
        ctx.stroke();

        if (k === 0) {
            ctx.lineTo(w + step, h);
            ctx.lineTo(0, h);
            ctx.closePath();
            const fill = ctx.createLinearGradient(0, base - maxA, 0, h);
            fill.addColorStop(0, hsla(mid.hue, mid.sat, mid.light, 0.28 + 0.15 * chorus));
            fill.addColorStop(1, hsla(mid.hue, mid.sat, mid.light, 0));
            ctx.shadowBlur = 0;
            ctx.fillStyle = fill;
            ctx.fill();
        }
    }
    ctx.globalAlpha = 1;
}

// Sparks thrown out from the cover's edge: a burst on each beat, a big one as a chorus
// starts, and a trickle in between that follows the bass
function drawParticles(fs) {
    const { dpr, dt, calm, cover, palette } = fs;
    const frames = dt * 60;

    let emit = level * fs.bass * 3 * frames;
    if (fs.onBeat) emit += (calm ? 8 : 28) + 40 * chorus;
    if (fs.chorusHit && !calm) emit += 140;
    if (!playing) emit = 0;

    for (let n = Math.floor(emit) + (Math.random() < emit % 1 ? 1 : 0); n > 0; n--) {
        const angle = Math.random() * 2 * Math.PI;
        const r = cover.size * (0.5 + 0.1 * Math.random());
        const v = (1.2 + 4 * fs.bass + 3 * Math.random()) * dpr * (calm ? 0.35 : 1) * (1 + 0.5 * chorus);
        const c = barColor(palette, Math.random(), 0.7);
        particles.push({
            x: cover.cx + Math.cos(angle) * r,
            y: cover.cy + Math.sin(angle) * r,
            vx: Math.cos(angle) * v,
            vy: Math.sin(angle) * v,
            size: (2.2 + 3.5 * Math.random()) * dpr,
            life: 1,
            fade: 0.35 + 0.4 * Math.random(),
            c
        });
    }
    if (particles.length > 800) particles.splice(0, particles.length - 800);

    // Additive blending: overlapping sparks add up to a hot glow. Shadows would be
    // too slow for hundreds of dots, so each gets a faint halo instead.
    ctx.globalCompositeOperation = "lighter";
    ctx.shadowBlur = 0;
    const drag = Math.pow(0.985, frames);

    particles = particles.filter(p => {
        p.x += p.vx * frames;
        p.y += p.vy * frames;
        p.vx *= drag;
        p.vy *= drag;
        p.life -= dt * p.fade;
        if (p.life <= 0) return false;

        const { hue, sat, light } = p.c;
        ctx.fillStyle = hsla(hue, sat, light, 0.18 * p.life);
        ctx.beginPath();
        ctx.arc(p.x, p.y, p.size * 3.5, 0, 2 * Math.PI);
        ctx.fill();

        ctx.fillStyle = hsla(hue, sat, Math.min(0.9, light + 0.2), 0.9 * p.life);
        ctx.beginPath();
        ctx.arc(p.x, p.y, p.size, 0, 2 * Math.PI);
        ctx.fill();
        return true;
    });
    ctx.globalCompositeOperation = "source-over";
}

// Rounded squares the shape of the cover, sent out from it on each beat and speeding up
// as they go, like flying through a tunnel. The cover pulses with the bass.
function drawTunnel(fs) {
    const { w, h, dpr, dt, cover, palette } = fs;
    const frames = dt * 60;
    const start = cover.size * 0.55;

    if (fs.onBeat) tunnelRings.push({ r: start, d: Math.random(), weight: 1 + fs.bass });
    if (fs.chorusHit)
        for (let k = 1; k <= 3; k++) tunnelRings.push({ r: start * (1 + 0.25 * k), d: Math.random(), weight: 1.6 });

    const far = Math.hypot(w, h) * 0.7;
    const grow = (1 + 1.2 * chorus) * (fs.calm ? 0.4 : 1);

    tunnelRings = tunnelRings.filter(ring => {
        ring.r += (ring.r * 0.018 + 1.5 * dpr) * grow * frames;
        if (ring.r > far) return false;

        const fade = 1 - (ring.r - start) / (far - start);
        const c = barColor(palette, ring.d, 0.6);
        ctx.strokeStyle = hsla(c.hue, c.sat, c.light, 0.9 * fade);
        ctx.shadowColor = hsla(c.hue, c.sat, c.light, 0.8 * fade);
        ctx.lineWidth = (1.5 + 4 * ring.r / cover.size) * dpr * ring.weight * (1 + 0.4 * fs.bass);

        roundedSquare(cover.cx, cover.cy, ring.r, ring.r * 0.08);
        ctx.stroke();
        return true;
    });

    return { scale: fs.calm ? 1 : 1 + 0.03 * Math.min(1, fs.bass) + 0.02 * flash };
}

function roundedSquare(cx, cy, half, r) {
    const x = cx - half, y = cy - half, s = half * 2;
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + s, y, x + s, y + s, r);
    ctx.arcTo(x + s, y + s, x, y + s, r);
    ctx.arcTo(x, y + s, x, y, r);
    ctx.arcTo(x, y, x + s, y, r);
    ctx.closePath();
}

// The cover cut into tiles that fly apart on the beat (and further in a chorus), then
// settle back into the picture. The real cover fades out while the tiles stand in for it.
function drawShatter(fs) {
    const { cover, dpr, calm } = fs;
    if (!cover.found || !coverImg?.complete || !coverImg.naturalWidth) return null;

    const n = 6;
    if (shards.length !== n * n) {
        shards = Array.from({ length: n * n }, (_, k) => ({
            col: k % n,
            row: Math.floor(k / n),
            ox: (k % n + 0.5) / n - 0.5,       // offset from the middle, -0.5 … 0.5
            oy: (Math.floor(k / n) + 0.5) / n - 0.5,
            spin: Math.random() - 0.5,
            reach: 0.6 + 0.8 * Math.random()
        }));
    }

    // How far apart: rises fast on a hit, eases back together
    const target = calm
        ? 0.1 * Math.max(0, fs.bass - 0.4)
        : level * (0.45 * fs.kick + 0.5 * Math.max(0, fs.bass - 0.4)) * fs.boost + 0.6 * flash;
    shatterPush += (target - shatterPush) * (target > shatterPush ? 0.4 : 0.1);

    const { cx, cy, size } = cover;
    const tile = size / n;
    const sw = coverImg.naturalWidth / n;
    const sh = coverImg.naturalHeight / n;
    const [h0, s0] = fs.palette[0];
    ctx.shadowColor = hsla(h0, Math.max(s0, 0.85), 0.55, 0.6);
    ctx.shadowBlur = 16 * dpr * Math.min(1, shatterPush * 2); // none at rest, so the seams vanish

    shards.forEach(s => {
        const spread = shatterPush * s.reach;
        ctx.save();
        ctx.translate(cx + s.ox * size * (1 + spread), cy + s.oy * size * (1 + spread));
        ctx.rotate(s.spin * spread * 0.8);
        // A hair bigger so no seams show when the picture is whole
        ctx.drawImage(coverImg, s.col * sw, s.row * sh, sw, sh, -tile / 2, -tile / 2, tile + 0.5, tile + 0.5);
        ctx.restore();
    });

    return { hidden: true };
}

// Bar height from the mic for a bar at distance d from the middle (0 middle … 1 edge).
// The middle shows bass and the edges treble, spread on a log scale like hearing is.
function micLevel(d) {
    const { analyser, audio, bins } = mic;
    const hzPerBin = audio.sampleRate / analyser.fftSize;

    const lowHz = 40 * Math.pow(8000 / 40, d);
    const highHz = 40 * Math.pow(8000 / 40, Math.min(1, d + 0.04));
    const from = Math.max(1, Math.floor(lowHz / hzPerBin));
    const to = Math.max(from, Math.ceil(highHz / hzPerBin));

    let sum = 0;
    for (let b = from; b <= to && b < bins.length; b++)
        sum += bins[b];

    // Treble is naturally quieter than bass, so tilt it up a little
    const value = (sum / (to - from + 1) / 255) * (1 + d * 0.8);
    micPeak = Math.max(micPeak, value);

    return Math.pow(Math.min(1, value / micPeak), 1.4) * 0.95;
}

function roundedBar(x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x, y + h);
    ctx.lineTo(x, y + r);
    ctx.quadraticCurveTo(x, y, x + r, y);
    ctx.lineTo(x + w - r, y);
    ctx.quadraticCurveTo(x + w, y, x + w, y + r);
    ctx.lineTo(x + w, y + h);
    ctx.closePath();
    ctx.fill();
}

// Picks three vivid, distinct colors from the album art. Reading pixels needs
// the image served with CORS (Spotify's i.scdn.co does); otherwise use the fallback.
function loadPalette(url) {
    return new Promise(resolve => {
        const img = new Image();
        img.crossOrigin = "anonymous";
        img.onerror = () => resolve(FALLBACK);
        img.onload = () => {
            try {
                const size = 32;
                const small = document.createElement("canvas");
                small.width = small.height = size;
                const sctx = small.getContext("2d", { willReadFrequently: true });
                sctx.drawImage(img, 0, 0, size, size);
                const data = sctx.getImageData(0, 0, size, size).data;

                // Count colors in coarse buckets, weighting vivid ones higher. Skip near-black
                // and greys; a muted cover (sepia, washed-out) gets a second, looser pass, and
                // the drawing turns its tints up to full saturation.
                const countColors = minSat => {
                    const buckets = new Map();
                    for (let i = 0; i < data.length; i += 4) {
                        const [r, g, b] = [data[i], data[i + 1], data[i + 2]];
                        const max = Math.max(r, g, b), min = Math.min(r, g, b);
                        const sat = max === 0 ? 0 : (max - min) / max;
                        if (max < 40 || sat < minSat) continue;

                        const key = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
                        const entry = buckets.get(key) ?? { r: 0, g: 0, b: 0, n: 0, score: 0 };
                        entry.r += r; entry.g += g; entry.b += b; entry.n++;
                        entry.score += 1 + sat * 2 + max / 255;
                        buckets.set(key, entry);
                    }
                    return buckets;
                };

                let buckets = countColors(0.2);
                if (buckets.size === 0)
                    buckets = countColors(0.06);

                const picked = [];
                [...buckets.values()]
                    .sort((a, b) => b.score - a.score)
                    .map(e => [e.r / e.n, e.g / e.n, e.b / e.n])
                    .forEach(c => {
                        if (picked.length < 3 && picked.every(p => distance(p, c) > 80))
                            picked.push(c);
                    });

                // Few distinct colors (e.g. a mostly one-color cover): add the first one's
                // hue rotated by thirds, so the bars still span a range of colors
                while (picked.length < 3) {
                    if (picked.length === 0) { picked.push(...FALLBACK); break; }
                    const [hue, s, l] = rgbToHsl(picked[0]);
                    picked.push(hslToRgb([hue + 120 * picked.length, Math.max(s, 0.8), l]));
                }

                resolve(picked);
            } catch {
                resolve(FALLBACK); // tainted canvas: the image had no CORS header
            }
        };
        img.src = url;
    });
}

const distance = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
const hsla = (h, s, l, a) => `hsla(${((h % 360) + 360) % 360}, ${s * 100}%, ${l * 100}%, ${a})`;

// Blends two [hue, sat, light] colors, going the short way around the hue circle
function mixHsl(a, b, f) {
    let dh = b[0] - a[0];
    if (dh > 180) dh -= 360;
    if (dh < -180) dh += 360;
    return [a[0] + dh * f, a[1] + (b[1] - a[1]) * f, a[2] + (b[2] - a[2]) * f];
}

// [r, g, b] 0–255 → [hue 0–360, sat 0–1, light 0–1]
function rgbToHsl([r, g, b]) {
    r /= 255; g /= 255; b /= 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b);
    const l = (max + min) / 2;
    if (max === min) return [0, 0, l];

    const d = max - min;
    const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    const h = max === r ? (g - b) / d + (g < b ? 6 : 0)
            : max === g ? (b - r) / d + 2
            : (r - g) / d + 4;
    return [h * 60, s, l];
}

function hslToRgb([h, s, l]) {
    h = ((h % 360) + 360) % 360 / 360;
    const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
    const p = 2 * l - q;
    const channel = t => {
        t = (t + 1) % 1;
        if (t < 1 / 6) return p + (q - p) * 6 * t;
        if (t < 1 / 2) return q;
        if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6;
        return p;
    };
    return [channel(h + 1 / 3), channel(h), channel(h - 1 / 3)].map(v => v * 255);
}

function hash(text) {
    let h = 0;
    for (const ch of text) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
    return h;
}
