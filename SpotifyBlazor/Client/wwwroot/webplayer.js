// Spotify Web Playback SDK wrapper.
// Turns this browser tab into a Spotify Connect device (Premium accounts only).

let player = null;
let sdkLoading = null;

function loadSdk() {
    if (window.Spotify) return Promise.resolve();
    if (sdkLoading) return sdkLoading;

    sdkLoading = new Promise((resolve, reject) => {
        // The SDK calls this global once it has loaded
        window.onSpotifyWebPlaybackSDKReady = () => resolve();

        const script = document.createElement("script");
        script.src = "https://sdk.scdn.co/spotify-player.js";
        script.async = true;
        script.onerror = () => {
            sdkLoading = null;
            reject(new Error("Failed to load the Spotify Web Playback SDK"));
        };
        document.head.appendChild(script);
    });

    return sdkLoading;
}

export async function connect(dotnetRef, name, volume) {
    if (player) return true;

    await loadSdk();

    player = new window.Spotify.Player({
        name: name,
        volume: volume,
        // The SDK calls this whenever it needs a token, including after expiry
        getOAuthToken: async cb => {
            const token = await dotnetRef.invokeMethodAsync("GetAccessToken");
            if (token) cb(token);
        }
    });

    player.addListener("ready", ({ device_id }) =>
        dotnetRef.invokeMethodAsync("OnReady", device_id));

    player.addListener("not_ready", ({ device_id }) =>
        dotnetRef.invokeMethodAsync("OnNotReady", device_id));

    // Fires on play/pause/track change, not on every position tick
    player.addListener("player_state_changed", state =>
        dotnetRef.invokeMethodAsync("OnStateChanged", state != null));

    for (const type of ["initialization_error", "authentication_error", "account_error", "playback_error"]) {
        player.addListener(type, ({ message }) =>
            dotnetRef.invokeMethodAsync("OnError", type, message));
    }

    // Browsers block audio until the user interacts with the page,
    // so unlock it on the first click anywhere (e.g. clicking a track)
    document.addEventListener("click", activate, { once: true, capture: true });

    return await player.connect();
}

// Browsers block audio until the user interacts with the page.
// Call this synchronously from a click handler before starting playback here.
export function activate() {
    if (player) player.activateElement();
}

export function disconnect() {
    if (player) {
        player.disconnect();
        player = null;
    }
}
