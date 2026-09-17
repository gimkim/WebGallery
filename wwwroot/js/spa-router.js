(() => {
  const base = document.body.dataset.appBase || '';
  let controller, serial = 0, renderedUrl = location.href, clock;
  // Tab-local, bounded snapshots: never persist private listings to browser storage.
  const pages = new Map();
  let privateRoot;
  const cacheKey = href => {
    const url = new URL(href, location.href); url.hash = ''; url.searchParams.delete('focus');
    if (!/share/i.test(url.pathname) && privateRoot && url.searchParams.get('path') === privateRoot) url.searchParams.delete('path');
    url.searchParams.sort(); return url.href;
  };
  const signature = doc => ['.topbar', '.library-nav', '.page-shell'].map(selector => {
    const node = doc.querySelector(selector)?.cloneNode(true);
    node?.querySelectorAll('input[name="__RequestVerificationToken"]').forEach(input => input.remove());
    node?.querySelectorAll('[data-focus-path]').forEach(item => item.removeAttribute('data-focus-path'));
    return node?.outerHTML || '';
  }).join('');
  function remember(href, html, doc) {
    if (!doc.querySelector('#gallery-items') || doc.querySelector('[data-viewer-auto-open="true"], [data-video-auto-open="true"]')) return;
    const root=doc.querySelector('[data-write-path]')?.dataset.writePath.split('/')[0];
    if(root)privateRoot=root;
    const key = cacheKey(href); pages.delete(key);
    if (html.length > 4 * 1024 * 1024) return;
    // Post-create UI is a one-shot command, not part of a reusable folder snapshot.
    if (doc.querySelector('[data-open-after-create="true"], dialog[data-open-after-create][open]')) {
      const snapshot = new DOMParser().parseFromString(html, 'text/html');
      snapshot.querySelectorAll('[data-open-after-create]').forEach(panel => {
        panel.dataset.openAfterCreate = 'false';
        if (panel.tagName === 'DIALOG') panel.removeAttribute('open');
      });
      pages.set(key, { html: snapshot.documentElement.outerHTML, signature: signature(snapshot) });
    } else pages.set(key, { html, signature: signature(doc) });
    while (pages.size > 24 || [...pages.values()].reduce((sum, page) => sum + page.html.length * 2, 0) > 24 * 1024 * 1024) pages.delete(pages.keys().next().value);
  }
  function render(next) {
    next.querySelectorAll('script').forEach(script => script.remove());
    window.GalleryPages.unmount(); clearPlaceholders();
    for (const selector of ['.topbar', '.library-nav', '.page-shell']) {
      const old = document.querySelector(selector), replacement = next.querySelector(selector);
      if (old && replacement) { if (selector === '.page-shell' || old.outerHTML !== replacement.outerHTML) old.replaceWith(replacement); }
      else if (old) old.remove();
      else if (replacement) document.querySelector('.page-shell').before(replacement);
    }
    document.title = next.title;
    window.GalleryPages.mount();
  }
  remember(location.href, document.documentElement.outerHTML, document);
  history.scrollRestoration = 'manual';
  const overlay = document.createElement('section'); overlay.className = 'folder-navigation-loading spa-navigation-status'; overlay.hidden = true;
  overlay.innerHTML = '<div class="navigation-ring" role="progressbar" aria-label="Loading page"></div><span role="status">Loading</span><button type="button" aria-label="Cancel loading" title="Cancel loading">×</button><pre hidden></pre>';
  const output = overlay.querySelector('pre'), progress = overlay.querySelector('[role="progressbar"]');
  document.body.appendChild(overlay);
  let placeholders = [], busyRegions = [], positioned = [];
  function clearPlaceholders() {
    placeholders.forEach(node => node.classList.remove('spa-element-placeholder')); placeholders = [];
    for (const [node, value] of positioned) node.style.position = value;
    positioned = [];
    for (const [node, inert, busy] of busyRegions) { node.inert = inert; if (busy === null) node.removeAttribute('aria-busy'); else node.setAttribute('aria-busy', busy); }
    busyRegions = [];
  }
  function showPlaceholders() {
    clearPlaceholders();
    const main = document.querySelector('.page-shell'), gallery = main.querySelector('#gallery-items');
    if (gallery) {
      placeholders = [...main.querySelectorAll('#gallery-items .media, #gallery-items .item-info, .breadcrumbs, .gallery-summary')];
      busyRegions = [[gallery, gallery.inert, gallery.getAttribute('aria-busy')]];
    } else {
      // Preserve each section's existing dimensions rather than replacing the whole page shell.
      placeholders = [...main.querySelectorAll('.panel, .empty-state, .collection-card, .gallery-heading h1')]
        .filter(node => !node.parentElement.closest('.panel, .empty-state, .collection-card'));
      busyRegions = placeholders.map(node => [node, node.inert, node.getAttribute('aria-busy')]);
    }
    // Read all styles before any mutation: interleaving these triggered one forced
    // style recalculation per item and delayed the very placeholder meant to show progress.
    positioned = placeholders.filter(node => getComputedStyle(node).position === 'static').map(node => [node, node.style.position]);
    positioned.forEach(([node]) => { node.style.position = 'relative'; });
    placeholders.forEach(node => node.classList.add('spa-element-placeholder'));
    busyRegions.forEach(([node]) => { node.inert = true; node.setAttribute('aria-busy', 'true'); });
  }
  const safe = url => {
    const value = new URL(url, location.href);
    for (const key of [...value.searchParams.keys()]) if (!['path','sort','dir','view','itemsPerRow','focus','page'].includes(key)) value.searchParams.set(key, '[redacted]');
    return (value.pathname + value.search).replace(/[a-f0-9]{48,}/gi, '[redacted]').replace(/@root-\d+/gi, '[root]');
  };
  const pageUrl = href => {
    const url = new URL(href, location.href);
    if (url.origin !== location.origin || (url.pathname.toLowerCase() !== base.toLowerCase() && !url.pathname.toLowerCase().startsWith(base.toLowerCase() + '/'))) return false;
    const route = url.pathname.slice(base.length) || '/';
    return /^\/(?:Gallery(?:\/(?:Index|Share))?|Collections(?:\/(?:Index|SelectFolders|ShareActivity))?|Admin(?:\/(?:Index|Logs))?|Account\/Login)?\/?$/i.test(route);
  };
  const saveScroll = () => history.replaceState({ ...history.state, galleryScroll: [scrollX, scrollY] }, '', location.href);
  function finish() { clearInterval(clock); overlay.hidden = true; clearPlaceholders(); document.dispatchEvent(new Event('gallery:navigation-end')); }
  function cancel() { serial++; controller?.abort(); if (location.href !== renderedUrl) history.replaceState(history.state, '', renderedUrl); finish(); }
  function paintBeforeRequest(signal) {
    return new Promise(resolve => {
      let first, second, timer;
      const done = () => { cancelAnimationFrame(first); cancelAnimationFrame(second); clearTimeout(timer); signal.removeEventListener('abort', done); resolve(); };
      signal.addEventListener('abort', done, { once: true });
      first = requestAnimationFrame(() => { second = requestAnimationFrame(done); });
      timer = setTimeout(done, 120); // Background tabs may suspend animation frames.
      if (signal.aborted) done();
    });
  }
  overlay.querySelector('button').addEventListener('click', cancel);
  document.addEventListener('keydown', event => { if (!overlay.hidden && event.key === 'Escape') { event.preventDefault(); cancel(); } });
  async function navigate(href, options = {}) {
    const url = new URL(href, location.href);
    if (url.origin !== location.origin) { location.assign(url.href); return; }
    if (!options.pop) saveScroll();
    controller?.abort(); controller = new AbortController();
    const signal = controller.signal, mine = ++serial, started = performance.now();
    if (options.body) pages.clear();
    const cached = !options.body && pages.get(cacheKey(url.href));
    clearInterval(clock); overlay.hidden = false;
    document.dispatchEvent(new Event('gallery:navigation-start'));
    if (cached) {
      pages.delete(cacheKey(url.href)); pages.set(cacheKey(url.href), cached);
      const snapshot = new DOMParser().parseFromString(cached.html, 'text/html');
      const items = snapshot.querySelector('#gallery-items');
      if (items) items.dataset.focusPath = url.searchParams.get('focus') || '';
      if (options.pop) history.replaceState(history.state, '', url.href);
      else history.pushState({ galleryScroll: [0, 0] }, '', url.href);
      renderedUrl = url.href;
      render(snapshot); scrollTo(...(options.pop ? options.scroll || [0, 0] : [0, 0]));
      document.dispatchEvent(new Event('gallery:navigation-end'));
    } else showPlaceholders();
    overlay.querySelector('[role="status"]').textContent = 'Loading';
    output.hidden = true;
    let state = 'Preparing page request', status = 'pending', loaded = 0, total = 0;
    progress.removeAttribute('aria-valuenow'); progress.removeAttribute('aria-valuemax');
    progress.style.removeProperty('--progress');
    const report = () => {
      const percent = total ? `${Math.min(100, loaded / total * 100).toFixed(1)}%` : 'indeterminate';
      output.textContent = `${options.body ? 'POST' : 'GET'} ${safe(url.href)}\nAPI      application/vnd.webgallery.page+json\nSTATE    ${state}\nHTTP     ${status}\nRECEIVED ${(loaded / 1024).toFixed(1)} KiB / ${total ? (total / 1024).toFixed(1) + ' KiB' : 'unknown'}\nPROGRESS ${percent}\nELAPSED  ${((performance.now() - started) / 1000).toFixed(2)} s`;
      if (total) { progress.setAttribute('aria-valuemin', '0'); progress.setAttribute('aria-valuemax', '100'); progress.setAttribute('aria-valuenow', String(Math.min(100, loaded / total * 100))); progress.style.setProperty('--progress', `${Math.min(100, loaded / total * 100)}%`); }
    };
    report(); clock = setInterval(report, 100);
    try {
      await paintBeforeRequest(signal);
      if (mine !== serial || signal.aborted) return;
      state = 'Waiting for response headers'; report();
      const response = await fetch(url, { method: options.body ? 'POST' : 'GET', body: options.body, credentials: 'same-origin', cache: 'no-store', signal,
        headers: { 'X-Gallery-Page': '1', Accept: 'application/vnd.webgallery.page+json' } });
      status = `${response.status} ${response.statusText}`; state = 'Receiving page';
      // Fetch yields decoded bytes; encoded Content-Length is not a valid denominator.
      total = !response.headers.get('content-encoding') ? Number(response.headers.get('content-length')) || 0 : 0;
      report();
      if (!response.headers.get('content-type')?.includes('application/vnd.webgallery.page+json')) {
        if (cached && [401,403,404,429].includes(response.status)) { pages.clear(); location.replace(response.url || url.href); return; }
        throw new Error(`HTTP ${response.status}: page API response unavailable. Cancel to return.`);
      }
      const reader = response.body.getReader(), decoder = new TextDecoder(); let json = '';
      while (true) { const { done, value } = await reader.read(); if (done) break; loaded += value.byteLength; json += decoder.decode(value, { stream: true }); if (loaded > 64 * 1024 * 1024) { await reader.cancel(); throw new Error('Page exceeds the 64 MiB safety limit.'); } }
      json += decoder.decode(); if (mine !== serial) return;
      const data = JSON.parse(json), next = new DOMParser().parseFromString(data.html, 'text/html');
      const main = next.querySelector('.page-shell'); if (!main) throw new Error('Invalid page response');
      state = 'Rendering page'; total = loaded; report();
      const finalUrl = new URL(response.url || data.url, location.href);
      if (finalUrl.pathname === url.pathname && finalUrl.search === url.search) finalUrl.hash = url.hash;
      if (finalUrl.origin !== location.origin) throw new Error('Unexpected redirect origin');
      const sameFolder = cached && response.ok && cacheKey(finalUrl.href) === cacheKey(url.href) && next.querySelector('#gallery-items');
      if (!response.ok || !next.querySelector('#gallery-items')) pages.clear();
      else remember(finalUrl.href, data.html, next);
      if (sameFolder && signature(next) === cached.signature) { finish(); return; }
      // Keep an active viewer, selection or edited control intact; the refreshed snapshot
      // is already available on the next visit instead of tearing down the user's action.
      if (sameFolder && document.querySelector('.page-shell .selected, dialog[open], .page-shell input:focus, .page-shell select:focus')) { finish(); return; }
      const position = sameFolder ? [scrollX, scrollY] : options.pop ? options.scroll || [0, 0] : [0, 0];
      if (sameFolder) next.querySelector('#gallery-items').removeAttribute('data-focus-path');
      if (options.pop || cached) history.replaceState(history.state, '', finalUrl.href);
      else history.pushState({ galleryScroll: [0, 0] }, '', finalUrl.href);
      renderedUrl = finalUrl.href;
      render(next); finish(); scrollTo(...position);
      // The destination focus query continues to center the child folder through site.js.
    } catch (error) {
      if (mine !== serial || signal.aborted) return;
      clearInterval(clock); state = error.message; report();
      overlay.querySelector('[role="status"]').textContent = 'Loading failed';
      output.hidden = false;
      output.textContent = error.message;
      output.textContent += options.body ? '\nThe server may have saved this form. Cancel and check before retrying.' : '\nCancel to return to the previous page.';
    }
  }
  window.GalleryRouter = { navigate, invalidate: () => pages.clear() };
  window.addEventListener('click', event => {
    if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    const link = event.target.closest?.('a[href]');
    if (!link || link.hasAttribute('download') || (link.target && link.target !== '_self') || !pageUrl(link.href)) return;
    if (link.hash && link.pathname === location.pathname && link.search === location.search) return;
    event.preventDefault(); navigate(link.href);
  });
  window.addEventListener('submit', event => {
    if (event.defaultPrevented) return;
    const form = event.target, submitter = event.submitter;
    const url = new URL(submitter?.getAttribute('formaction') || form.action, location.href);
    if (url.origin !== location.origin || /download/i.test(url.pathname) || (form.target && form.target !== '_self')) return;
    const method = (submitter?.getAttribute('formmethod') || form.method || 'get').toLowerCase();
    const body = new FormData(form, submitter);
    if (method === 'get') { url.search = new URLSearchParams(body).toString(); if (!pageUrl(url.href)) return; event.preventDefault(); navigate(url.href); }
    else if (method === 'post' && url.pathname.toLowerCase().startsWith(base.toLowerCase() + '/')) { event.preventDefault(); navigate(url.href, { body }); }
  });
  window.addEventListener('popstate', event => navigate(location.href, { pop: true, scroll: event.state?.galleryScroll }));
  window.addEventListener('pagehide', () => { controller?.abort(); clearInterval(clock); window.GalleryPages.unmount(); });
  window.addEventListener('pageshow', event => { if (event.persisted) { window.GalleryPages.mount(); finish(); } });
  window.GalleryPages.mount();
})();
