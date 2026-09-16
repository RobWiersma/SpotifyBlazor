export function scrollLyricsToLine(panel, index) {
    if (!panel) return;

    const lines = panel.querySelectorAll(".lyrics-line");
    if (index < 0 || index >= lines.length) return;

    const target = lines[index];
    panel.scrollTo({
        top: target.offsetTop - panel.clientHeight / 2,
        behavior: "smooth"
    });
}