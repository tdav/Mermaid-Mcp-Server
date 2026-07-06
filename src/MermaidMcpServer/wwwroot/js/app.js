mermaid.initialize({ startOnLoad: false, theme: 'dark' });

// Re-run Mermaid on the fragment returned by HTMX into #canvas.
document.body.addEventListener('htmx:afterSwap', (e) => {
  if (e.target.id !== 'canvas') return;
  const node = e.target.querySelector('.mermaid');
  if (!node) return;
  node.removeAttribute('data-processed');
  mermaid.run({ nodes: [node] }).catch((err) => {
    e.target.innerHTML = '<div class="diagram-error">' + String(err.message || err) + '</div>';
  });
});

// Theme toggle: swap data-theme and Mermaid theme.
document.getElementById('theme-toggle').addEventListener('click', () => {
  const root = document.documentElement;
  const next = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
  root.setAttribute('data-theme', next);
  mermaid.initialize({ startOnLoad: false, theme: next === 'dark' ? 'dark' : 'default' });
  htmx.trigger('#code', 'keyup');
});

// Load code from ?code=<base64url> (preview links from the render_mermaid tool).
(function loadFromQuery() {
  const p = new URLSearchParams(location.search).get('code');
  if (!p) return;
  const b64 = p.replace(/-/g, '+').replace(/_/g, '/');
  try { document.getElementById('code').value = decodeURIComponent(escape(atob(b64))); } catch { /* ignore */ }
})();

// Populate examples dropdown; loading one fills the textarea and re-renders.
['flowchart'].forEach((t) => {
  const opt = document.createElement('option'); opt.value = t; opt.textContent = t;
  document.getElementById('examples').appendChild(opt);
});
document.getElementById('examples').addEventListener('change', async (e) => {
  if (!e.target.value) return;
  const code = await (await fetch('/examples/' + e.target.value)).text();
  document.getElementById('code').value = code;
  htmx.trigger('#code', 'keyup');
});
