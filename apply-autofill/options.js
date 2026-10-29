const form = document.getElementById("form");
const status = document.getElementById("status");

for (const sel of form.querySelectorAll("select")) {
  for (const v of ["", "Yes", "No"]) sel.add(new Option(v || "(skip, I'll answer)", v));
}

function fill(data) {
  for (const el of form.elements) if (el.name) el.value = data[el.name] ?? "";
}

function read() {
  const data = {};
  for (const el of form.elements) if (el.name) data[el.name] = el.value.trim();
  return data;
}

function flash(msg) {
  status.textContent = msg;
  setTimeout(() => (status.textContent = ""), 2500);
}

browser.storage.local.get("autofillData").then(({ autofillData }) => fill(autofillData || {}));

form.addEventListener("submit", async (e) => {
  e.preventDefault();
  await browser.storage.local.set({ autofillData: read() });
  flash("Saved ✓");
});

document.getElementById("export").addEventListener("click", () => {
  const blob = new Blob([JSON.stringify(read(), null, 2)], { type: "application/json" });
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob);
  a.download = "apply-autofill-backup.json";
  a.click();
  URL.revokeObjectURL(a.href);
});

document.getElementById("import").addEventListener("change", async (e) => {
  const file = e.target.files[0];
  if (!file) return;
  try {
    fill(JSON.parse(await file.text()));
    await browser.storage.local.set({ autofillData: read() });
    flash("Imported ✓");
  } catch {
    flash("That file isn't a valid backup.");
  }
  e.target.value = "";
});
