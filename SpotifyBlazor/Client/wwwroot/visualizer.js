// Now Playing visualizer: animated bars in the album art's colors.
// Spotify's audio can't be analyzed in the browser (DRM, and the audio-analysis
// API is closed to new apps), so by default the motion is generated: layered
// sine "noise", a steady kick at a tempo picked from the track id, and slow
// swells. It rises while playing and settles when paused.
// Mic mode instead listens through the microphone and draws what it hears.
// The audio is only measured in the browser, never recorded or sent anywhere.

// Spotify green, violet, hot pink
const FALLBACK = [[29, 185, 84], [127, 90, 240], [255, 64, 160]];

let canvas, ctx, resizeObserver, frame;
let playing = false;
let level = 0;              // 0 paused … 1 playing, eased
let bpm = 118;
let bars = [];
let colors = FALLBACK.map(c => [...c]);
let targetColors = FALLBACK;
let imageUrl;

// Mic mode
let mic;                    // { stream, audio, analyser, bins }
let micPeak = 0.2;          // running loudness peak, so quiet and loud rooms both fill the bars

const reducedMotion = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

export function start(el, options) {
    stop();

    canvas = el;
    ctx = canvas.getContext("2d");

    resizeObserver = new ResizeObserver(resize);
    resizeObserver.observe(canvas);
    resize();

    update(options);
    frame = requestAnimationFrame(draw);
}

// options: { imageUrl, playing, trackId }
export function update(options) {
    playing = !!options.playing;

    if (options.trackId)
        bpm = 90 + (hash(options.trackId) % 50); // 90–139, stable per track

    if (options.imageUrl && options.imageUrl !== imageUrl) {
        imageUrl = options.imageUrl;
        loadPalette(imageUrl).then(p => { if (imageUrl === options.imageUrl) targetColors = p; });
    }
}

export function stop() {
    cancelAnimationFrame(frame);
    resizeObserver?.disconnect();
    canvas = ctx = resizeObserver = undefined;
    stopMic();
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

    // About one bar per 18 CSS px, each with its own random wobble
    const count = Math.max(24, Math.min(96, Math.round(canvas.clientWidth / 18)));
    bars = Array.from({ length: count }, () => ({
        value: 0,
        f: [0.6 + Math.random() * 0.9, 1.7 + Math.random() * 1.6, 3.5 + Math.random() * 3],
        p: [Math.random() * 6.28, Math.random() * 6.28, Math.random() * 6.28]
    }));
}

function draw(now) {
    frame = requestAnimationFrame(draw);
    if (!ctx || bars.length === 0) return;

    const t = now / 1000;
    const calm = reducedMotion();
    const speed = calm ? 0.35 : 1;

    level += ((playing ? 1 : 0) - level) * 0.04;

    // Ease colors toward the current album's palette
    for (let i = 0; i < 3; i++)
        for (let c = 0; c < 3; c++)
            colors[i][c] += (targetColors[i][c] - colors[i][c]) * 0.03;

    // Kick: a sharp pulse on each beat. Swell: slow 16-second build and release.
    const beat = (t * bpm / 60) % 1;
    const kick = calm ? 0 : Math.pow(1 - beat, 6);
    const swell = 0.7 + 0.3 * Math.sin(t * 2 * Math.PI / 16);

    const w = canvas.width, h = canvas.height;
    const dpr = window.devicePixelRatio || 1;
    const maxH = h * 0.42;
    const slot = w / bars.length;
    const barW = slot * 0.62;

    ctx.clearRect(0, 0, w, h);
    ctx.shadowBlur = 22 * dpr;

    // The album's colors as hue/saturation/lightness, so each bar can shift its hue
    const palette = colors.map(rgbToHsl);

    if (mic) {
        mic.analyser.getByteFrequencyData(mic.bins);
        // Let the peak decay slowly so the bars rescale when the room gets quieter
        micPeak = Math.max(micPeak * 0.997, 0.08);
    }

    bars.forEach((bar, i) => {
        // Mirrored: the middle bars act like bass, louder and kicked harder
        const x = (i + 0.5) / bars.length * 2 - 1;
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

        // Rise fast, fall slowly, like a real meter
        bar.value += (target - bar.value) * (target > bar.value ? 0.35 : 0.08);

        const barH = Math.max(2 * dpr, bar.value * maxH);
        const bx = i * slot + (slot - barW) / 2;

        // Color: the palette spread from the middle outwards, then a hue wave that drifts
        // across the bars over time. Taller bars glow brighter. Full saturation keeps it vivid.
        const d = Math.abs(x);
        const [h1, s1, l1] = d < 0.5
            ? mixHsl(palette[0], palette[1], d * 2)
            : mixHsl(palette[1], palette[2], (d - 0.5) * 2);
        const hue = h1 + 50 * d + 35 * Math.sin(t * speed * 0.4 + d * 4);
        const sat = Math.max(s1, 0.85);
        const light = Math.min(0.62, Math.max(0.45, l1)) + 0.1 * bar.value;

        const fill = ctx.createLinearGradient(0, h, 0, h - barH);
        fill.addColorStop(0, hsla(hue, sat, light, 0.95));
        fill.addColorStop(1, hsla(hue + 40, sat, Math.min(0.85, light + 0.18), 0.9));

        ctx.fillStyle = fill;
        ctx.shadowColor = hsla(hue, sat, light, 0.75);
        roundedBar(bx, h - barH, barW, barH, Math.min(barW / 2, 6 * dpr));
    });
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

                // Count colors in coarse buckets, weighting vivid ones higher
                const buckets = new Map();
                for (let i = 0; i < data.length; i += 4) {
                    const [r, g, b] = [data[i], data[i + 1], data[i + 2]];
                    const max = Math.max(r, g, b), min = Math.min(r, g, b);
                    const sat = max === 0 ? 0 : (max - min) / max;
                    if (max < 40 || sat < 0.2) continue; // skip near-black and greys

                    const key = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
                    const entry = buckets.get(key) ?? { r: 0, g: 0, b: 0, n: 0, score: 0 };
                    entry.r += r; entry.g += g; entry.b += b; entry.n++;
                    entry.score += 1 + sat * 2 + max / 255;
                    buckets.set(key, entry);
                }

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
