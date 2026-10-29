// Injected on demand. Fills empty fields in the open application step; never clicks Next/Submit.
(async () => {
  const api = typeof browser !== "undefined" ? browser : chrome;
  const isTop = window === window.top;
  const d = (await api.storage.local.get("autofillData")).autofillData;

  if (!d || !d.firstName) {
    if (isTop) {
      toast("Add your info first. Opening settings…", []);
      api.runtime.sendMessage({ type: "openOptions" });
    }
    return;
  }

  // ---------- helpers ----------
  const norm = (s) =>
    (s || "").replace(/[‘’]/g, "'").replace(/\s+/g, " ").trim().toLowerCase();
  const textOf = (n) => (n ? n.innerText || n.textContent || "" : "");
  const shadowOf = (e) => e.openOrClosedShadowRoot || e.shadowRoot;

  function deepQuery(root, sel, out = []) {
    root.querySelectorAll(sel).forEach((e) => out.push(e));
    root.querySelectorAll("*").forEach((e) => {
      const s = shadowOf(e);
      if (s) deepQuery(s, sel, out);
    });
    return out;
  }

  function visible(el) {
    if (!el || !el.getClientRects().length) return false;
    return getComputedStyle(el).visibility !== "hidden";
  }

  // ---------- answers ----------
  const fullName = [d.firstName, d.lastName].filter(Boolean).join(" ");
  const cityOnly = (d.city || "").split(",")[0].trim();
  // "Location" fields (LinkedIn's is a city typeahead) want "City, State, Country".
  const locationText = [d.state, d.country].reduce(
    (acc, part) => (part && !norm(acc).includes(norm(part)) ? `${acc}, ${part}` : acc),
    (d.city || "").trim()
  );

  const skills = (d.skills || "")
    .split(/\r?\n/)
    .map((l) => l.match(/^\s*(.+?)\s*[:=]\s*(\d+(?:\.\d+)?)\s*$/))
    .filter(Boolean)
    .map((m) => ({ name: norm(m[1]), years: m[2] }))
    .sort((a, b) => b.name.length - a.name.length);

  function yearsFor(q) {
    for (const s of skills) {
      const esc = s.name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
      if (new RegExp(`(^|[^a-z0-9])${esc}([^a-z0-9]|$)`).test(q)) return s.years;
    }
    return d.yearsDefault || "";
  }

  const customRules = (d.rules || "")
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l && !l.startsWith("#") && l.includes("=>"))
    .map((l) => {
      const i = l.indexOf("=>");
      const pat = l.slice(0, i).trim();
      const answer = l.slice(i + 2).trim();
      const re = pat.match(/^\/(.+)\/([a-z]*)$/);
      if (re) {
        try {
          const rx = new RegExp(re[1], re[2].includes("i") ? re[2] : re[2] + "i");
          return { test: (q) => rx.test(q), answer };
        } catch {
          return null;
        }
      }
      const p = norm(pat);
      return { test: (q) => q.includes(p), answer };
    })
    .filter(Boolean);

  // Order matters: specific yes/no questions before generic contact fields
  // ("authorized to work in this country" must not hit the country rule).
  const BUILTIN = [
    [/sponsor|visa status|require.*visa/, () => d.needsSponsorship],
    [/authori[sz]ed to work|legally (authori[sz]ed|eligible|able|permitted)|eligible to work|right to work|work authori[sz]ation/, () => d.workAuthorized],
    [/18 years|over 18|at least 18|legal age/, () => "Yes"],
    [/background check|drug (test|screen)/, () => d.backgroundCheck],
    [/relocat/, () => d.relocate],
    [/commut|on-?site|in[- ]office|in the office|hybrid/, () => d.commute],
    [/(completed|have|hold|possess|earned).*(degree|level of education)/, () => d.hasDegree],
    [/highest (level of )?education|degree/, () => d.education],
    [/how many years|years.*experience|experience.*years/, (q) => yearsFor(q)],
    [/salary|compensation|pay (expectation|requirement)|expected pay|desired pay|rate expectation/, () => d.salary],
    [/notice period|start date|available to start|when can you start|earliest start/, () => d.startDate],
    [/hear about|how did you (find|learn)|referral source/, () => "LinkedIn"],
    [/gender|\bsex\b/, () => d.gender],
    [/\brace\b|ethnicity|hispanic|latino/, () => d.ethnicity],
    [/veteran|military/, () => d.veteran],
    [/disabilit/, () => d.disability],
    [/first name|given name/, () => d.firstName],
    [/last name|surname|family name/, () => d.lastName],
    [/full name|^name\b|your name|legal name/, () => fullName],
    [/e-?mail/, () => d.email],
    [/country code|phone country|dialing code/, () => d.phoneCountry],
    [/phone|mobile|\bcell\b/, () => d.phone],
    [/linkedin/, () => d.linkedin],
    [/github/, () => d.github],
    [/website|portfolio|personal (site|url)|\burl\b/, () => d.website],
    [/street|address line 1|^address|mailing address/, () => d.address],
    [/location/, () => locationText],
    [/\bcity\b/, () => cityOnly],
    [/\bstate\b|province/, () => d.state],
    [/\bzip\b|postal/, () => d.zip],
    [/\bcountry\b/, () => d.country],
    [/current (job )?title|job title|headline/, () => d.currentTitle],
    [/current (company|employer)|company name/, () => d.currentCompany],
    [/cover letter|summary|why (are you|do you)/, () => d.coverLetter],
  ];

  function answerFor(q) {
    for (const r of customRules) if (r.test(q)) return r.answer;
    for (const [rx, fn] of BUILTIN) if (rx.test(q)) return (fn(q) || "").toString().trim();
    return "";
  }

  // ---------- reading the form ----------
  function byIds(el, ids) {
    const root = el.getRootNode();
    return ids
      .split(/\s+/)
      .map((id) => textOf(root.getElementById ? root.getElementById(id) : document.getElementById(id)))
      .join(" ");
  }

  function labelOf(el) {
    if (el.labels && el.labels.length) return textOf(el.labels[0]);
    if (el.getAttribute("aria-labelledby")) return byIds(el, el.getAttribute("aria-labelledby"));
    return el.getAttribute("aria-label") || "";
  }

  function questionFor(el) {
    if (el.type === "radio") {
      const group = el.closest("fieldset, [role=radiogroup]");
      if (group) {
        const legend = group.querySelector("legend");
        if (legend) return norm(textOf(legend));
        if (group.getAttribute("aria-labelledby")) return norm(byIds(group, group.getAttribute("aria-labelledby")));
        if (group.getAttribute("aria-label")) return norm(group.getAttribute("aria-label"));
      }
    }
    const q =
      labelOf(el) ||
      el.getAttribute("placeholder") ||
      textOf(el.closest("[data-test-form-element], .fb-dash-form-element, .jobs-easy-apply-form-element")) ||
      el.name ||
      el.id;
    return norm(q);
  }

  // ---------- writing values ----------
  function setValue(el, value) {
    const proto =
      el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype
      : el instanceof HTMLSelectElement ? HTMLSelectElement.prototype
      : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(proto, "value").set.call(el, value);
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new Event("change", { bubbles: true }));
    el.dispatchEvent(new Event("blur", { bubbles: true }));
  }

  const PLACEHOLDER = /^(select an option|select|choose.*|please select.*|--.*|)$/;

  function pick(options, answer) {
    const a = norm(answer);
    if (!a) return null;
    const c = options.filter((o) => !PLACEHOLDER.test(o.text));
    return (
      c.find((o) => o.text === a) ||
      c.find((o) => o.text.startsWith(a)) ||
      c.find((o) => o.text.includes(a)) ||
      c.find((o) => o.text.length >= 3 && a.includes(o.text)) ||
      null
    );
  }

  function mark(el, ok) {
    const target = el.type === "radio" ? el.closest("fieldset, [role=radiogroup]") || el : el;
    target.style.outline = ok ? "2px solid #2e7d32" : "2px dashed #e65100";
    target.style.outlineOffset = "2px";
  }

  const key = (el, type, k) =>
    el.dispatchEvent(new KeyboardEvent(type, { key: k, code: k, bubbles: true }));

  // Typeaheads (LinkedIn location) only accept a value picked from their suggestion list.
  async function chooseSuggestion(el, typed) {
    const listId = el.getAttribute("aria-controls") || el.getAttribute("aria-owns");
    const want = norm(typed);
    const first = want.split(",")[0];
    for (let i = 0; i < 40; i++) {
      await new Promise((r) => setTimeout(r, 100));
      const root = el.getRootNode();
      const list = listId && root.getElementById ? root.getElementById(listId) : null;
      const opts = (list ? [...list.querySelectorAll("[role=option]")] : deepQuery(document, "[role=listbox] [role=option]")).filter(visible);
      if (!opts.length) continue;
      const best =
        opts.find((o) => norm(textOf(o)) === want) ||
        opts.find((o) => norm(textOf(o)).startsWith(first)) ||
        opts[0];
      for (const t of ["mousedown", "mouseup"]) best.dispatchEvent(new MouseEvent(t, { bubbles: true }));
      best.click();
      return true;
    }
    // No list appeared: try the keyboard way.
    key(el, "keydown", "ArrowDown");
    key(el, "keydown", "Enter");
    return false;
  }

  // ---------- scope: the open Easy Apply modal, else the page minus header/search ----------
  const dialogs = deepQuery(document, '[role="dialog"], dialog[open], .artdeco-modal').filter(
    (dl) => visible(dl) && deepQuery(dl, "input, select, textarea").length
  );
  let fields = dialogs.length
    ? deepQuery(dialogs[dialogs.length - 1], "input, select, textarea")
    : deepQuery(document, "input, select, textarea").filter(
        (e) => !e.closest("header, nav, [role=search], [role=banner], form[role=search]")
      );

  const SKIP_TYPES = /^(hidden|submit|button|reset|image|file|password|search)$/;
  fields = fields.filter(
    (e) =>
      !SKIP_TYPES.test(e.type) &&
      !e.disabled &&
      !e.readOnly &&
      (visible(e) || (e.labels && e.labels[0] && visible(e.labels[0])))
  );

  if (!fields.length) {
    if (isTop) toast("No form fields found on this page.", []);
    return;
  }

  // ---------- fill ----------
  let filled = 0;
  const needs = new Set();
  const doneGroups = new Set();
  const combos = [];

  for (const el of fields) {
    if (el.type === "radio") {
      const groupKey = el.name || el.closest("fieldset, [role=radiogroup]");
      if (doneGroups.has(groupKey)) continue;
      doneGroups.add(groupKey);
      const group = fields.filter((r) => r.type === "radio" && (el.name ? r.name === el.name : r.closest("fieldset, [role=radiogroup]") === groupKey));
      if (group.some((r) => r.checked)) continue;
      const q = questionFor(el);
      const opt = pick(group.map((r) => ({ el: r, text: norm(labelOf(r) || r.value) })), answerFor(q));
      if (opt) {
        opt.el.click();
        mark(el, true);
        filled++;
      } else {
        mark(el, false);
        needs.add(q);
      }
      continue;
    }

    const q = questionFor(el);

    if (el.type === "checkbox") {
      // Only tick boxes the user explicitly answered "yes" to via custom answers.
      const a = norm(answerFor(q));
      if (!el.checked && /^(yes|true|check|checked)$/.test(a)) {
        el.click();
        mark(el, true);
        filled++;
      }
      continue;
    }

    if (el instanceof HTMLSelectElement) {
      const cur = el.options[el.selectedIndex];
      if (cur && !PLACEHOLDER.test(norm(cur.text))) continue;
      const opt = pick([...el.options].map((o) => ({ el: o, text: norm(o.text) })), answerFor(q));
      if (opt) {
        setValue(el, opt.el.value);
        mark(el, true);
        filled++;
      } else {
        mark(el, false);
        needs.add(q);
      }
      continue;
    }

    // text / number / email / tel / textarea
    if (el.value && el.value.trim()) continue;
    const a = answerFor(q);
    if (!a) {
      mark(el, false);
      needs.add(q);
      continue;
    }
    el.focus();
    setValue(el, el.type === "number" ? (parseFloat(a.replace(/[^0-9.]/g, "")) || "").toString() : a);
    mark(el, true);
    filled++;
    if (el.getAttribute("role") === "combobox" || el.getAttribute("aria-autocomplete")) {
      key(el, "keyup", a.slice(-1)); // some typeaheads only search on keyup
      combos.push([el, a]);
    }
  }

  for (const [el, a] of combos) {
    if (!(await chooseSuggestion(el, a))) needs.add(questionFor(el) + " (pick from the dropdown)");
  }

  toast(
    `Filled ${filled} field${filled === 1 ? "" : "s"}.` +
      (needs.size ? ` ${needs.size} need you (orange):` : " Review, then click Next."),
    [...needs]
  );

  // ---------- toast ----------
  function toast(title, items) {
    const host = window.top === window ? document : null;
    if (!host) return;
    host.getElementById("apply-autofill-toast")?.remove();
    const box = document.createElement("div");
    box.id = "apply-autofill-toast";
    box.style.cssText =
      "position:fixed;right:16px;bottom:16px;z-index:2147483647;max-width:360px;padding:12px 14px;" +
      "background:#1d2226;color:#fff;font:14px/1.4 system-ui,sans-serif;border-radius:8px;" +
      "box-shadow:0 4px 16px rgba(0,0,0,.3);cursor:pointer";
    const h = document.createElement("div");
    h.style.fontWeight = "600";
    h.textContent = title;
    box.appendChild(h);
    if (items.length) {
      const ul = document.createElement("ul");
      ul.style.cssText = "margin:6px 0 0;padding-left:18px";
      for (const it of items.slice(0, 6)) {
        const li = document.createElement("li");
        li.textContent = it.length > 90 ? it.slice(0, 87) + "…" : it;
        ul.appendChild(li);
      }
      if (items.length > 6) {
        const li = document.createElement("li");
        li.textContent = `…and ${items.length - 6} more`;
        ul.appendChild(li);
      }
      box.appendChild(ul);
      const tip = document.createElement("div");
      tip.style.cssText = "margin-top:6px;opacity:.75;font-size:12px";
      tip.textContent = "Tip: add answers for these under Custom answers in the extension settings.";
      box.appendChild(tip);
    }
    box.onclick = () => box.remove();
    document.body.appendChild(box);
    setTimeout(() => box.remove(), items.length ? 12000 : 5000);
  }
})();
