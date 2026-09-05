export function getWidth(element) {
    return element.offsetWidth;
}

export function updateHeight() {
    const el = document.getElementById("playerbar");
    if (!el) return;

    const height = el.offsetHeight;
    document.documentElement.style.setProperty("--playerbar-height", height + "px");
}

window.addEventListener("resize", () => {
    // Call only if the element exists
    const el = document.getElementById("playerbar");
    if (el) updateHeight();
});

