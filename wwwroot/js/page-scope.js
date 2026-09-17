window.GalleryPages = {
  mounts: [], active: [],
  mount() {
    for (const mount of this.mounts) {
      const scope = this.scope(); this.active.push(scope); mount(scope);
    }
  },
  unmount() { for (const scope of this.active) scope.dispose(); this.active = []; },
  scope() {
    const abort = new AbortController(), cleanup = [], exits = [];
    const scheduled = new Map();
    let disposed = false;
    const schedule = (kind, start, stop, repeat = false) => (callback, ...args) => {
      if (disposed) return 0;
      const id = start((...values) => { if (!repeat) scheduled.delete(kind + id); if (!disposed) callback(...values); }, ...args);
      scheduled.set(kind + id, () => stop(id)); return id;
    };
    const timeout = schedule('timer', window.setTimeout.bind(window), window.clearTimeout.bind(window));
    const interval = schedule('timer', window.setInterval.bind(window), window.clearInterval.bind(window), true);
    const frame = schedule('frame', window.requestAnimationFrame.bind(window), window.cancelAnimationFrame.bind(window));
    const idle = window.requestIdleCallback ? schedule('idle', window.requestIdleCallback.bind(window), window.cancelIdleCallback.bind(window)) : callback => timeout(callback, 1);
    const clear = (kind, native) => id => { scheduled.delete(kind + id); native(id); };
    const clears = { clearTimeout: clear('timer', window.clearTimeout.bind(window)), clearInterval: clear('timer', window.clearInterval.bind(window)), cancelAnimationFrame: clear('frame', window.cancelAnimationFrame.bind(window)), cancelIdleCallback: clear('idle', window.cancelIdleCallback?.bind(window) || window.clearTimeout.bind(window)) };
    const locationProxy = { get href() { return location.href; }, assign: url => window.GalleryRouter.navigate(url) };
    const proxy = target => new Proxy(target, { get(target, key) {
      if (key === 'location') return locationProxy;
      if (key in clears) return clears[key];
      if (key === 'setTimeout') return timeout;
      if (key === 'setInterval') return interval;
      if (key === 'requestAnimationFrame') return frame;
      if (key === 'requestIdleCallback') return idle;
      if (key === 'addEventListener') return (name, fn, options) => {
        target.addEventListener(name, fn, options);
        cleanup.push(() => target.removeEventListener(name, fn, options));
        if (name === 'pagehide') exits.push(fn);
      };
      const value = Reflect.get(target, key, target); return typeof value === 'function' ? value.bind(target) : value;
    }, set(target, key, value) { return Reflect.set(target, key, value, target); } });
    const observer = Native => Native && class extends Native { constructor(...args) { super(...args); cleanup.push(() => this.disconnect()); } };
    return {
      window: proxy(window), document: proxy(document), setTimeout: timeout, setInterval: interval, requestAnimationFrame: frame,
      ...clears,
      IntersectionObserver: observer(window.IntersectionObserver), ResizeObserver: observer(window.ResizeObserver),
      fetch: (url, options = {}) => fetch(url, { ...options, signal: options.signal ? AbortSignal.any([abort.signal, options.signal]) : abort.signal }),
      dispose() { if (disposed) return; disposed = true; for (const exit of exits) { try { exit(new Event('pagehide')); } catch (error) { console.error(error); } } abort.abort(); for (const stop of cleanup) stop(); for (const stop of scheduled.values()) stop(); scheduled.clear(); }
    };
  }
};
