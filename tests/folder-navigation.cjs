const fs = require('node:fs'), vm = require('node:vm'), assert = require('node:assert/strict');
const events = {}, windowEvents = {}, raf = [], timers = new Map(), assigned = [];
let id = 0, stopped = 0;
const message = {}, button = { addEventListener: (name, fn) => button[name] = fn };
const skeleton = { style: { setProperty() {} }, appendChild() {} };
const overlay = { hidden: true, classList: { toggle() {} }, querySelector: s => s === 'p' ? message : s === 'button' ? button : skeleton };
const shell = { inert: false };
const document = {
  querySelector: s => s === '.page-shell' ? shell : null,
  createElement: s => s === 'section' ? overlay : {}, body: { appendChild() {} },
  addEventListener: (name, fn) => events[name] = fn,
  dispatchEvent: event => events[event.type]?.(event)
};
const context = { document, window: { addEventListener: (n, fn) => windowEvents[n] = fn, stop: () => stopped++ },
  location: { href: 'https://example.test/Gallery/', origin: 'https://example.test', protocol: 'https:', assign: url => assigned.push(url) },
  URL, Event, CustomEvent: class extends Event { constructor(name, options) { super(name); this.detail = options.detail; } }, requestAnimationFrame: fn => raf.push(fn), setTimeout: fn => { timers.set(++id, fn); return id; }, clearTimeout: key => timers.delete(key) };
vm.runInNewContext(fs.readFileSync(require('node:path').join(__dirname, '../wwwroot/js/folder-navigation.js'), 'utf8'), context);
const link = { href: 'https://example.test/Gallery/?path=photos', target: '', textContent: 'Photos', hasAttribute: () => false, querySelector: () => null };
function click(extra = {}) { const event = { button: 0, target: { closest: () => link }, preventDefault() { this.defaultPrevented = true; }, ...extra }; events.click(event); return event; }
assert(!click({ ctrlKey: true }).defaultPrevented); assert(overlay.hidden);
assert(click().defaultPrevented); assert(!overlay.hidden); assert(shell.inert); assert.equal(assigned.length, 0);
raf.shift()(); assert.equal(assigned.length, 0); raf.shift()(); assert.equal(assigned.length, 1);
button.click(); assert(overlay.hidden); assert(!shell.inert); assert.equal(stopped, 1);
click(); windowEvents.pagehide(); windowEvents.pageshow(); assert(overlay.hidden); assert(!shell.inert);
while (raf.length) raf.shift()(); assert.equal(assigned.length, 1, 'Old RAF must not navigate after restore');
click(); button.click(); while (raf.length) raf.shift()(); assert.equal(assigned.length, 1, 'Cancel before paint prevents navigation');
console.log('PASS: immediate placeholder before navigation, paint ordering, modified-click exemption, cancellation, BFCache restore and stale callback suppression. Mock DOM, not browser visual QA.');
