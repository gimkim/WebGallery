(() => {
  const key = 'gallery-navigation-trace-v1';
  let waiting = false, clock, panel, started;
  const store = value => { try { value ? sessionStorage.setItem(key, JSON.stringify(value)) : sessionStorage.removeItem(key); } catch {} };
  const safeRoute = value => {
    const url = new URL(value, location.href);
    const query = new URLSearchParams();
    for (const [name, value] of url.searchParams) {
      query.append(name, ['path', 'sort', 'dir', 'view', 'itemsPerRow', 'focus'].includes(name)
        ? value.replace(/@root-\d+/gi, '[root]').replace(/[a-f0-9]{48,}/gi, '[redacted]') : '[redacted]');
    }
    return url.pathname.replace(/[a-f0-9]{48,}/gi, '[redacted]') + (query.size ? '?' + query.toString() : '');
  };
  const seconds = ms => `${Math.max(0, ms / 1000).toFixed(2)} s`;
  const bytes = count => count >= 1048576 ? `${(count / 1048576).toFixed(2)} MiB` : `${(count / 1024).toFixed(1)} KiB`;
  function makePanel(parent) {
    const node = document.createElement('section');
    node.className = 'navigation-console';
    node.setAttribute('aria-label', 'Navigation diagnostics');
    const heading = document.createElement('div'); heading.className = 'navigation-console-title'; heading.textContent = '>_ NAVIGATION TRACE';
    const output = document.createElement('pre'); output.setAttribute('aria-live', 'off');
    node.append(heading, output); parent.appendChild(node);
    return { node, output };
  }
  document.addEventListener('gallery:navigation-start', event => {
    const host = document.querySelector('.folder-navigation-loading');
    if (!host || !event.detail?.url) return;
    clearInterval(clock); panel?.node.remove();
    panel = makePanel(host); waiting = true; started = performance.now();
    const route = safeRoute(event.detail.url);
    store({ route, startedAt: Date.now() });
    const render = () => { panel.output.textContent = [
      `GET ${route}`, 'TYPE     MVC document · same-origin',
      'STATE    awaiting destination document',
      'RESPONSE pending · available after navigation',
      'PROGRESS indeterminate · browser-managed transfer',
      'RECEIVED unavailable until document arrives',
      `ELAPSED  ${seconds(performance.now() - started)}`,
      'QUEUE    gallery thumbnail requests suspended',
      '         [Esc] cancel'
    ].join('\n'); };
    render(); clock = setInterval(render, 100);
  });
  document.addEventListener('gallery:navigation-end', () => {
    if (!waiting) return;
    waiting = false; clearInterval(clock); panel?.node.remove(); store(null);
  });
  window.addEventListener('pagehide', () => clearInterval(clock));
  let previous;
  try { previous = JSON.parse(sessionStorage.getItem(key) || 'null'); sessionStorage.removeItem(key); } catch {}
  if (!previous || typeof previous.startedAt !== 'number' || typeof previous.route !== 'string' || Date.now() - previous.startedAt > 120000) return;
  // Native document navigation exposes its real response metrics only in the destination document.
  window.addEventListener('load', () => {
    const timing = performance.getEntriesByType('navigation')[0];
    if (!timing || timing.type === 'back_forward') return;
    const result = makePanel(document.body); result.node.classList.add('navigation-console-complete');
    const status = timing.responseStatus > 0 ? String(timing.responseStatus) : 'not exposed by this browser';
    const completed = timing.responseEnd > 0;
    result.output.textContent = [
      `GET ${previous.route.slice(0, 1800)}`, `FINAL    ${safeRoute(location.href)}`,
      `HTTP     ${status}`, `PROGRESS ${completed ? '100% · document received' : 'not exposed'}`,
      `BODY     ${timing.decodedBodySize > 0 ? bytes(timing.decodedBodySize) + ' decoded' : 'not exposed'}`,
      `TRANSFER ${timing.transferSize > 0 ? bytes(timing.transferSize) + ' including headers' : '0 B / cache or browser unavailable'}`,
      `WAIT     ${timing.requestStart > 0 ? seconds(timing.responseStart - timing.requestStart) + ' to first byte' : 'not exposed'}`,
      `DOWNLOAD ${completed ? seconds(timing.responseEnd - timing.responseStart) : 'not exposed'}`,
      `TOTAL    ${seconds(Date.now() - previous.startedAt)} · navigation elapsed`,
      '         Document only; thumbnails load separately.'
    ].join('\n');
    const close = document.createElement('button'); close.type = 'button'; close.textContent = 'Dismiss';
    close.addEventListener('click', () => result.node.remove()); result.node.appendChild(close);
    setTimeout(() => result.node.remove(), 8000);
  }, { once: true });
})();
