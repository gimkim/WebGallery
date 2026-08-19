(() => {
  document.querySelectorAll('[data-cooldown]').forEach(message => {
    const value = message.querySelector('[data-cooldown-value]');
    const startedAt = Date.now();
    const initialSeconds = Math.max(0, Number.parseInt(message.dataset.cooldown || '0', 10));
    const update = () => {
      const elapsed = Math.floor((Date.now() - startedAt) / 1000);
      const remaining = Math.max(0, initialSeconds - elapsed);
      const minutes = Math.floor(remaining / 60);
      const seconds = remaining % 60;
      if (value) value.textContent = remaining > 0 ? `${minutes}:${seconds.toString().padStart(2, '0')}` : 'now';
      if (remaining > 0) window.setTimeout(update, 250);
    };
    update();
  });

  const gallery = document.querySelector('#gallery-items');
  const columns = document.querySelector('#columns');
  const columnsValue = document.querySelector('#columns-value');
  const densityStepButtons = document.querySelectorAll('[data-density-step]');
  const viewButtons = document.querySelectorAll('[data-view]');
  const collectionFolderGrids = document.querySelectorAll('.collection-folder-grid');
  const collectionList = document.querySelector('.collection-list');
  const collectionCards = document.querySelectorAll('[data-collection-collapsible]');
  const galleryToolbar = document.querySelector('.gallery-toolbar');
  const gallerySearch = document.querySelector('#gallery-search');
  const galleryTypeFilter = document.querySelector('#gallery-type-filter');
  const gallerySortSelect = document.querySelector('#gallery-sort-select');
  const savedView = gallery?.dataset.initialView || localStorage.getItem('gim-gallery-view') || 'grid';
  const savedColumns = Number(gallery?.dataset.initialColumns || localStorage.getItem('gim-gallery-columns'));

  collectionFolderGrids.forEach(grid => {
    const defaultColumns = Number(grid.dataset.defaultColumns) || 8;
    const gridColumns = savedColumns >= 2 && savedColumns <= 10 ? savedColumns : defaultColumns;
    const folderCount = Math.max(1, Number(grid.dataset.folderCount) || gridColumns);
    grid.style.setProperty('--columns', Math.min(gridColumns, folderCount));
  });

  let collectionLayoutFrame = 0;
  const updateCollectionLayout = () => {
    collectionLayoutFrame = 0;
    if (!collectionList) return;

    const gap = Number.parseFloat(getComputedStyle(collectionList).columnGap) || 18;
    const availableTracks = Math.max(1, Math.floor((collectionList.clientWidth + gap) / (360 + gap)));
    const requestedColumns = Math.max(2, Math.min(10, Number(columns?.value || savedColumns || 8)));
    collectionCards.forEach(card => {
      const folderCount = Math.max(0, Number(card.dataset.folderCount) || 0);
      const foldersSection = card.querySelector('[data-section-key="folders"]');
      const foldersVisible = !card.classList.contains('collection-collapsed')
        && foldersSection
        && !foldersSection.classList.contains('collection-section-collapsed');
      const visibleFolderColumns = Math.min(folderCount, requestedColumns);
      const desiredTracks = foldersVisible && visibleFolderColumns > 0
        ? Math.ceil(visibleFolderColumns / 2)
        : 1;
      card.style.setProperty('--collection-span', Math.min(availableTracks, desiredTracks));
    });
  };
  const scheduleCollectionLayout = () => {
    if (collectionLayoutFrame) return;
    collectionLayoutFrame = window.requestAnimationFrame(updateCollectionLayout);
  };

  function setView(view) {
    const normalizedView = view === 'list' ? 'list' : 'grid';
    gallery?.classList.toggle('list-view', normalizedView === 'list');
    viewButtons.forEach(button => {
      const active = button.dataset.view === normalizedView;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', active ? 'true' : 'false');
    });
    localStorage.setItem('gim-gallery-view', normalizedView);
  }
  viewButtons.forEach(button => button.addEventListener('click', () => setView(button.dataset.view)));
  setView(savedView);
  if (columns) {
    if (savedColumns >= 2 && savedColumns <= 10) columns.value = savedColumns;
    const updateColumns = () => {
      gallery?.style.setProperty('--columns', columns.value);
      collectionFolderGrids.forEach(grid => {
        const folderCount = Math.max(1, Number(grid.dataset.folderCount) || Number(columns.value));
        grid.style.setProperty('--columns', Math.min(Number(columns.value), folderCount));
      });
      if (columnsValue) columnsValue.textContent = columns.value;
      densityStepButtons.forEach(button => {
        const nextValue = Number(columns.value) + Number(button.dataset.densityStep || 0);
        button.disabled = nextValue < Number(columns.min) || nextValue > Number(columns.max);
      });
      localStorage.setItem('gim-gallery-columns', columns.value);
      scheduleCollectionLayout();
    };
    columns.addEventListener('input', updateColumns);
    densityStepButtons.forEach(button => button.addEventListener('click', () => {
      const step = Number(button.dataset.densityStep || 0);
      const nextValue = Math.max(Number(columns.min), Math.min(Number(columns.max), Number(columns.value) + step));
      if (nextValue === Number(columns.value)) return;
      columns.value = nextValue;
      columns.dispatchEvent(new Event('input', { bubbles: true }));
    }));
    updateColumns();
  }

  const updateStickyListHeaderOffset = () => {
    if (!galleryToolbar) return;
    document.documentElement.style.setProperty('--gallery-toolbar-height', `${galleryToolbar.offsetHeight}px`);
  };
  if (galleryToolbar) {
    if ('ResizeObserver' in window) new ResizeObserver(updateStickyListHeaderOffset).observe(galleryToolbar);
    else window.addEventListener('resize', updateStickyListHeaderOffset);
    updateStickyListHeaderOffset();
  }
  gallerySortSelect?.addEventListener('change', () => {
    if (!gallerySortSelect.value) return;
    cancelThumbnailRequests(false);
    window.location.assign(gallerySortSelect.value);
  });

  document.querySelectorAll('form[data-create-share]').forEach(form => {
    form.addEventListener('submit', () => {
      form.elements.namedItem('itemsPerRow').value = columns?.value || localStorage.getItem('gim-gallery-columns') || '8';
      form.elements.namedItem('viewMode').value = document.querySelector('[data-view].active')?.dataset.view === 'list' ? 'list' : 'grid';
    });
  });

  document.querySelectorAll('[data-collection-collapsible]').forEach(card => {
    const collectionId = card.dataset.collectionId;
    const toggle = card.querySelector('[data-collection-toggle]');
    const body = card.querySelector('[data-collection-body]');
    if (!collectionId || !toggle || !body) return;

    const storageKey = `gim-collection-expanded-${collectionId}`;
    const forceExpanded = card.dataset.forceExpanded === 'true';
    let expanded = forceExpanded || localStorage.getItem(storageKey) === 'true';
    const render = persist => {
      body.hidden = !expanded;
      toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
      const action = expanded ? 'Collapse' : 'Expand';
      toggle.setAttribute('aria-label', `${action} ${toggle.dataset.collectionName || 'collection'}`);
      toggle.title = `${action} collection`;
      card.classList.toggle('collection-collapsed', !expanded);
      if (persist) localStorage.setItem(storageKey, expanded ? 'true' : 'false');
      scheduleCollectionLayout();
    };
    toggle.addEventListener('click', () => {
      expanded = !expanded;
      render(true);
    });
    card.addEventListener('collection:set-expanded', event => {
      expanded = event.detail.expanded;
      render(true);
    });
    render(forceExpanded);
  });

  const setAllCollectionsExpanded = expanded => {
    document.querySelectorAll('[data-collection-collapsible]').forEach(card => {
      card.dispatchEvent(new CustomEvent('collection:set-expanded', { detail: { expanded } }));
    });
  };
  document.querySelector('[data-collections-expand-all]')?.addEventListener('click', () => setAllCollectionsExpanded(true));
  document.querySelector('[data-collections-collapse-all]')?.addEventListener('click', () => setAllCollectionsExpanded(false));

  document.querySelectorAll('[data-collection-section]').forEach(section => {
    const collectionId = section.dataset.collectionId;
    const sectionKey = section.dataset.sectionKey;
    const toggle = section.querySelector('[data-section-toggle]');
    const body = section.querySelector('[data-section-body]');
    if (!collectionId || !sectionKey || !toggle || !body) return;

    const storageKey = `gim-collection-${collectionId}-${sectionKey}-expanded`;
    const savedState = localStorage.getItem(storageKey);
    const forceExpanded = section.dataset.forceExpanded === 'true';
    let expanded = forceExpanded || (savedState === null
      ? section.dataset.defaultExpanded !== 'false'
      : savedState === 'true');
    const render = persist => {
      body.hidden = !expanded;
      toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
      const sectionName = sectionKey === 'share-links' ? 'share links' : 'folders';
      const action = expanded ? 'Collapse' : 'Expand';
      toggle.setAttribute('aria-label', `${action} ${sectionName}`);
      toggle.title = `${action} ${sectionName}`;
      section.classList.toggle('collection-section-collapsed', !expanded);
      if (persist) localStorage.setItem(storageKey, expanded ? 'true' : 'false');
      scheduleCollectionLayout();
    };
    toggle.addEventListener('click', () => {
      expanded = !expanded;
      render(true);
    });
    render(forceExpanded);
  });

  if (collectionList) {
    if ('ResizeObserver' in window) {
      new ResizeObserver(scheduleCollectionLayout).observe(collectionList);
    } else {
      window.addEventListener('resize', scheduleCollectionLayout);
    }
    scheduleCollectionLayout();
  }

  const folderSharesSection = document.querySelector('[data-folder-shares-collapsible]');
  const folderSharesToggle = folderSharesSection?.querySelector('[data-folder-shares-toggle]');
  const folderSharesBody = folderSharesSection?.querySelector('[data-folder-shares-body]');
  if (folderSharesSection && folderSharesToggle && folderSharesBody) {
    const storageKey = 'gim-folder-shares-expanded';
    let expanded = localStorage.getItem(storageKey) !== 'false';
    const render = persist => {
      folderSharesBody.hidden = !expanded;
      folderSharesToggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
      folderSharesToggle.setAttribute('aria-label', expanded ? 'Collapse folder share links' : 'Expand folder share links');
      folderSharesToggle.title = expanded ? 'Collapse folder share links' : 'Expand folder share links';
      folderSharesSection.classList.toggle('folder-share-links-collapsed', !expanded);
      if (persist) localStorage.setItem(storageKey, expanded ? 'true' : 'false');
    };
    folderSharesToggle.addEventListener('click', () => {
      expanded = !expanded;
      render(true);
    });
    render(false);
  }

  const thumbnailStates = new WeakMap();
  const thumbnailImages = [...document.querySelectorAll('img[data-thumbnail-src]')];
  const maximumThumbnailRequests = 12;
  const pendingThumbnails = [];
  let activeThumbnailRequests = 0;
  let thumbnailRequestsSuspended = false;
  let thumbnailResumeTimer = null;
  const thumbnailHost = image => image.closest('.thumbnail-slot, .image-button');
  function setThumbnailReady(image) {
    const state = thumbnailStates.get(image);
    if (state) {
      state.loaded = true;
      state.assigned = false;
    }
    image.classList.add('thumbnail-loaded');
    image.classList.remove('thumbnail-error');
    const host = thumbnailHost(image);
    host?.classList.remove('thumbnail-loading', 'thumbnail-failed');
    host?.classList.add('thumbnail-ready');
  }
  function setThumbnailError(image) {
    const state = thumbnailStates.get(image);
    if (state) state.assigned = false;
    image.classList.add('thumbnail-error');
    const host = thumbnailHost(image);
    host?.classList.remove('thumbnail-loading', 'thumbnail-ready');
    host?.classList.add('thumbnail-failed');
  }
  thumbnailImages.forEach(image => {
    image.addEventListener('load', () => setThumbnailReady(image));
    image.addEventListener('error', () => setThumbnailError(image));
  });
  function assignThumbnailBlob(image, state, blob) {
    const objectUrl = URL.createObjectURL(blob);
    state.assigned = true;
    image.addEventListener('load', () => URL.revokeObjectURL(objectUrl), { once: true });
    image.addEventListener('error', () => URL.revokeObjectURL(objectUrl), { once: true });
    image.src = objectUrl;
  }
  function queueThumbnail(image) {
    const state = thumbnailStates.get(image);
    if (!state || state.loaded || state.assigned || state.controller || state.queued || state.probing || !state.visible) return;
    state.queued = true;
    pendingThumbnails.push(image);
    pumpThumbnailQueue();
  }
  async function probeFolderThumbnailCache(image, state) {
    state.cacheProbeAttempted = true;
    state.probing = true;
    let cachedBlob = null;
    try {
      const response = await fetch(image.dataset.thumbnailSrc, {
        credentials: 'same-origin',
        mode: 'same-origin',
        cache: 'only-if-cached'
      });
      if (response.ok) cachedBlob = await response.blob();
    } catch {
      // Browsers without only-if-cached support fall through to the bounded queue.
    } finally {
      state.probing = false;
    }

    if (cachedBlob && state.visible && !thumbnailRequestsSuspended) {
      assignThumbnailBlob(image, state, cachedBlob);
    } else if (state.visible && !thumbnailRequestsSuspended) {
      queueThumbnail(image);
    }
  }
  function requestThumbnail(image) {
    const state = thumbnailStates.get(image);
    if (!state || state.loaded || state.assigned || state.controller || state.queued || state.probing || !state.visible) return;
    if (image.closest('.folder-cover-grid') && !state.cacheProbeAttempted) {
      probeFolderThumbnailCache(image, state);
    } else {
      queueThumbnail(image);
    }
  }
  function pumpThumbnailQueue() {
    if (thumbnailRequestsSuspended) return;
    while (activeThumbnailRequests < maximumThumbnailRequests && pendingThumbnails.length > 0) {
      const image = pendingThumbnails.shift();
      const state = thumbnailStates.get(image);
      if (!state?.queued) continue;
      state.queued = false;
      if (state.loaded || state.assigned || state.controller || !state.visible) continue;
      startThumbnailRequest(image, state);
    }
  }
  function startThumbnailRequest(image, state) {
    const host = thumbnailHost(image);
    host?.classList.remove('thumbnail-failed');
    host?.classList.add('thumbnail-loading');
    const controller = new AbortController();
    state.controller = controller;
    activeThumbnailRequests++;
    fetch(image.dataset.thumbnailSrc, {
      signal: controller.signal,
      credentials: 'same-origin',
      cache: 'default',
      priority: 'low',
      headers: { 'X-Thumbnail-Priority': 'visible' }
    }).then(response => {
      if (!response.ok) {
        const error = new Error(`Thumbnail request failed: ${response.status}`);
        error.status = response.status;
        throw error;
      }
      return response.blob();
    }).then(blob => {
      if (!state.visible || controller.signal.aborted) return;
      assignThumbnailBlob(image, state, blob);
    }).catch(error => {
      if (error.name === 'AbortError') return;
      if (error.status === 503 && state.visible) {
        state.retry = setTimeout(() => {
          state.retry = null;
          queueThumbnail(image);
        }, 750);
      } else {
        setThumbnailError(image);
      }
    }).finally(() => {
      if (state.controller === controller) state.controller = null;
      activeThumbnailRequests = Math.max(0, activeThumbnailRequests - 1);
      if (!thumbnailRequestsSuspended && state.visible && !state.loaded && controller.signal.aborted) queueThumbnail(image);
      pumpThumbnailQueue();
    });
  }
  function cancelThumbnailRequests(allowResume) {
    thumbnailRequestsSuspended = true;
    pendingThumbnails.length = 0;
    thumbnailImages.forEach(image => {
      const state = thumbnailStates.get(image);
      if (!state) return;
      state.queued = false;
      if (state.retry) {
        clearTimeout(state.retry);
        state.retry = null;
      }
      state.controller?.abort();
    });
    if (thumbnailResumeTimer) clearTimeout(thumbnailResumeTimer);
    thumbnailResumeTimer = null;
    if (allowResume) {
      thumbnailResumeTimer = setTimeout(() => {
        thumbnailResumeTimer = null;
        if (document.visibilityState === 'hidden') return;
        thumbnailRequestsSuspended = false;
        thumbnailImages.forEach(requestThumbnail);
      }, 1200);
    }
  }
  function resumeThumbnailRequests() {
    if (document.visibilityState === 'hidden') return;
    thumbnailRequestsSuspended = false;
    thumbnailImages.forEach(requestThumbnail);
  }
  if ('IntersectionObserver' in window) {
    const thumbnailObserver = new IntersectionObserver(entries => {
      entries.forEach(entry => {
        const image = entry.target;
        const state = thumbnailStates.get(image);
        state.visible = entry.isIntersecting;
        if (entry.isIntersecting) {
          requestThumbnail(image);
        } else {
          state.queued = false;
          if (state.retry) {
            clearTimeout(state.retry);
            state.retry = null;
          }
          state.controller?.abort();
        }
      });
    }, { rootMargin: '0px', threshold: 0.01 });
    thumbnailImages.forEach(image => {
      thumbnailStates.set(image, { visible: false, loaded: false, assigned: false, queued: false, probing: false, cacheProbeAttempted: false, controller: null, retry: null });
      thumbnailObserver.observe(image);
    });
  } else {
    thumbnailImages.forEach(image => {
      thumbnailStates.set(image, { visible: true, loaded: false, assigned: false, queued: false, probing: false, cacheProbeAttempted: false, controller: null, retry: null });
      requestThumbnail(image);
    });
  }

  document.addEventListener('click', event => {
    if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    const link = event.target.closest('a[href]');
    if (!link || link.target === '_blank') return;
    cancelThumbnailRequests(true);
  }, true);
  document.addEventListener('submit', () => cancelThumbnailRequests(true), true);
  window.addEventListener('pagehide', () => cancelThumbnailRequests(false));

  const focusPath = gallery?.dataset.focusPath;
  if (focusPath) {
    const focusCard = [...gallery.querySelectorAll('[data-relative-path]')]
      .find(card => card.dataset.relativePath === focusPath);
    if (focusCard) requestAnimationFrame(() => {
      focusCard.scrollIntoView({ block: 'center' });
      focusCard.classList.add('return-focus');
      setTimeout(() => focusCard.classList.remove('return-focus'), 1800);
    });
  }

  const fileSelects = [...document.querySelectorAll('.file-select')];
  const folderSelects = [...document.querySelectorAll('.folder-select')];
  const selectAll = document.querySelector('#select-all');
  const selectedButton = document.querySelector('#download-selected');
  const selectedCount = document.querySelector('#selected-count');
  const selectionToolbar = document.querySelector('#selection-toolbar');
  const galleryToolbarMain = document.querySelector('[data-gallery-toolbar-main]');
  const clearSelection = document.querySelector('#clear-selection');
  const visibleSummary = document.querySelector('#gallery-visible-summary');
  const filterEmpty = document.querySelector('#gallery-filter-empty');
  const clearGalleryFilter = document.querySelector('#clear-gallery-filter');
  const filterableCards = [...(gallery?.querySelectorAll('[data-item-kind]') || [])];
  const visibleFileSelects = () => fileSelects.filter(input => !input.closest('.gallery-card')?.hidden);
  function updateSelection() {
    const count = fileSelects.filter(input => input.checked).length;
    const visibleInputs = visibleFileSelects();
    const visibleSelectedCount = visibleInputs.filter(input => input.checked).length;
    fileSelects.forEach(input => input.closest('.gallery-card')?.classList.toggle('selected', input.checked));
    if (selectedCount) selectedCount.textContent = count;
    if (selectedButton) selectedButton.disabled = count === 0;
    if (selectionToolbar) selectionToolbar.hidden = count === 0;
    if (galleryToolbarMain) galleryToolbarMain.hidden = count > 0;
    galleryToolbar?.classList.toggle('selection-active', count > 0);
    if (selectAll) {
      selectAll.checked = visibleInputs.length > 0 && visibleSelectedCount === visibleInputs.length;
      selectAll.indeterminate = visibleSelectedCount > 0 && visibleSelectedCount < visibleInputs.length;
    }
    updateStickyListHeaderOffset();
  }
  let selectionAnchor = null;
  fileSelects.forEach((input, index) => input.addEventListener('change', () => {
    selectionAnchor = index;
    updateSelection();
  }));
  selectAll?.addEventListener('change', () => {
    visibleFileSelects().forEach(input => input.checked = selectAll.checked);
    updateSelection();
  });
  clearSelection?.addEventListener('click', () => {
    fileSelects.forEach(input => input.checked = false);
    selectionAnchor = null;
    updateSelection();
  });

  function applyGalleryFilter() {
    const query = (gallerySearch?.value || '').trim().toLocaleLowerCase();
    const kind = galleryTypeFilter?.value || 'all';
    let visibleCount = 0;
    filterableCards.forEach(card => {
      const matchesName = !query || (card.dataset.name || '').toLocaleLowerCase().includes(query);
      const matchesKind = kind === 'all' || card.dataset.itemKind === kind;
      card.hidden = !(matchesName && matchesKind);
      if (!card.hidden) visibleCount++;
    });
    const filtering = query.length > 0 || kind !== 'all';
    if (visibleSummary) {
      visibleSummary.hidden = !filtering;
      visibleSummary.textContent = `${visibleCount} of ${filterableCards.length} shown`;
    }
    if (filterEmpty) filterEmpty.hidden = visibleCount > 0 || !filtering;
    gallery?.classList.toggle('has-no-filter-results', visibleCount === 0 && filtering);
    updateSelection();
  }
  gallerySearch?.addEventListener('input', applyGalleryFilter);
  galleryTypeFilter?.addEventListener('change', applyGalleryFilter);
  clearGalleryFilter?.addEventListener('click', () => {
    if (gallerySearch) gallerySearch.value = '';
    if (galleryTypeFilter) galleryTypeFilter.value = 'all';
    applyGalleryFilter();
    gallerySearch?.focus();
  });
  updateSelection();

  const addFoldersButton = document.querySelector('#add-folders-to-collection');
  const selectedFolderCount = document.querySelector('#selected-folder-count');
  function updateFolderSelection() {
    const count = folderSelects.filter(input => input.checked !== (input.dataset.initialChecked === 'true')).length;
    folderSelects.forEach(input => input.closest('.gallery-card')?.classList.toggle('selected', input.checked));
    if (selectedFolderCount) selectedFolderCount.textContent = count;
    if (addFoldersButton) addFoldersButton.disabled = count === 0;
  }
  folderSelects.forEach(input => input.addEventListener('change', updateFolderSelection));
  updateFolderSelection();

  document.querySelectorAll('[data-toggle-panel]').forEach(button => button.addEventListener('click', () => {
    const panel = document.getElementById(button.dataset.togglePanel);
    if (panel) panel.hidden = !panel.hidden;
  }));
  document.querySelectorAll('[data-open-after-create="true"]').forEach(panel => {
    panel.hidden = false;
  });
  document.querySelectorAll('[data-copy]').forEach(button => button.addEventListener('click', async () => {
    await navigator.clipboard.writeText(button.dataset.copy);
    const old = button.textContent;
    button.textContent = 'Copied';
    setTimeout(() => button.textContent = old, 1200);
  }));
  document.querySelectorAll('form[data-confirm]').forEach(form => form.addEventListener('submit', event => {
    if (!confirm(form.dataset.confirm)) event.preventDefault();
  }));

  const dialog = document.querySelector('#image-viewer');
  const viewerImage = document.querySelector('#viewer-image');
  const viewerName = document.querySelector('#viewer-name');
  const viewerDownload = document.querySelector('#viewer-download');
  const viewerStage = document.querySelector('.viewer-stage');
  const viewerLoading = document.querySelector('.viewer-loading');
  const viewerLoadingLabel = document.querySelector('#viewer-loading-label');
  const viewerProgress = document.querySelector('#viewer-progress');
  const viewerProgressDetail = document.querySelector('#viewer-progress-detail');
  const viewerPrevious = document.querySelector('#viewer-previous');
  const viewerNext = document.querySelector('#viewer-next');
  const viewerCounter = document.querySelector('#viewer-counter');
  const viewerInfo = document.querySelector('#viewer-info');
  const viewerInfoType = document.querySelector('#viewer-info-type');
  const viewerInfoDimensions = document.querySelector('#viewer-info-dimensions');
  const viewerInfoSize = document.querySelector('#viewer-info-size');
  const viewerInfoModified = document.querySelector('#viewer-info-modified');
  const viewerFilmstrip = document.querySelector('#viewer-filmstrip');
  const viewerButtons = [...document.querySelectorAll('[data-viewer-src]')];
  let viewerIsLarge = false;
  let viewerIsOriginal = false;
  let viewerFitScale = 1;
  let viewerScale = 1;
  let viewerDisplayingPlaceholder = false;
  let viewerPlaceholderButton = null;
  let currentViewerIndex = -1;
  let pendingImageClick = null;
  let suppressGalleryClickUntil = 0;
  const viewerTouchPointers = new Map();
  let viewerTouchGesture = null;
  let viewerPinch = null;
  let viewerLastTap = null;
  let suppressViewerClickUntil = 0;
  let viewerLoadGeneration = 0;
  let viewerThumbnailLoader = null;
  let viewerOriginalLoader = null;
  let viewerOriginalRequest = null;
  let viewerOriginalCacheProbe = null;
  let viewerNeighborPrefetchController = null;
  let viewerNeighborPrefetchTimer = null;
  let viewerNeighborPrefetchUsesIdle = false;
  const viewerFullImageCache = new Map();
  const maximumViewerCacheEntries = 4;
  const maximumViewerCacheBytes = 256 * 1024 * 1024;
  let viewerFullImageCacheBytes = 0;

  function getViewerAvailableSize() {
    const stageStyle = getComputedStyle(viewerStage);
    return {
      width: Math.max(1, viewerStage.clientWidth
        - parseFloat(stageStyle.paddingLeft) - parseFloat(stageStyle.paddingRight)),
      height: Math.max(1, viewerStage.clientHeight
        - parseFloat(stageStyle.paddingTop) - parseFloat(stageStyle.paddingBottom))
    };
  }

  function setViewerScale(scale, resetScroll = false) {
    if (!viewerImage || !viewerStage || !viewerImage.naturalWidth || !viewerImage.naturalHeight) return;

    const available = getViewerAvailableSize();
    viewerFitScale = Math.min(1, available.width / viewerImage.naturalWidth, available.height / viewerImage.naturalHeight);
    viewerIsLarge = viewerFitScale < 1;
    viewerScale = Math.max(viewerFitScale, Math.min(4, scale));
    viewerIsOriginal = viewerScale > viewerFitScale + 0.001;
    const renderedWidth = Math.max(1, Math.round(viewerImage.naturalWidth * viewerScale));
    const renderedHeight = Math.max(1, Math.round(viewerImage.naturalHeight * viewerScale));
    viewerImage.style.width = `${renderedWidth}px`;
    viewerImage.style.height = `${renderedHeight}px`;
    viewerImage.classList.toggle('original-size', viewerIsOriginal);
    viewerImage.classList.toggle('zoomable', viewerIsLarge);
    viewerStage.classList.toggle('original-size', viewerIsOriginal);
    viewerStage.classList.toggle('original-overflow-x', viewerIsOriginal && renderedWidth > available.width);
    viewerStage.classList.toggle('original-overflow-y', viewerIsOriginal && renderedHeight > available.height);
    if (resetScroll) viewerStage.scrollTo(0, 0);
    viewerImage.title = viewerIsLarge
      ? (viewerIsOriginal ? 'Click to fit image' : 'Click to view original size 1:1')
      : '';
  }

  function setViewerSize(original) {
    if (!viewerImage || !viewerStage || !viewerImage.naturalWidth || !viewerImage.naturalHeight) return;
    const available = getViewerAvailableSize();
    const fitScale = Math.min(1, available.width / viewerImage.naturalWidth, available.height / viewerImage.naturalHeight);
    setViewerScale(original ? 1 : fitScale, !original);
  }

  function setViewerPlaceholderSize(button) {
    if (!viewerImage || !viewerStage || !viewerImage.naturalWidth || !viewerImage.naturalHeight) return;
    const available = getViewerAvailableSize();
    const maxWidth = Number.parseInt(button.dataset.viewerThumbnailWidth || '0', 10);
    const maxHeight = Number.parseInt(button.dataset.viewerThumbnailHeight || '0', 10);
    const originalWidth = Number.parseInt(button.dataset.viewerOriginalWidth || '0', 10);
    const originalHeight = Number.parseInt(button.dataset.viewerOriginalHeight || '0', 10);
    let renderedWidth;
    let renderedHeight;
    let scale;
    if (originalWidth > 0 && originalHeight > 0) {
      const originalScale = Math.min(1, available.width / originalWidth, available.height / originalHeight);
      renderedWidth = Math.max(1, Math.round(originalWidth * originalScale));
      renderedHeight = Math.max(1, Math.round(originalHeight * originalScale));
      scale = Math.min(renderedWidth / viewerImage.naturalWidth, renderedHeight / viewerImage.naturalHeight);
    } else {
      const representsLargerOriginal = (maxWidth > 0 && viewerImage.naturalWidth >= maxWidth - 1)
        || (maxHeight > 0 && viewerImage.naturalHeight >= maxHeight - 1);
      const availableScale = Math.min(available.width / viewerImage.naturalWidth, available.height / viewerImage.naturalHeight);
      scale = representsLargerOriginal ? availableScale : Math.min(1, availableScale);
      renderedWidth = Math.max(1, Math.round(viewerImage.naturalWidth * scale));
      renderedHeight = Math.max(1, Math.round(viewerImage.naturalHeight * scale));
    }
    viewerImage.style.width = `${renderedWidth}px`;
    viewerImage.style.height = `${renderedHeight}px`;
    viewerImage.classList.remove('original-size', 'zoomable');
    viewerImage.title = '';
    viewerStage.classList.remove('original-size', 'original-overflow-x', 'original-overflow-y');
    viewerStage.scrollTo(0, 0);
    viewerFitScale = scale;
    viewerScale = scale;
    viewerIsLarge = false;
    viewerIsOriginal = false;
    viewerDisplayingPlaceholder = true;
    viewerPlaceholderButton = button;
  }

  function setViewerScaleAt(clientX, clientY, scale, anchorX, anchorY) {
    const oldImageRect = viewerImage.getBoundingClientRect();
    const oldStageRect = viewerStage.getBoundingClientRect();
    const xRatio = anchorX ?? Math.max(0, Math.min(1, (clientX - oldImageRect.left) / oldImageRect.width));
    const yRatio = anchorY ?? Math.max(0, Math.min(1, (clientY - oldImageRect.top) / oldImageRect.height));
    const targetViewportX = clientX - oldStageRect.left;
    const targetViewportY = clientY - oldStageRect.top;

    setViewerScale(scale);
    const newImageRect = viewerImage.getBoundingClientRect();
    const newStageRect = viewerStage.getBoundingClientRect();
    const targetContentX = viewerStage.scrollLeft + newImageRect.left - newStageRect.left + (xRatio * newImageRect.width);
    const targetContentY = viewerStage.scrollTop + newImageRect.top - newStageRect.top + (yRatio * newImageRect.height);
    viewerStage.scrollTo(targetContentX - targetViewportX, targetContentY - targetViewportY);
  }

  function zoomViewerAt(clientX, clientY) {
    setViewerScaleAt(clientX, clientY, 1);
  }

  function toggleTouchViewerZoom(clientX, clientY) {
    if (viewerScale > viewerFitScale + 0.001) {
      setViewerSize(false);
      return;
    }

    const targetScale = viewerFitScale < 1 ? 1 : 2;
    setViewerScaleAt(clientX, clientY, targetScale);
  }

  function resetViewerImage() {
    viewerImage.style.removeProperty('width');
    viewerImage.style.removeProperty('height');
    viewerImage.classList.remove('original-size', 'zoomable');
    viewerStage.classList.remove('original-size', 'original-overflow-x', 'original-overflow-y');
    viewerStage.scrollTo(0, 0);
    viewerIsLarge = false;
    viewerIsOriginal = false;
    viewerDisplayingPlaceholder = false;
    viewerPlaceholderButton = null;
    viewerFitScale = 1;
    viewerScale = 1;
    viewerTouchPointers.clear();
    viewerTouchGesture = null;
    viewerPinch = null;
    viewerLastTap = null;
  }

  function cancelViewerLoads() {
    viewerLoadGeneration++;
    viewerNeighborPrefetchController?.abort();
    viewerNeighborPrefetchController = null;
    if (viewerNeighborPrefetchTimer !== null) {
      if (viewerNeighborPrefetchUsesIdle && 'cancelIdleCallback' in window) window.cancelIdleCallback(viewerNeighborPrefetchTimer);
      else clearTimeout(viewerNeighborPrefetchTimer);
      viewerNeighborPrefetchTimer = null;
    }
    viewerOriginalRequest?.abort();
    viewerOriginalRequest = null;
    viewerOriginalCacheProbe?.abort();
    viewerOriginalCacheProbe = null;
    [viewerThumbnailLoader, viewerOriginalLoader].forEach(loader => {
      if (!loader) return;
      loader.onload = null;
      loader.onerror = null;
      loader.src = '';
    });
    viewerThumbnailLoader = null;
    viewerOriginalLoader = null;
  }

  function getViewerMemoryCache(url) {
    const entry = viewerFullImageCache.get(url);
    if (!entry) return null;
    viewerFullImageCache.delete(url);
    viewerFullImageCache.set(url, entry);
    return entry;
  }

  function rememberViewerOriginal(url, blob) {
    const existing = viewerFullImageCache.get(url);
    if (existing) {
      viewerFullImageCache.delete(url);
      viewerFullImageCacheBytes -= existing.size;
      URL.revokeObjectURL(existing.objectUrl);
    }

    const entry = { objectUrl: URL.createObjectURL(blob), size: blob.size };
    viewerFullImageCache.set(url, entry);
    viewerFullImageCacheBytes += entry.size;
    while (viewerFullImageCache.size > maximumViewerCacheEntries
      || (viewerFullImageCacheBytes > maximumViewerCacheBytes && viewerFullImageCache.size > 1)) {
      const oldest = viewerFullImageCache.entries().next().value;
      if (!oldest) break;
      viewerFullImageCache.delete(oldest[0]);
      viewerFullImageCacheBytes -= oldest[1].size;
      URL.revokeObjectURL(oldest[1].objectUrl);
    }
    return entry;
  }

  function clearViewerMemoryCache() {
    viewerFullImageCache.forEach(entry => URL.revokeObjectURL(entry.objectUrl));
    viewerFullImageCache.clear();
    viewerFullImageCacheBytes = 0;
  }

  function preloadViewerImage(url, priority) {
    const loader = new Image();
    loader.decoding = 'async';
    loader.fetchPriority = priority;
    const ready = new Promise((resolve, reject) => {
      loader.onload = async () => {
        try { await loader.decode(); } catch { }
        resolve(loader);
      };
      loader.onerror = reject;
    });
    loader.src = url;
    return { loader, ready };
  }

  function formatViewerBytes(bytes) {
    if (!Number.isFinite(bytes) || bytes < 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB'];
    let value = bytes;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit++;
    }
    const digits = unit === 0 ? 0 : (value >= 100 ? 0 : 1);
    return `${value.toFixed(digits)} ${units[unit]}`;
  }

  function updateViewerChrome(button) {
    if (viewerCounter) viewerCounter.textContent = `${currentViewerIndex + 1} / ${viewerButtons.length}`;
    if (viewerInfoType) viewerInfoType.textContent = button.dataset.viewerType || 'Image';
    if (viewerInfoSize) viewerInfoSize.textContent = button.dataset.viewerSize || '—';
    if (viewerInfoModified) viewerInfoModified.textContent = button.dataset.viewerModified || '—';
    if (viewerInfoDimensions) viewerInfoDimensions.textContent = 'Loading…';
    if (viewerInfo) viewerInfo.open = false;
  }

  function updateViewerDimensions() {
    if (!viewerInfoDimensions || !viewerImage?.naturalWidth || !viewerImage?.naturalHeight) return;
    viewerInfoDimensions.textContent = `${viewerImage.naturalWidth} × ${viewerImage.naturalHeight}`;
  }

  function renderViewerFilmstrip() {
    if (!viewerFilmstrip) return;
    viewerFilmstrip.replaceChildren();
    if (viewerButtons.length < 2 || currentViewerIndex < 0) {
      viewerFilmstrip.hidden = true;
      return;
    }

    viewerFilmstrip.hidden = false;
    const maximumItems = 11;
    const start = Math.max(0, Math.min(currentViewerIndex - Math.floor(maximumItems / 2), viewerButtons.length - maximumItems));
    const end = Math.min(viewerButtons.length, start + maximumItems);
    for (let index = start; index < end; index++) {
      const sourceButton = viewerButtons[index];
      const item = document.createElement('button');
      item.type = 'button';
      item.className = 'viewer-filmstrip-item';
      item.title = sourceButton.dataset.viewerName || `Image ${index + 1}`;
      item.setAttribute('aria-label', `View ${sourceButton.dataset.viewerName || `image ${index + 1}`}`);
      if (index === currentViewerIndex) {
        item.classList.add('active');
        item.setAttribute('aria-current', 'true');
      }
      const image = document.createElement('img');
      image.alt = '';
      image.decoding = 'async';
      image.loading = index === currentViewerIndex ? 'eager' : 'lazy';
      image.src = sourceButton.dataset.viewerThumbnail;
      item.append(image);
      item.addEventListener('click', () => {
        if (index === currentViewerIndex) return;
        currentViewerIndex = index;
        showViewerImage(viewerButtons[currentViewerIndex]);
      });
      viewerFilmstrip.append(item);
    }
    requestAnimationFrame(() => viewerFilmstrip.querySelector('.active')?.scrollIntoView({ block: 'nearest', inline: 'center' }));
  }

  function scheduleViewerNeighborPrefetch(generation) {
    if (viewerButtons.length < 2 || currentViewerIndex < 0 || navigator.connection?.saveData) return;
    const connectionType = navigator.connection?.effectiveType;
    if (connectionType === 'slow-2g' || connectionType === '2g') return;

    const run = async () => {
      viewerNeighborPrefetchTimer = null;
      if (generation !== viewerLoadGeneration || !dialog?.open) return;
      const controller = new AbortController();
      viewerNeighborPrefetchController = controller;
      const indexes = [
        (currentViewerIndex + 1) % viewerButtons.length,
        (currentViewerIndex - 1 + viewerButtons.length) % viewerButtons.length
      ];
      for (const index of [...new Set(indexes)]) {
        if (controller.signal.aborted || generation !== viewerLoadGeneration || !dialog?.open) break;
        const url = viewerButtons[index].dataset.viewerSrc;
        if (!url || viewerFullImageCache.has(url)) continue;
        try {
          const response = await fetch(url, {
            signal: controller.signal,
            credentials: 'same-origin',
            cache: 'force-cache',
            priority: 'low'
          });
          const size = Number.parseInt(response.headers.get('Content-Length') || '0', 10);
          if (!response.ok || size > 96 * 1024 * 1024) {
            await response.body?.cancel();
            continue;
          }
          const blob = await response.blob();
          if (!controller.signal.aborted && generation === viewerLoadGeneration) rememberViewerOriginal(url, blob);
        } catch (error) {
          if (error?.name === 'AbortError') break;
        }
      }
      if (viewerNeighborPrefetchController === controller) viewerNeighborPrefetchController = null;
    };

    viewerNeighborPrefetchUsesIdle = 'requestIdleCallback' in window;
    viewerNeighborPrefetchTimer = viewerNeighborPrefetchUsesIdle
      ? window.requestIdleCallback(run, { timeout: 1200 })
      : window.setTimeout(run, 500);
  }

  function updateViewerProgress(loaded, total) {
    const hasTotal = Number.isFinite(total) && total > 0;
    const percent = hasTotal ? Math.min(100, Math.round((loaded / total) * 100)) : 0;
    if (viewerProgress) {
      if (hasTotal) {
        viewerProgress.max = 100;
        viewerProgress.value = percent;
      } else {
        viewerProgress.max = 100;
        viewerProgress.value = 0;
      }
    }
    if (viewerProgressDetail) {
      viewerProgressDetail.textContent = hasTotal
        ? `${percent}% · ${formatViewerBytes(loaded)} / ${formatViewerBytes(total)}`
        : `0% · ${formatViewerBytes(loaded)} / waiting for size`;
    }
  }

  async function displayViewerSource(loader, generation, placeholderButton = null) {
    if (generation !== viewerLoadGeneration || !dialog?.open) return false;
    viewerImage.src = loader.currentSrc || loader.src;
    try { await viewerImage.decode(); } catch { }
    if (generation !== viewerLoadGeneration || !dialog?.open) return false;
    viewerImage.classList.remove('loading');
    if (placeholderButton) {
      setViewerPlaceholderSize(placeholderButton);
    } else {
      viewerDisplayingPlaceholder = false;
      viewerPlaceholderButton = null;
      setViewerSize(false);
      updateViewerDimensions();
      scheduleViewerNeighborPrefetch(generation);
    }
    return true;
  }

  async function getCachedViewerOriginal(url, generation) {
    const controller = new AbortController();
    viewerOriginalCacheProbe = controller;
    try {
      const response = await fetch(url, {
        signal: controller.signal,
        credentials: 'same-origin',
        mode: 'same-origin',
        cache: 'only-if-cached'
      });
      if (generation !== viewerLoadGeneration || !response.ok) return null;
      return await response.blob();
    } catch {
      return null;
    } finally {
      if (viewerOriginalCacheProbe === controller) viewerOriginalCacheProbe = null;
    }
  }

  function prepareViewerOriginal(url, blob) {
    const entry = rememberViewerOriginal(url, blob);
    const original = preloadViewerImage(entry.objectUrl, 'auto');
    viewerOriginalLoader = original.loader;
    return original.ready;
  }

  function loadViewerOriginal(url, generation, onHeaders, onReady, onError) {
    const request = new XMLHttpRequest();
    let knownTotal = 0;
    viewerOriginalRequest = request;
    request.open('GET', url, true);
    request.responseType = 'blob';
    request.onreadystatechange = () => {
      if (request.readyState !== XMLHttpRequest.HEADERS_RECEIVED || generation !== viewerLoadGeneration) return;
      knownTotal = Number.parseInt(request.getResponseHeader('Content-Length') || '0', 10);
      if (knownTotal > 0) updateViewerProgress(0, knownTotal);
      onHeaders(
        Number.parseInt(request.getResponseHeader('X-Image-Width') || '0', 10),
        Number.parseInt(request.getResponseHeader('X-Image-Height') || '0', 10));
    };
    request.onprogress = event => {
      if (generation !== viewerLoadGeneration) return;
      updateViewerProgress(event.loaded, event.lengthComputable ? event.total : knownTotal);
    };
    request.onload = () => {
      if (viewerOriginalRequest === request) viewerOriginalRequest = null;
      onHeaders(0, 0);
      if (generation !== viewerLoadGeneration || request.status < 200 || request.status >= 300) {
        if (generation === viewerLoadGeneration) onError();
        return;
      }
      updateViewerProgress(request.response.size, request.response.size);
      prepareViewerOriginal(url, request.response).then(onReady).catch(onError);
    };
    request.onerror = () => {
      onHeaders(0, 0);
      onError();
    };
    request.send();
  }

  function showViewerImage(button) {
    cancelViewerLoads();
    const generation = viewerLoadGeneration;
    resetViewerImage();
    viewerName.textContent = button.dataset.viewerName;
    updateViewerChrome(button);
    renderViewerFilmstrip();
    viewerImage.alt = button.dataset.viewerName;
    viewerDownload.href = button.dataset.download;
    viewerImage.classList.add('loading');
    viewerImage.removeAttribute('src');
    if (viewerLoading) viewerLoading.hidden = true;
    if (viewerLoadingLabel) viewerLoadingLabel.textContent = 'Loading full image...';
    if (viewerProgress) viewerProgress.hidden = true;
    updateViewerProgress(0, 0);
    if (button.dataset.shareAudit) {
      fetch(button.dataset.shareAudit, {
        method: 'POST',
        credentials: 'same-origin',
        keepalive: true
      }).catch(() => { });
    }

    let thumbnailDisplayed = false;
    let originalReady = false;
    const memoryEntry = getViewerMemoryCache(button.dataset.viewerSrc);
    if (memoryEntry) {
      const original = preloadViewerImage(memoryEntry.objectUrl, 'high');
      viewerOriginalLoader = original.loader;
      original.ready.then(async loader => {
        if (generation !== viewerLoadGeneration) return;
        originalReady = true;
        await displayViewerSource(loader, generation);
      }).catch(() => { });
      return;
    }

    getCachedViewerOriginal(button.dataset.viewerSrc, generation).then(cachedBlob => {
      if (generation !== viewerLoadGeneration) return;
      if (cachedBlob) {
        prepareViewerOriginal(button.dataset.viewerSrc, cachedBlob).then(async loader => {
          if (generation !== viewerLoadGeneration) return;
          originalReady = true;
          await displayViewerSource(loader, generation);
        }).catch(() => { });
        return;
      }

      if (viewerLoading) viewerLoading.hidden = false;
      if (viewerProgress) viewerProgress.hidden = false;
      let thumbnailStarted = false;
      const startThumbnail = (originalWidth, originalHeight) => {
        if (thumbnailStarted || generation !== viewerLoadGeneration) return;
        thumbnailStarted = true;
        if (originalWidth > 0 && originalHeight > 0) {
          button.dataset.viewerOriginalWidth = originalWidth;
          button.dataset.viewerOriginalHeight = originalHeight;
        }
        const thumbnail = preloadViewerImage(button.dataset.viewerThumbnail, 'high');
        viewerThumbnailLoader = thumbnail.loader;
        thumbnail.ready.then(async loader => {
          if (generation !== viewerLoadGeneration || originalReady) return;
          thumbnailDisplayed = await displayViewerSource(loader, generation, button);
        }).catch(() => { });
      };
      loadViewerOriginal(button.dataset.viewerSrc, generation, startThumbnail, async loader => {
        if (generation !== viewerLoadGeneration) return;
        originalReady = true;
        await displayViewerSource(loader, generation);
        if (viewerLoading) viewerLoading.hidden = true;
      }, () => {
        if (generation !== viewerLoadGeneration) return;
        if (!thumbnailDisplayed) viewerImage.classList.add('loading');
        if (viewerLoadingLabel) viewerLoadingLabel.textContent = 'Full image could not be loaded.';
        if (viewerProgress) viewerProgress.hidden = true;
      });
    });
  }

  function openViewer(button) {
    cancelThumbnailRequests(false);
    currentViewerIndex = viewerButtons.indexOf(button);
    const canNavigate = viewerButtons.length > 1;
    viewerPrevious.hidden = !canNavigate;
    viewerNext.hidden = !canNavigate;
    dialog.showModal();
    showViewerImage(button);
  }

  function navigateViewer(offset) {
    if (viewerButtons.length < 2 || currentViewerIndex < 0) return;
    currentViewerIndex = (currentViewerIndex + offset + viewerButtons.length) % viewerButtons.length;
    showViewerImage(viewerButtons[currentViewerIndex]);
  }

  viewerButtons.forEach(button => {
    const input = button.closest('.file-card')?.querySelector('.file-select');
    const index = fileSelects.indexOf(input);
    let longPressTimer = null;
    let longPressPoint = null;

    const cancelLongPress = () => {
      if (longPressTimer) clearTimeout(longPressTimer);
      longPressTimer = null;
      longPressPoint = null;
    };
    button.addEventListener('pointerdown', event => {
      if (event.pointerType !== 'touch' || index < 0) return;
      longPressPoint = { x: event.clientX, y: event.clientY };
      longPressTimer = setTimeout(() => {
        input.checked = !input.checked;
        selectionAnchor = index;
        suppressGalleryClickUntil = Date.now() + 800;
        updateSelection();
        if (navigator.vibrate) navigator.vibrate(25);
        longPressTimer = null;
      }, 520);
    });
    button.addEventListener('pointermove', event => {
      if (!longPressPoint) return;
      if (Math.hypot(event.clientX - longPressPoint.x, event.clientY - longPressPoint.y) > 10) cancelLongPress();
    });
    button.addEventListener('pointerup', cancelLongPress);
    button.addEventListener('pointercancel', cancelLongPress);

    button.addEventListener('click', event => {
      if (Date.now() < suppressGalleryClickUntil) {
        event.preventDefault();
        return;
      }
      if (gallery?.classList.contains('list-view')) {
        openViewer(button);
        return;
      }

      if (event.shiftKey && selectionAnchor !== null && index >= 0) {
        clearTimeout(pendingImageClick);
        const visibleIndexes = fileSelects
          .map((item, itemIndex) => item.closest('.gallery-card')?.hidden ? -1 : itemIndex)
          .filter(itemIndex => itemIndex >= 0);
        const anchorPosition = visibleIndexes.indexOf(selectionAnchor);
        const currentPosition = visibleIndexes.indexOf(index);
        if (!event.ctrlKey) fileSelects.forEach(item => item.checked = false);
        if (anchorPosition >= 0 && currentPosition >= 0) {
          const from = Math.min(anchorPosition, currentPosition);
          const to = Math.max(anchorPosition, currentPosition);
          visibleIndexes.slice(from, to + 1).forEach(itemIndex => fileSelects[itemIndex].checked = true);
        } else {
          input.checked = true;
        }
        selectionAnchor = index;
        updateSelection();
        return;
      }

      if (event.ctrlKey && index >= 0) {
        clearTimeout(pendingImageClick);
        input.checked = !input.checked;
        selectionAnchor = index;
        updateSelection();
        return;
      }

      clearTimeout(pendingImageClick);
      pendingImageClick = setTimeout(() => {
        fileSelects.forEach(item => item.checked = item === input);
        selectionAnchor = index;
        updateSelection();
      }, 220);
    });

    button.addEventListener('dblclick', event => {
      if (gallery?.classList.contains('list-view')) return;
      event.preventDefault();
      clearTimeout(pendingImageClick);
      openViewer(button);
    });
  });

  document.querySelectorAll('.file-card').forEach(card => {
    if (card.querySelector('.image-button')) return;
    const input = card.querySelector('.file-select');
    const index = fileSelects.indexOf(input);
    if (!input || index < 0) return;
    let timer = null;
    let start = null;
    const cancel = () => {
      if (timer) clearTimeout(timer);
      timer = null;
      start = null;
    };
    card.addEventListener('pointerdown', event => {
      if (event.pointerType !== 'touch' || event.target.closest('.select-toggle')) return;
      start = { x: event.clientX, y: event.clientY };
      timer = setTimeout(() => {
        input.checked = !input.checked;
        selectionAnchor = index;
        suppressGalleryClickUntil = Date.now() + 800;
        updateSelection();
        if (navigator.vibrate) navigator.vibrate(25);
        timer = null;
      }, 520);
    });
    card.addEventListener('pointermove', event => {
      if (start && Math.hypot(event.clientX - start.x, event.clientY - start.y) > 10) cancel();
    });
    card.addEventListener('pointerup', cancel);
    card.addEventListener('pointercancel', cancel);
    card.addEventListener('click', event => {
      if (Date.now() >= suppressGalleryClickUntil) return;
      event.preventDefault();
      event.stopPropagation();
    }, true);
  });

  viewerImage?.addEventListener('click', event => {
    event.stopPropagation();
    if (Date.now() < suppressViewerClickUntil) return;
    if (!viewerIsLarge) return;
    if (viewerIsOriginal) setViewerSize(false);
    else zoomViewerAt(event.clientX, event.clientY);
  });
  viewerStage?.addEventListener('pointerdown', event => {
    if (event.pointerType !== 'touch' || viewerImage.classList.contains('loading')) return;
    event.preventDefault();
    viewerTouchPointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
    try { viewerStage.setPointerCapture(event.pointerId); } catch { }

    if (viewerTouchPointers.size === 1) {
      viewerTouchGesture = {
        startedOnImage: event.target === viewerImage,
        startX: event.clientX,
        startY: event.clientY,
        lastX: event.clientX,
        lastY: event.clientY,
        moved: false,
        pinched: false
      };
      return;
    }

    if (viewerTouchPointers.size === 2) {
      const points = [...viewerTouchPointers.values()];
      const midpointX = (points[0].x + points[1].x) / 2;
      const midpointY = (points[0].y + points[1].y) / 2;
      const imageRect = viewerImage.getBoundingClientRect();
      viewerPinch = {
        distance: Math.hypot(points[1].x - points[0].x, points[1].y - points[0].y),
        scale: viewerScale,
        anchorX: Math.max(0, Math.min(1, (midpointX - imageRect.left) / imageRect.width)),
        anchorY: Math.max(0, Math.min(1, (midpointY - imageRect.top) / imageRect.height))
      };
      if (viewerTouchGesture) viewerTouchGesture.pinched = true;
    }
  });
  viewerStage?.addEventListener('pointermove', event => {
    const previous = viewerTouchPointers.get(event.pointerId);
    if (event.pointerType !== 'touch' || !previous) return;
    event.preventDefault();
    viewerTouchPointers.set(event.pointerId, { x: event.clientX, y: event.clientY });

    if (viewerTouchPointers.size >= 2 && viewerPinch) {
      const points = [...viewerTouchPointers.values()].slice(0, 2);
      const distance = Math.hypot(points[1].x - points[0].x, points[1].y - points[0].y);
      const midpointX = (points[0].x + points[1].x) / 2;
      const midpointY = (points[0].y + points[1].y) / 2;
      if (viewerPinch.distance > 0) {
        setViewerScaleAt(midpointX, midpointY,
          viewerPinch.scale * (distance / viewerPinch.distance), viewerPinch.anchorX, viewerPinch.anchorY);
      }
      return;
    }

    if (viewerTouchGesture && viewerTouchPointers.size === 1) {
      const dx = event.clientX - viewerTouchGesture.lastX;
      const dy = event.clientY - viewerTouchGesture.lastY;
      if (Math.hypot(event.clientX - viewerTouchGesture.startX, event.clientY - viewerTouchGesture.startY) > 8) {
        viewerTouchGesture.moved = true;
      }
      if (viewerScale > viewerFitScale + 0.001) viewerStage.scrollBy(-dx, -dy);
      viewerTouchGesture.lastX = event.clientX;
      viewerTouchGesture.lastY = event.clientY;
    }
  });

  function finishViewerTouch(event) {
    if (event.pointerType !== 'touch' || !viewerTouchPointers.has(event.pointerId)) return;
    event.preventDefault();
    suppressViewerClickUntil = Date.now() + 600;
    viewerTouchPointers.delete(event.pointerId);

    if (viewerTouchPointers.size === 1) {
      const point = [...viewerTouchPointers.values()][0];
      viewerTouchGesture = {
        startedOnImage: true,
        startX: point.x,
        startY: point.y,
        lastX: point.x,
        lastY: point.y,
        moved: false,
        pinched: true
      };
      viewerPinch = null;
      return;
    }

    if (viewerTouchPointers.size > 0) return;
    const gesture = viewerTouchGesture;
    viewerTouchGesture = null;
    viewerPinch = null;
    if (!gesture || gesture.pinched || !gesture.startedOnImage) return;

    const swipeX = event.clientX - gesture.startX;
    const swipeY = event.clientY - gesture.startY;
    if (gesture.moved
      && viewerScale <= viewerFitScale + 0.001
      && viewerButtons.length > 1
      && Math.abs(swipeX) >= 56
      && Math.abs(swipeX) > Math.abs(swipeY) * 1.25) {
      viewerLastTap = null;
      navigateViewer(swipeX < 0 ? 1 : -1);
      return;
    }
    if (gesture.moved) return;

    const now = Date.now();
    const isDoubleTap = viewerLastTap
      && now - viewerLastTap.time <= 320
      && Math.hypot(event.clientX - viewerLastTap.x, event.clientY - viewerLastTap.y) <= 36;
    if (isDoubleTap) {
      toggleTouchViewerZoom(event.clientX, event.clientY);
      viewerLastTap = null;
    } else {
      viewerLastTap = { time: now, x: event.clientX, y: event.clientY };
    }
  }

  viewerStage?.addEventListener('pointerup', finishViewerTouch);
  viewerStage?.addEventListener('pointercancel', event => {
    if (event.pointerType !== 'touch') return;
    viewerTouchPointers.delete(event.pointerId);
    if (viewerTouchPointers.size === 0) {
      viewerTouchGesture = null;
      viewerPinch = null;
    }
  });
  window.addEventListener('resize', () => {
    if (!dialog?.open) return;
    if (viewerDisplayingPlaceholder && viewerPlaceholderButton) setViewerPlaceholderSize(viewerPlaceholderButton);
    else if (!viewerIsOriginal) setViewerSize(false);
  });
  viewerPrevious?.addEventListener('click', event => {
    event.stopPropagation();
    navigateViewer(-1);
  });
  viewerNext?.addEventListener('click', event => {
    event.stopPropagation();
    navigateViewer(1);
  });
  document.addEventListener('keydown', event => {
    if (!dialog?.open || viewerButtons.length < 2) return;
    if (event.key === 'ArrowLeft') {
      event.preventDefault();
      navigateViewer(-1);
    } else if (event.key === 'ArrowRight') {
      event.preventDefault();
      navigateViewer(1);
    }
  });
  document.querySelector('#viewer-close')?.addEventListener('click', () => dialog.close());
  dialog?.addEventListener('click', event => { if (event.target === dialog) dialog.close(); });
  dialog?.addEventListener('close', () => {
    cancelViewerLoads();
    viewerImage.removeAttribute('src');
    viewerImage.classList.remove('loading');
    resetViewerImage();
    currentViewerIndex = -1;
    resumeThumbnailRequests();
  });
  window.addEventListener('pagehide', () => {
    cancelViewerLoads();
    clearViewerMemoryCache();
  });
})();
