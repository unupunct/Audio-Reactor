// Headless smoke + reactivity test for all visualizer presets.
// Usage: node Tools/test-presets.js
const fs = require('fs'), path = require('path'), vm = require('vm');
const dir = path.join(__dirname, '..', 'Source', 'AudioReactor.App', 'Visualizer');

function makeCtx(log) {
  const grad = { addColorStop() { } };
  const h = (v) => { if (typeof v === 'number') log.push(Math.round(v * 100)); else if (typeof v === 'string') log.push(v.length); };
  const target = { canvas: { width: 1280, height: 720 } };
  return new Proxy(target, {
    get(t, k) {
      if (k in t) return t[k];
      if (k === 'createLinearGradient' || k === 'createRadialGradient') return (...a) => { a.forEach(h); return grad; };
      if (k === 'createImageData') return (w, hh) => ({ data: new Uint8ClampedArray(w * hh * 4), width: w, height: hh });
      if (k === 'putImageData') return (img) => { let sum = 0; const d = img.data; for (let i = 0; i < d.length; i += 7) sum += d[i]; log.push(sum | 0); };
      if (k === 'measureText') return () => ({ width: 10 });
      return (...a) => { log.push(String(k).length); a.forEach(h); };
    },
    set(t, k, v) { t[k] = v; h(v); return true; }
  });
}

const sandbox = {
  console, Math, Array, Float32Array, Uint8ClampedArray, Object, Map, JSON, Number, String, parseInt, isFinite,
  window: { devicePixelRatio: 1, addEventListener() { }, chrome: undefined },
  document: { createElement: () => ({ width: 0, height: 0, getContext: () => makeCtx(sandbox.__log) }), getElementById: () => null },
  __log: [],
  requestAnimationFrame() { }
};
vm.createContext(sandbox);
let src = '';
for (const f of ['engine.js', 'presets1.js', 'presets2.js', 'presets3.js', 'presets4.js']) src += fs.readFileSync(path.join(dir, f), 'utf8') + '\n';
vm.runInContext(src + '\nthis.Reactor = Reactor;', sandbox);
const Reactor = sandbox.Reactor;

const list = Reactor.list();
const cats = {}; list.forEach(p => cats[p.category] = (cats[p.category] || 0) + 1);
console.log(`Presets: ${list.length}`, cats);

function frame(kind, i) {
  const loud = kind === 'loud', f = Reactor.state.frame;
  const a = loud ? 0.5 + 0.5 * Math.sin(i * 0.4) : 0;
  Object.assign(f, { bass: loud ? 0.8 * (0.5 + a) : 0, lowMid: loud ? 0.6 : 0, mid: loud ? 0.5 + 0.3 * a : 0, highMid: loud ? 0.4 : 0, treble: loud ? 0.5 * a : 0, energy: loud ? 0.6 : 0, volume: loud ? 0.7 : 0, beat: loud && i % 12 === 0, beatI: loud ? 0.9 : 0, bpm: loud ? 120 : 0 });
  f.spec = Array.from({ length: 64 }, (_, k) => loud ? Math.max(0, 0.9 - k / 70 + 0.2 * Math.sin(i * 0.3 + k)) : 0);
  f.wave = Array.from({ length: 256 }, (_, k) => loud ? 0.5 * Math.sin(k * 0.2 + i) : 0);
  f.waveL = Array.from({ length: 128 }, (_, k) => loud ? 0.5 * Math.sin(k * 0.3 + i) : 0); f.waveR = Array.from({ length: 128 }, (_, k) => loud ? 0.5 * Math.cos(k * 0.25 + i) : 0);
  if (f.beat) { Reactor.state.beatPulse = 1; }
}

function run(p, kind, quality) {
  Math.random = (() => { let s = 12345; return () => (s = (s * 1664525 + 1013904223) % 4294967296) / 4294967296; })();
  const log = sandbox.__log = [], ctx = makeCtx(log), inst = Reactor.get(p.name).factory(), S = Reactor.state;
  S.cfg.quality = quality; S.t = 0; S.beatPulse = 0;
  const cfg = S.cfg, o = { feat: Reactor.feat, q: { Low: 0.35, Medium: 0.65, High: 1, Ultra: 1.5 }[quality], intensity: 1, pal: cfg.palette, adj: cfg.adj };
  for (let i = 0; i < 60; i++) { frame(kind, i); S.dt = 1 / 60; S.t += S.dt; S.beatPulse *= 0.93; inst.draw(ctx, 1280, 720, S, o); }
  return log.join(',');
}

let fails = 0, nonReactive = [];
for (const p of list) {
  try {
    for (const q of ['Low', 'High', 'Ultra']) run(p, 'loud', q);
    const silent = run(p, 'silent', 'High'), loud = run(p, 'loud', 'High');
    if (silent === loud) nonReactive.push(p.name);
  } catch (e) { fails++; console.log(`FAIL  ${p.name}: ${e.stack.split('\n').slice(0, 3).join(' | ')}`); }
}
console.log(`Errors: ${fails}`);
console.log(`Not audio-reactive: ${nonReactive.length ? nonReactive.join(', ') : 'none'}`);
const names = list.map(p => p.name); const dup = names.filter((n, i) => names.indexOf(n) !== i);
console.log(`Duplicate names: ${dup.length ? dup.join(', ') : 'none'}`);
process.exit(fails || nonReactive.length || list.length < 50 || dup.length ? 1 : 0);
