// Toolbar button / Alt+Shift+F: inject the filler into the current tab.
browser.action.onClicked.addListener(async (tab) => {
  try {
    await browser.scripting.executeScript({
      target: { tabId: tab.id, allFrames: true },
      files: ["fill.js"],
    });
  } catch (e) {
    console.error("Apply Autofill: could not run on this page", e);
  }
});

browser.runtime.onMessage.addListener((msg) => {
  if (msg && msg.type === "openOptions") browser.runtime.openOptionsPage();
});

browser.runtime.onInstalled.addListener(({ reason }) => {
  if (reason === "install") browser.runtime.openOptionsPage();
});
