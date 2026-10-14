export function getWidth(element) {
    return element.offsetWidth;
}

let observer = null;

// Publishes the player bar's height as --playerbar-height so page content can
// reserve room for it, and keeps it current as the bar's content or the window changes.
export function updateHeight() {
    const el = document.getElementById("playerbar");
    if (!el) return;

    document.documentElement.style.setProperty("--playerbar-height", el.offsetHeight + "px");

    if (!observer) {
        observer = new ResizeObserver(() => updateHeight());
        observer.observe(el);
    }
}

export function showPlayerBar() {
    document.querySelector('.playerbar').classList.add('playerbar-visible');
}

export function hidePlayerBar() {
    document.querySelector('.playerbar').classList.remove('playerbar-visible');
}
