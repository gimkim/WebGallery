const fs = require('node:fs'), vm = require('node:vm'), assert = require('node:assert/strict');
const source = fs.readFileSync(require('node:path').join(__dirname, '../wwwroot/js/navigation-console.js'), 'utf8');
const storage = new Map();
function setup() {
  const events = {}, win = {}, nodes = [];
  function node() { const n = { children: [], textContent: '', classList: { add() {} }, setAttribute() {}, append(...v) { this.children.push(...v); }, appendChild(v) { this.children.push(v); }, addEventListener() {}, remove() { this.removed = true; } }; nodes.push(n); return n; }
  const host = node(), body = node();
  const context = { URL, URLSearchParams, Date, document: { createElement: node, body, querySelector: () => host, addEventListener: (n, fn) => events[n] = fn },
    window: { addEventListener: (n, fn) => win[n] = fn }, location: { href: 'https://test/Gallery/?path=photos' },
    sessionStorage: { getItem: key => storage.get(key), setItem: (k, v) => storage.set(k, v), removeItem: k => storage.delete(k) },
    performance: { now: () => 123, getEntriesByType: () => [{ responseStatus: 200, responseEnd: 200, responseStart: 150, requestStart: 20, transferSize: 4096, decodedBodySize: 10000, type: 'navigate' }] },
    setInterval: () => 1, clearInterval() {}, setTimeout() {} };
  vm.runInNewContext(source, context); return { events, win, nodes, host, body };
}
let page = setup();
page.events['gallery:navigation-start']({ detail: { url: `https://test/Gallery/Share?token=${'a'.repeat(48)}&path=@root-42/photos` } });
const text = page.host.children[0].children[1].textContent;
assert(text.includes('pending')); assert(text.includes('indeterminate')); assert(!text.includes('aaaa')); assert(!text.includes('@root-42'));
assert(![...storage.values()][0].includes('aaaa'), 'No raw token persisted');
page.win.pagehide();
const destination = setup(); destination.win.load();
const output = destination.body.children[0].children[1].textContent;
assert(output.includes('HTTP     200')); assert(output.includes('100%')); assert(output.includes('4.0 KiB')); assert(output.includes('0.13 s'));
page = setup(); page.events['gallery:navigation-start']({ detail: { url: 'https://test/Gallery/' } }); page.events['gallery:navigation-end']();
assert.equal(storage.size, 0);
console.log('PASS: pending/unknown metrics, share-token/root-marker redaction, destination HTTP/byte/timing values and cancellation cleanup (mock browser metrics).');
