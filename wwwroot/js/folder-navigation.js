(() => {
  const shell = document.querySelector('.page-shell');
  if (!shell) return;
  const overlay = document.createElement('section');
  overlay.className = 'folder-navigation-loading';
  overlay.hidden = true;
  overlay.innerHTML = '<div class="folder-navigation-heading"><div role="status" aria-live="polite"><h2>Opening folder…</h2><p></p></div><button type="button">Cancel</button></div><div class="folder-navigation-progress" role="progressbar" aria-label="Loading folder"></div><div class="folder-navigation-skeleton" aria-hidden="true"></div>';
  const message = overlay.querySelector('p');
  const skeleton = overlay.querySelector('.folder-navigation-skeleton');
  for (let i = 0; i < 24; i++) skeleton.appendChild(document.createElement('span'));
  document.body.appendChild(overlay);
  let pending = false, dispatched = false, generation = 0, timer, slowTimer, oldInert;
  function reset(stop = false) {
    if (stop && dispatched) window.stop();
    generation++;
    clearTimeout(timer); clearTimeout(slowTimer);
    overlay.hidden = true;
    if (pending) shell.inert = oldInert;
    pending = false; dispatched = false;
    document.dispatchEvent(new Event('gallery:navigation-end'));
  }
  overlay.querySelector('button').addEventListener('click', () => reset(true));
  document.addEventListener('keydown', event => { if (pending && event.key === 'Escape') { event.preventDefault(); reset(true); } });
  window.addEventListener('pageshow', () => reset());
  // Reset outgoing visual state so browser Back/Forward restores a usable page from BFCache.
  window.addEventListener('pagehide', () => { generation++; clearTimeout(timer); clearTimeout(slowTimer); overlay.hidden = true; if (pending) shell.inert = oldInert; pending = false; });
  document.addEventListener('click', event => {
    if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || pending) return;
    const link = event.target.closest?.('a.directory-card-link, a.parent-card, .breadcrumbs a[href]');
    if (!link || link.hasAttribute('download') || (link.target && link.target !== '_self')) return;
    const destination = new URL(link.href, location.href);
    if (destination.origin !== location.origin || destination.href === location.href || destination.protocol !== location.protocol) return;
    event.preventDefault();
    const grid = document.querySelector('#gallery-items');
    const columns = Math.max(1, Math.min(16, Number(grid && getComputedStyle(grid).getPropertyValue('--columns')) || 8));
    skeleton.style.setProperty('--navigation-columns', columns);
    overlay.classList.toggle('is-list', !!grid?.classList.contains('list-view'));
    message.textContent = link.querySelector('strong')?.textContent?.trim() || link.textContent.trim() || 'Loading folder';
    oldInert = shell.inert; shell.inert = true;
    pending = true; dispatched = false; overlay.hidden = false;
    document.dispatchEvent(new CustomEvent('gallery:navigation-start', { detail: { url: destination.href } }));
    const current = ++generation;
    const navigate = () => {
      if (!pending || current !== generation || dispatched) return;
      dispatched = true;
      try { location.assign(destination.href); } catch { reset(); }
    };
    // Give the placeholder a paint before beginning a full MVC document navigation.
    requestAnimationFrame(() => requestAnimationFrame(navigate));
    timer = setTimeout(navigate, 150); // RAF can pause in a background tab.
    slowTimer = setTimeout(() => { if (pending) message.textContent = 'Still loading. You can cancel and try again.'; }, 12000);
  });
})();
