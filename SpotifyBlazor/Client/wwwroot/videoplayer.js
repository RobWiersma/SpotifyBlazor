// Drives the music video iframe with YouTube's IFrame Player API, so the page
// knows when a video won't play and can move on to the next search result.

// 2 bad id, 5 HTML5 player error, 100 removed/private,
// 101/150 owner blocks embedding, 153 browser sent no referrer
const SKIP_ERRORS = new Set([2, 5, 100, 101, 150, 153]);

let apiReady;

// Called as the panel opens, so the API is ready before a blocked video can fail
export function loadApi() {
    apiReady ??= new Promise((resolve, reject) => {
        if (window.YT?.Player) {
            resolve();
            return;
        }

        // The API calls this global once it has loaded
        const previous = window.onYouTubeIframeAPIReady;
        window.onYouTubeIframeAPIReady = () => {
            previous?.();
            resolve();
        };

        const script = document.createElement("script");
        script.src = "https://www.youtube.com/iframe_api";
        script.onerror = () => {
            apiReady = undefined; // let the next open try again
            script.remove();
            reject(new Error("YouTube player API failed to load"));
        };
        document.head.appendChild(script);
    });

    return apiReady;
}

// iframe already shows videoIds[0]; on a playback error, try the next id.
// Tells .NET which video is showing (OnVideoChanged) or that none would play (OnAllVideosBlocked).
// Blazor owns the iframe: closing the panel removes it, which ends the player too.
export async function attach(iframe, videoIds, dotnetRef) {
    await loadApi();

    let index = 0;

    const player = new YT.Player(iframe, {
        events: {
            onError: e => {
                if (!SKIP_ERRORS.has(e.data))
                    return;

                index++;
                if (index < videoIds.length) {
                    player.loadVideoById(videoIds[index]);
                    dotnetRef.invokeMethodAsync("OnVideoChanged", index);
                } else {
                    dotnetRef.invokeMethodAsync("OnAllVideosBlocked");
                }
            }
        }
    });
}
