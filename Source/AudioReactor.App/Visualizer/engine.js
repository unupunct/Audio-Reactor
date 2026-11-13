// Audio Reactor visualizer engine. Receives analysis frames from the host (WebView2 postMessage)
// and renders the active preset on a canvas. Presets are registered with Reactor.register().
const Reactor = (() => {
  const presets = new Map();
  const state = {
    frame: { bass: 0, lowMid: 0, mid: 0, highMid: 0, treble: 0, energy: 0, volume: 0, beat: false, beatI: 0, bpm: 0,
             spec: new Array(64).fill(0), wave: new Array(256).fill(0), waveL: new Array(128).fill(0), waveR: new Array(128).fill(0) },
    cfg: { preset: 'Spectrum Bars', intensity: 1, speed: 1, quality: 'High', fpsLimit: 60, showHud: true,
           palette: { primary: '#00e5ff', secondary: '#a64dff', background: '#05060a', accent: '#ff2bd6', particle: '#ffffff', glow: '#00e5ff' },
           // Mapping: which audio feature drives which visual parameter (editable in Visualizer Studio).
           map: { size: 'bass', motion: 'mid', light: 'treble', camera: 'beat', glow: 'volume' },
           adj: { brightness: 1, contrast: 1, saturation: 1, glow: 1, bloom: 0.5, particles: 1, particleSpeed: 1, rotation: 1,
                  camera: 1, zoom: 1, depth: 1, motion: 1, sens: 1, bassSens: 1, trebleSens: 1 } },
    t: 0, dt: 0.016, beatPulse: 0, flash: 0, fps: 0, prog: 0
  };
  let overlayT0 = 0;
  let canvas, ctx, hud, W = 0, H = 0, cur = null, last = 0, fpsAcc = 0, fpsN = 0, instance = null;

  function register(name, category, description, factory) { presets.set(name, { name, category, description, factory }); }
  function list() { return [...presets.values()].map(p => ({ name: p.name, category: p.category, description: p.description })); }

  function resize() {
    const rs = state.cfg.recordSize;
    if (rs && rs.w > 0 && rs.h > 0) { W = rs.w; H = rs.h; canvas.style.objectFit = 'contain'; }  // render at the exact recording resolution
    else {
      const dpr = Math.min(window.devicePixelRatio || 1, state.cfg.quality === 'Ultra' ? 2 : state.cfg.quality === 'High' ? 1.5 : 1);
      W = canvas.clientWidth * dpr | 0; H = canvas.clientHeight * dpr | 0;
    }
    if (canvas.width !== W || canvas.height !== H) { canvas.width = W; canvas.height = H; }
  }

  function select(name) {
    const p = presets.get(name) || presets.values().next().value;
    if (!p) return;
    cur = p; instance = p.factory(); state.cfg.preset = p.name;
    ctx.setTransform(1, 0, 0, 1, 0, 0); ctx.globalAlpha = 1; ctx.globalCompositeOperation = 'source-over';
    ctx.fillStyle = state.cfg.palette.background; ctx.fillRect(0, 0, W, H);
  }

  function post(msg) { try { window.chrome.webview.postMessage(msg); } catch (e) { } }

  function onMessage(ev) {
    const m = ev.data; if (!m) return;
    if (m.type === 'frame') {
      const f = state.frame, adj = state.cfg.adj;
      f.bass = Math.min(1, m.bass * adj.bassSens * adj.sens); f.lowMid = Math.min(1, m.lowMid * adj.sens); f.mid = Math.min(1, m.mid * adj.sens);
      f.highMid = Math.min(1, m.highMid * adj.sens); f.treble = Math.min(1, m.treble * adj.trebleSens * adj.sens);
      f.energy = m.energy; f.volume = m.volume; f.bpm = m.bpm; f.beatI = m.beatI;
      if (m.beat) { f.beat = true; state.beatPulse = 1; state.flash = Math.min(1, 0.4 + m.beatI * 0.6); }
      f.spec = m.spec; f.wave = m.wave; f.waveL = m.waveL; f.waveR = m.waveR; state.prog = m.prog || 0;
    } else if (m.type === 'config') {
      Object.assign(state.cfg, m.cfg || {});
      if (m.cfg && m.cfg.palette) state.cfg.palette = Object.assign({}, state.cfg.palette, m.cfg.palette);
      if (m.cfg && m.cfg.adj) state.cfg.adj = Object.assign({}, state.cfg.adj, m.cfg.adj);
      if (m.cfg && m.cfg.map) state.cfg.map = Object.assign({}, state.cfg.map, m.cfg.map);
      if (m.cfg && m.cfg.overlay) { state.cfg.overlay = Object.assign({}, state.cfg.overlay || {}, m.cfg.overlay); if (m.cfg.overlay.restart !== undefined) overlayT0 = performance.now(); }
      if (m.cfg && m.cfg.preset && (!cur || cur.name !== m.cfg.preset)) select(m.cfg.preset);
    } else if (m.type === 'list') post({ type: 'presets', list: list() });
    else if (m.type === 'rec-start') recStart(m);
    else if (m.type === 'rec-pause') { if (rec && rec.state === 'recording') rec.pause(); }
    else if (m.type === 'rec-resume') { if (rec && rec.state === 'paused') rec.resume(); }
    else if (m.type === 'rec-stop') { if (rec && rec.state !== 'inactive') rec.stop(); else post({ type: 'rec-stopped' }); }
  }

  // Resolve a mapped feature (bass/mid/treble/beat/volume/energy) to a 0..1 value.
  function feat(name) {
    const f = state.frame;
    switch (name) {
      case 'bass': return f.bass; case 'lowMid': return f.lowMid; case 'mid': return f.mid; case 'highMid': return f.highMid;
      case 'treble': return f.treble; case 'beat': return state.beatPulse; case 'volume': return f.volume; case 'energy': return f.energy;
      default: return 0;
    }
  }

  function frameLoop(now) {
    requestAnimationFrame(frameLoop);
    const minDt = 1000 / Math.max(15, state.cfg.fpsLimit);
    if (now - last < minDt - 1) return;
    const dt = Math.min(0.1, (now - last) / 1000); last = now;
    state.dt = dt * state.cfg.speed; state.t += state.dt;
    state.beatPulse *= Math.pow(0.001, dt * 1.2); state.flash *= Math.pow(0.0005, dt);
    resize();
    const q = { Low: 0.35, Medium: 0.65, High: 1, Ultra: 1.5 }[state.cfg.quality] || 1;
    if (instance) {
      ctx.save();
      const a = state.cfg.adj;
      try { instance.draw(ctx, W, H, state, { feat, q, intensity: state.cfg.intensity, pal: state.cfg.palette, adj: a }); } catch (e) { post({ type: 'error', message: String(e) }); }
      ctx.restore();
      drawOverlay(ctx, W, H);
      if (a.brightness !== 1 || a.contrast !== 1 || a.saturation !== 1) { /* applied via CSS filter below */ }
    }
    state.frame.beat = false;
    fpsAcc += dt; fpsN++;
    if (fpsAcc >= 1) { state.fps = Math.round(fpsN / fpsAcc); fpsAcc = 0; fpsN = 0; post({ type: 'fps', fps: state.fps }); }
    if (hud) hud.textContent = state.cfg.showHud ? `${cur ? cur.name : ''}  ·  ${state.fps} fps  ·  ${state.frame.bpm ? Math.round(state.frame.bpm) + ' BPM' : ''}` : '';
    const a2 = state.cfg.adj;
    canvas.style.filter = `brightness(${a2.brightness}) contrast(${a2.contrast}) saturate(${a2.saturation})`;
  }

  // ---- text overlay (title card / lower third), drawn after the preset so it is part of recorded video ----
  function drawOverlay(c, W, H) {
    const o = state.cfg.overlay; if (!o || !o.on) return;
    const el = (performance.now() - overlayT0) / 1000;
    let a = Math.min(1, Math.max(0, (el - 0.3) / 0.9));
    if (o.mode === 'first10') a *= Math.min(1, Math.max(0, (11 - el)));
    if (a <= 0.01 && !o.progress) return;
    const scale = (o.scale || 1) * H / 1080, col = o.color || '#ffffff', layout = o.layout || 'LowerThird';
    c.save(); c.textBaseline = 'alphabetic';
    if (a > 0.01 && (o.title || o.subtitle || o.footer)) {
      const tSize = 76 * scale, sSize = 40 * scale, fSize = 28 * scale, pad = 70 * scale;
      const F = (size, weight) => `${weight} ${size}px "Segoe UI Variable Display","Segoe UI",sans-serif`;
      const fit = (txt, size, maxW, weight) => { let s = size; c.font = F(s, weight); while (c.measureText(txt).width > maxW && s > size * 0.45) { s -= 2; c.font = F(s, weight); } return s; };
      let align = 'left', x = pad, yBase;
      if (layout === 'Center') { align = 'center'; x = W / 2; yBase = H * 0.5; }
      else if (layout === 'TopLeft') { align = 'left'; x = pad; yBase = pad + tSize; }
      else if (layout === 'BottomCenter') { align = 'center'; x = W / 2; yBase = H - pad - (o.subtitle ? sSize * 1.8 : 0) - (o.footer ? fSize * 1.6 : 0); }
      else { align = 'left'; x = pad; yBase = H - pad * 1.4 - (o.subtitle ? sSize * 1.5 : 0) - (o.footer ? fSize * 1.7 : 0); }
      const band = layout === 'TopLeft' ? [0, H * 0.32] : layout === 'Center' ? [H * 0.28, H * 0.72] : [H * 0.62, H];
      const g = c.createLinearGradient(0, band[0], 0, band[1]);
      if (layout === 'TopLeft') { g.addColorStop(0, `rgba(0,0,0,${0.6 * a})`); g.addColorStop(1, 'rgba(0,0,0,0)'); }
      else if (layout === 'Center') { g.addColorStop(0, 'rgba(0,0,0,0)'); g.addColorStop(0.5, `rgba(0,0,0,${0.45 * a})`); g.addColorStop(1, 'rgba(0,0,0,0)'); }
      else { g.addColorStop(0, 'rgba(0,0,0,0)'); g.addColorStop(1, `rgba(0,0,0,${0.65 * a})`); }
      c.fillStyle = g; c.fillRect(0, band[0], W, band[1] - band[0]);
      c.textAlign = align; c.globalAlpha = a; c.fillStyle = col; c.shadowColor = 'rgba(0,0,0,0.8)'; c.shadowBlur = 14 * scale;
      const slide = (1 - a) * 24 * scale; let y = yBase + slide;
      if (o.title) { const sz = fit(o.title, tSize, W - pad * 2, 700); c.fillText(o.title, x, y); y += sz * 0.95 + 8 * scale; }
      if (o.subtitle) { fit(o.subtitle, sSize, W - pad * 2, 500); c.globalAlpha = a * 0.85; c.fillText(o.subtitle, x, y + sSize * 0.6); y += sSize * 1.5; }
      if (o.footer) { fit(o.footer, fSize, W - pad * 2, 400); c.globalAlpha = a * 0.65; c.fillText(o.footer, x, y + fSize * 0.8); }
    }
    if (o.progress && state.prog > 0) { c.globalAlpha = 0.9; c.shadowBlur = 0; c.fillStyle = state.cfg.palette.primary; c.fillRect(0, H - 6 * scale, W * Math.min(1, state.prog), 6 * scale); }
    c.restore();
  }

  // ---- recording: capture the canvas stream with MediaRecorder; chunks are streamed to the host as base64 ----
  let rec = null;
  function recStart(m) {
    try {
      const stream = canvas.captureStream(m.fps || 30);
      const types = ['video/webm;codecs=vp9', 'video/webm;codecs=vp8', 'video/webm'];
      const mime = types.find(t => window.MediaRecorder && MediaRecorder.isTypeSupported(t));
      if (!mime) { post({ type: 'rec-error', message: 'MediaRecorder is not available' }); return; }
      rec = new MediaRecorder(stream, { mimeType: mime, videoBitsPerSecond: (m.kbps || 8000) * 1000 });
      rec.onstart = () => { overlayT0 = performance.now(); post({ type: 'rec-started', t: performance.now() }); };
      rec.ondataavailable = (e) => {
        if (!e.data || !e.data.size) return;
        const fr = new FileReader(); fr.onload = () => post({ type: 'rec-chunk', data: String(fr.result).split(',')[1] }); fr.readAsDataURL(e.data);
      };
      rec.onstop = () => setTimeout(() => post({ type: 'rec-stopped' }), 300); // let the last chunk reader finish
      rec.onerror = (e) => post({ type: 'rec-error', message: String(e.error || e) });
      rec.start(500);
    } catch (e) { post({ type: 'rec-error', message: String(e) }); }
  }

  function start(c, h) {
    canvas = c; hud = h; ctx = c.getContext('2d', { alpha: false });
    if (window.chrome && window.chrome.webview) window.chrome.webview.addEventListener('message', onMessage);
    window.addEventListener('message', onMessage);
    resize(); select(state.cfg.preset);
    requestAnimationFrame(frameLoop);
    post({ type: 'ready', presets: list() });
  }

  // ---- shared helpers for presets ----
  const util = {
    hexToRgb(h) { const n = parseInt(h.replace('#', ''), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; },
    rgba(h, a) { const [r, g, b] = util.hexToRgb(h); return `rgba(${r},${g},${b},${a})`; },
    mix(h1, h2, t) { const a = util.hexToRgb(h1), b = util.hexToRgb(h2); return `rgb(${a.map((v, i) => Math.round(v + (b[i] - v) * t)).join(',')})`; },
    hsl(h, s, l, a = 1) { return `hsla(${((h % 360) + 360) % 360},${s}%,${l}%,${a})`; },
    fade(ctx, W, H, bg, a) { ctx.globalAlpha = a; ctx.fillStyle = bg; ctx.fillRect(0, 0, W, H); ctx.globalAlpha = 1; },
    rnd: (a, b) => a + Math.random() * (b - a),
    // value noise (cheap, deterministic) for organic motion
    noise(x, y) { const s = Math.sin(x * 127.1 + y * 311.7) * 43758.5453; return s - Math.floor(s); },
    smoothNoise(x, y) {
      const xi = Math.floor(x), yi = Math.floor(y), xf = x - xi, yf = y - yi;
      const u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf), n = util.noise;
      return n(xi, yi) * (1 - u) * (1 - v) + n(xi + 1, yi) * u * (1 - v) + n(xi, yi + 1) * (1 - u) * v + n(xi + 1, yi + 1) * u * v;
    }
  };

  return { register, list, get: (n) => presets.get(n), start, select, state, util, feat };
})();
