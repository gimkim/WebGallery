const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync(require('node:path').join(__dirname, '../wwwroot/js/site.js'), 'utf8');
const code = source.slice(source.indexOf('  function showViewerImage(button)'), source.indexOf('  function openViewer(button)'));
async function run(memory, cached) {
  let finish;
  const pending = new Promise(resolve => { finish = resolve; });
  const classes = new Set();
  const image = { naturalWidth: 480, naturalHeight: 360, style: {}, classList: { add: x => classes.add(x), remove: x => classes.delete(x) }, removeAttribute: () => {}, decode: () => Promise.resolve() };
  const ctx = {
    cancelViewerLoads() {}, viewerLoadGeneration: 1, resetViewerImage() {}, viewerName: {}, updateViewerChrome() {}, renderViewerFilmstrip() {},
    snapshotViewerThumbnail: () => 'data:image/png;base64,thumb',
    viewerImage: image, viewerDownload: {}, viewerLoading: {}, viewerLoadingLabel: {}, viewerProgress: {}, updateViewerProgress() {},
    viewerDisplayingPlaceholder: false, viewerPlaceholderButton: null,
    getViewerMemoryCache: () => memory ? { objectUrl: 'blob:full' } : null,
    preloadViewerImage: () => ({ loader: {}, ready: pending }),
    getViewerAvailableSize: () => ({ width: 1200, height: 900 }), setViewerPlaceholderSize() {},
    getCachedViewerOriginal: () => Promise.resolve(cached ? {} : null), prepareViewerOriginal: () => pending,
    loadViewerOriginal: (url, generation, headers, ready) => { pending.then(ready); },
    displayViewerSource: async loader => { image.src = loader.src; }
  };
  vm.createContext(ctx); vm.runInContext(code, ctx);
  ctx.showViewerImage({ dataset: { viewerSrc: '/full', viewerThumbnail: '/thumb' }, querySelector: () => ({ complete: true, naturalWidth: 480, src: 'blob:thumb' }) });
  assert.equal(image.src, 'data:image/png;base64,thumb', 'decoded snapshot must display before original decode resolves');
  assert.equal(classes.has('loading'), false);
  assert.equal(ctx.viewerLoading.hidden, false, 'progress overlay must show for all cache paths');
  assert.equal(ctx.viewerProgress.hidden, false);
  await Promise.resolve();
  finish({ src: 'blob:full' });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(image.src, 'blob:full');
}
(async () => { await run(true, false); await run(false, true); await run(false, false); console.log('PASS: pending memory Blob, HTTP cache and network original all retain thumbnail until ready.'); })().catch(e => { console.error(e); process.exitCode = 1; });
