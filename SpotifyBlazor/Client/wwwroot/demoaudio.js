// Demo mode's player: plays the demo songs in this page with an <audio> element, in place of
// Spotify. The songs are on this site, so the visualizer can analyze them directly (no mic).

let audio;          // the <audio> element
let graph;          // { ctx, analyser }: Web Audio graph the visualizer reads
let dotnet;         // DemoLibrary, told when a song ends

function element() {
    if (!audio) {
        audio = new Audio();
        audio.preload = "auto";
        audio.addEventListener("ended", () => dotnet?.invokeMethodAsync("OnSongEnded"));
    }
    return audio;
}

// Audio → analyser → speakers. Created once: an element can only be wired into one graph.
function ensureGraph() {
    if (graph) return graph;

    const ctx = new AudioContext({ latencyHint: "interactive" });
    const analyser = ctx.createAnalyser();
    analyser.fftSize = 1024;
    analyser.smoothingTimeConstant = 0.2;
    analyser.minDecibels = -80;
    analyser.maxDecibels = -10;

    ctx.createMediaElementSource(element()).connect(analyser);
    analyser.connect(ctx.destination);

    graph = { ctx, analyser };
    return graph;
}

export function init(dotnetRef) {
    dotnet = dotnetRef;
    element();
}

// Starts a song (or keeps the current one) at positionMs. Returns false when the browser
// blocks playback, e.g. before the visitor has clicked anything on the page.
export async function play(url, positionMs, volume) {
    const el = element();
    const target = new URL(url, document.baseURI).href;

    if (el.src !== target)
        el.src = target;

    if (positionMs != null)
        el.currentTime = positionMs / 1000;

    if (volume != null)
        el.volume = Math.max(0, Math.min(1, volume / 100));

    // Browsers start an AudioContext suspended until the page has been clicked
    const { ctx } = ensureGraph();
    if (ctx.state === "suspended")
        await ctx.resume().catch(() => { });

    try {
        await el.play();
        return true;
    } catch {
        return false;
    }
}

export function pause() {
    audio?.pause();
}

export function seek(positionMs) {
    if (audio)
        audio.currentTime = positionMs / 1000;
}

export function setVolume(volume) {
    if (audio)
        audio.volume = Math.max(0, Math.min(1, volume / 100));
}

export function stop() {
    if (!audio) return;
    audio.pause();
    audio.removeAttribute("src");
    audio.load();
}

// Where playback is, in ms, and whether it's running
export function state() {
    if (!audio)
        return { positionMs: 0, playing: false };

    return {
        positionMs: Math.round(audio.currentTime * 1000),
        playing: !audio.paused && !audio.ended
    };
}

// For the visualizer: the analyser on the demo songs and its AudioContext
export function input() {
    const { ctx, analyser } = ensureGraph();
    return { audio: ctx, analyser };
}
