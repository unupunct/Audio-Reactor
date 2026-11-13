// 3D-style visualizers (software-projected) and abstract effects.
(() => {
  const R = Reactor, U = R.util, TAU = Math.PI * 2;
  const rot = (x, y, z, ax, ay) => { let c = Math.cos(ay), s = Math.sin(ay); let x1 = x * c + z * s, z1 = -x * s + z * c; c = Math.cos(ax); s = Math.sin(ax); const y1 = y * c - z1 * s; z1 = y * s + z1 * c; return [x1, y1, z1]; };
  const proj = (p, W, H, f) => { const k = f / (f + p[2]); return [W / 2 + p[0] * k, H / 2 + p[1] * k, k]; };

  // Low-res field renderer used by plasma-style effects (cheap on CPU, scaled up with smoothing).
  function fieldCanvas() {
    let cv = null, cx = null, img = null, w = 0, h = 0;
    return (W, H, q, fn) => {
      const nw = Math.max(32, Math.round(96 * q)), nh = Math.max(18, Math.round(54 * q));
      if (!cv || nw !== w) { cv = document.createElement('canvas'); w = cv.width = nw; h = cv.height = nh; cx = cv.getContext('2d'); img = cx.createImageData(w, h); }
      const d = img.data;
      for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) { const [r, g, b] = fn(x / w, y / h); const i = (y * w + x) * 4; d[i] = r; d[i + 1] = g; d[i + 2] = b; d[i + 3] = 255; }
      cx.putImageData(img, 0, 0); return cv;
    };
  }
  const hsv = (h, s, v) => { h = ((h % 1) + 1) % 1; const i = Math.floor(h * 6), f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s); const m = [[v, t, p], [q, v, p], [p, v, t], [p, q, v], [t, p, v], [v, p, q]][i % 6]; return [m[0] * 255, m[1] * 255, m[2] * 255]; };

  R.register('3D Frequency Mountains', '3D', 'A perspective terrain whose ridges are the live spectrum, scrolling toward you.', () => {
    const rows = []; let acc = 0;
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 1);
      acc += S.dt * (4 + S.frame.energy * 8) * o.adj.motion; while (acc >= 1) { acc -= 1; rows.unshift(Array.from(S.frame.spec)); if (rows.length > 26) rows.pop(); }
      const cols = 64, cam = S.t * 0.2 * o.adj.camera;
      for (let r = rows.length - 1; r >= 0; r--) {
        const z = (r + acc) / 26, y0 = H * (0.38 + z * z * 0.55), sc = 0.3 + z * 1.4; c.beginPath();
        for (let i = 0; i < cols; i++) { const x = W / 2 + (i / (cols - 1) - 0.5) * W * sc * 1.4 + Math.sin(cam) * 30 * z, y = y0 - rows[r][i] * H * 0.3 * sc * o.intensity * o.adj.depth; i ? c.lineTo(x, y) : c.moveTo(x, y); }
        c.lineTo(W / 2 + 0.7 * W * sc * 1.4, H); c.lineTo(W / 2 - 0.7 * W * sc * 1.4, H); c.closePath(); c.fillStyle = U.rgba(o.pal.background, 0.92); c.fill();
        c.strokeStyle = U.mix(o.pal.secondary, o.pal.primary, z); c.lineWidth = Math.max(1, W / 900) + z * 1.5; c.shadowColor = o.pal.glow; c.shadowBlur = 6 * o.adj.glow * o.q; c.stroke();
      }
      c.shadowBlur = 0;
    } };
  });

  R.register('Reactive Spheres', '3D', 'A lattice of glowing spheres whose sizes are driven by individual frequency bands.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.3);
      const n = 6, pts = []; const ay = S.t * 0.4 * o.adj.rotation, ax = 0.5 + Math.sin(S.t * 0.2) * 0.3 * o.adj.camera, sp = Math.min(W, H) * 0.12 * o.adj.depth;
      for (let x = 0; x < n; x++) for (let y = 0; y < n; y++) for (let z = 0; z < n; z++) {
        const idx = (x * 7 + y * 3 + z * 5) % 64, v = S.frame.spec[idx]; const p = rot((x - 2.5) * sp, (y - 2.5) * sp, (z - 2.5) * sp, ax, ay); const q = proj(p, W, H, Math.min(W, H) * 1.6); pts.push({ q, v, idx, d: p[2] });
      }
      pts.sort((a, b) => b.d - a.d); c.globalCompositeOperation = 'lighter';
      for (const p of pts) { const r = (4 + p.v * 26 * o.intensity + S.beatPulse * 4) * p.q[2] * (W / 1200) * 1.4; const g = c.createRadialGradient(p.q[0], p.q[1], 0, p.q[0], p.q[1], r); const col = U.mix(o.pal.primary, o.pal.accent, p.idx / 64); g.addColorStop(0, col); g.addColorStop(1, 'transparent'); c.fillStyle = g; c.globalAlpha = 0.35 + p.v * 0.65; c.fillRect(p.q[0] - r, p.q[1] - r, r * 2, r * 2); }
      c.globalAlpha = 1; c.globalCompositeOperation = 'source-over';
    } }));

  R.register('Geometric Sculptures', '3D', 'A wireframe icosa-like solid that morphs: bass inflates it, treble twists it.', () => {
    const v = []; const phi = (1 + Math.sqrt(5)) / 2;
    [[-1, phi, 0], [1, phi, 0], [-1, -phi, 0], [1, -phi, 0], [0, -1, phi], [0, 1, phi], [0, -1, -phi], [0, 1, -phi], [phi, 0, -1], [phi, 0, 1], [-phi, 0, -1], [-phi, 0, 1]].forEach(p => { const l = Math.hypot(...p); v.push(p.map(x => x / l)); });
    const edges = []; for (let i = 0; i < 12; i++) for (let j = i + 1; j < 12; j++) { const d = Math.hypot(v[i][0] - v[j][0], v[i][1] - v[j][1], v[i][2] - v[j][2]); if (d < 1.1) edges.push([i, j]); }
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.22);
      const m = Math.min(W, H), size = m * (0.22 + S.frame.bass * 0.12 + S.beatPulse * 0.04) * o.adj.zoom, ay = S.t * 0.5 * o.adj.rotation + S.frame.treble * 0.8, ax = S.t * 0.3 * o.adj.rotation;
      for (let layer = 0; layer < 3; layer++) {
        const sc = size * (1 + layer * 0.35), tw = layer * (0.4 + S.frame.treble);
        const P = v.map((p, i) => { const k = 1 + S.frame.spec[(i * 5 + layer * 9) % 64] * 0.45 * o.intensity; return proj(rot(p[0] * sc * k, p[1] * sc * k, p[2] * sc * k, ax + tw, ay - tw), W, H, m * 2); });
        c.strokeStyle = U.mix(o.pal.primary, o.pal.accent, layer / 2); c.lineWidth = Math.max(1.5, W / 500); c.shadowColor = c.strokeStyle; c.shadowBlur = 10 * o.adj.glow * o.q; c.globalAlpha = 0.9 - layer * 0.25;
        c.beginPath(); for (const [a, b] of edges) { c.moveTo(P[a][0], P[a][1]); c.lineTo(P[b][0], P[b][1]); } c.stroke();
      }
      c.globalAlpha = 1; c.shadowBlur = 0;
    } };
  });

  R.register('3D Sound Waves', '3D', 'A rippling water-like sheet displaced by the spectrum and beats.', () => {
    const rings = [];
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 1);
      if (S.frame.beat) rings.push({ r: 0, a: 1 }); for (let i = rings.length - 1; i >= 0; i--) { rings[i].r += S.dt * 5 * o.adj.motion; rings[i].a -= S.dt * 0.5; if (rings[i].a <= 0) rings.splice(i, 1); }
      const gx = Math.floor(34 * Math.max(0.5, o.q)), gz = Math.floor(22 * Math.max(0.5, o.q)), ay = Math.sin(S.t * 0.15) * 0.5 * o.adj.camera, ax = 0.95;
      const grid = [];
      for (let z = 0; z < gz; z++) { const row = []; for (let x = 0; x < gx; x++) {
        const fx = (x / (gx - 1) - 0.5) * 2, fz = (z / (gz - 1) - 0.5) * 2, d = Math.hypot(fx, fz); let h = S.frame.spec[Math.min(63, Math.floor(d * 40))] * 0.5 * o.intensity + Math.sin(d * 9 - S.t * 3) * S.frame.bass * 0.15;
        for (const r of rings) h += Math.exp(-Math.pow((d * 5 - r.r), 2) * 2) * 0.35 * r.a;
        const p = rot(fx * W * 0.4, -h * H * 0.35 * o.adj.depth, fz * W * 0.3, ax, ay); row.push(proj(p, W, H, W * 1.2).concat([h])); } grid.push(row); }
      c.lineWidth = Math.max(1, W / 900);
      for (let z = 0; z < gz; z++) { c.beginPath(); for (let x = 0; x < gx; x++) { const p = grid[z][x]; x ? c.lineTo(p[0], p[1]) : c.moveTo(p[0], p[1]); } c.strokeStyle = U.mix(o.pal.secondary, o.pal.primary, z / gz); c.globalAlpha = 0.35 + 0.65 * z / gz; c.stroke(); }
      for (let x = 0; x < gx; x += 2) { c.beginPath(); for (let z = 0; z < gz; z++) { const p = grid[z][x]; z ? c.lineTo(p[0], p[1]) : c.moveTo(p[0], p[1]); } c.strokeStyle = U.rgba(o.pal.primary, 0.35); c.stroke(); }
      c.globalAlpha = 1;
    } };
  });

  R.register('Infinite Audio Tunnel', '3D', 'Hexagonal wormhole; each ring is a spectrum band that flashes as it rushes past.', () => {
    let z = 0;
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.3);
      z += (0.5 + S.frame.energy * 3 + S.beatPulse * 2) * S.dt * o.adj.motion; const cx = W / 2 + Math.sin(S.t * 0.8) * W * 0.04 * o.adj.camera, cy = H / 2 + Math.cos(S.t * 0.6) * H * 0.04 * o.adj.camera, rings = Math.floor(26 * Math.max(0.5, o.q));
      c.lineWidth = Math.max(1.5, W / 450);
      for (let i = rings; i >= 1; i--) {
        const d = (i - (z % 1)) / rings, s = Math.pow(1 - d, 2.4) * Math.max(W, H) * 0.8; if (s < 2) continue; const band = S.frame.spec[(i * 2 + Math.floor(z)) % 64];
        const col = U.mix(o.pal.primary, o.pal.accent, band); c.strokeStyle = col; c.shadowColor = col; c.shadowBlur = (4 + band * 18) * o.adj.glow * o.q; c.globalAlpha = Math.min(1, (1 - d) * (0.5 + band));
        c.beginPath(); for (let k = 0; k <= 6; k++) { const a = k / 6 * TAU + S.t * 0.3 * o.adj.rotation + i * 0.05; k ? c.lineTo(cx + Math.cos(a) * s * 0.5, cy + Math.sin(a) * s * 0.5) : c.moveTo(cx + Math.cos(a) * s * 0.5, cy + Math.sin(a) * s * 0.5); } c.stroke();
      }
      c.globalAlpha = 1; c.shadowBlur = 0;
    } };
  });

  R.register('Plasma Environment', '3D', 'A volumetric-looking plasma field whose frequency and hue shift with the music.', () => {
    const f = fieldCanvas();
    return { draw(c, W, H, S, o) {
      const t = S.t, b = S.frame.bass, m = S.frame.mid, tr = S.frame.treble;
      const cv = f(W, H, o.q, (x, y) => { const v = Math.sin(x * (6 + b * 6) + t) + Math.sin(y * (7 + m * 5) - t * 1.3) + Math.sin((x + y) * (5 + tr * 6) + t * 0.7) + Math.sin(Math.hypot(x - 0.5, y - 0.5) * (12 + b * 10) - t * 2); return hsv(v * 0.12 + t * 0.03 + b * 0.2, 0.75, 0.35 + 0.65 * (0.5 + 0.5 * Math.sin(v * 2)) * (0.6 + S.frame.energy * 0.6 * o.intensity)); });
      c.imageSmoothingEnabled = true; c.drawImage(cv, 0, 0, W, H);
    } };
  });

  // ---------- Abstract ----------
  R.register('Liquid Plasma', 'Abstract', 'Metaball-style blobs merge and split, pushed around by the bass.', () => {
    const f = fieldCanvas(); const blobs = Array.from({ length: 6 }, (_, i) => ({ a: i * 1.047, s: 0.3 + i * 0.11 }));
    return { draw(c, W, H, S, o) {
      const pos = blobs.map((b, i) => [0.5 + Math.cos(S.t * b.s + b.a) * 0.3, 0.5 + Math.sin(S.t * b.s * 1.3 + b.a * 2) * 0.28, 0.05 + S.frame.spec[i * 8] * 0.12 * o.intensity + S.frame.bass * 0.04]);
      const p1 = U.hexToRgb(o.pal.primary), p2 = U.hexToRgb(o.pal.secondary), bg = U.hexToRgb(o.pal.background);
      const cv = f(W, H, o.q, (x, y) => { let v = 0; for (const p of pos) { const dx = x - p[0], dy = (y - p[1]) * 0.6; v += p[2] * p[2] / (dx * dx + dy * dy + 0.0005); } const k = Math.min(1, v * 0.35), e = Math.max(0, Math.min(1, (v - 1) * 0.5)); return [bg[0] + (p2[0] - bg[0]) * k + (p1[0] - p2[0]) * e, bg[1] + (p2[1] - bg[1]) * k + (p1[1] - p2[1]) * e, bg[2] + (p2[2] - bg[2]) * k + (p1[2] - p2[2]) * e]; });
      c.imageSmoothingEnabled = true; c.drawImage(cv, 0, 0, W, H);
    } };
  });

  R.register('Fractal Patterns', 'Abstract', 'A zooming Julia set whose constant is steered by bass and treble.', () => {
    const f = fieldCanvas();
    return { draw(c, W, H, S, o) {
      const cr = -0.75 + Math.cos(S.t * 0.2) * 0.1 + S.frame.bass * 0.08, ci = 0.12 + Math.sin(S.t * 0.27) * 0.1 + S.frame.treble * 0.08, zoom = 1.6 / (1 + S.frame.energy * 0.4 * o.adj.zoom), it = Math.floor(18 + 20 * o.q);
      const cv = f(W, H, Math.min(o.q, 1) * 0.8, (x, y) => { let zr = (x - 0.5) * 3 * zoom * (W / H) * 0.6, zi = (y - 0.5) * 3 * zoom, n = 0; while (n < it && zr * zr + zi * zi < 4) { const t = zr * zr - zi * zi + cr; zi = 2 * zr * zi + ci; zr = t; n++; } return n >= it ? [5, 5, 12] : hsv(0.55 + n / it * 0.5 + S.t * 0.02, 0.8, Math.min(1, 0.25 + n / it * 1.2)); });
      c.imageSmoothingEnabled = true; c.drawImage(cv, 0, 0, W, H);
    } };
  });

  R.register('Fluid Waves', 'Abstract', 'Layered sine ribbons flow like silk; amplitude is the spectrum, speed is the energy.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.18); c.globalCompositeOperation = 'lighter'; const layers = Math.floor(7 * Math.max(0.6, o.q));
      for (let l = 0; l < layers; l++) {
        const amp = H * (0.06 + S.frame.spec[l * 8] * 0.2 * o.intensity), ph = S.t * (0.6 + S.frame.energy) * o.adj.motion * (l % 2 ? -1 : 1) + l; c.beginPath();
        for (let x = 0; x <= W; x += 8) { const u = x / W, y = H / 2 + Math.sin(u * (3 + l) * 2 + ph) * amp + Math.sin(u * 7 + ph * 1.7) * amp * 0.4 + (l - layers / 2) * H * 0.03; x ? c.lineTo(x, y) : c.moveTo(x, y); }
        c.strokeStyle = U.mix(o.pal.primary, o.pal.secondary, l / layers); c.lineWidth = Math.max(2, W / 300) * (1 + S.beatPulse * 0.5); c.shadowColor = c.strokeStyle; c.shadowBlur = 12 * o.adj.glow * o.q; c.globalAlpha = 0.7; c.stroke();
      }
      c.globalAlpha = 1; c.shadowBlur = 0; c.globalCompositeOperation = 'source-over';
    } }));

  R.register('Kaleidoscope', 'Abstract', 'Eight-fold mirrored shapes spun by the mids and flashed by the beat.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.2); const cx = W / 2, cy = H / 2, m = Math.min(W, H) * 0.5, seg = 8;
      for (let s = 0; s < seg; s++) {
        c.save(); c.translate(cx, cy); c.rotate(s * TAU / seg + S.t * 0.25 * o.adj.rotation); if (s % 2) c.scale(1, -1);
        for (let i = 0; i < 16; i++) { const v = S.frame.spec[i * 4], r = m * (0.1 + i / 16 * 0.9), a = Math.sin(S.t * 0.8 + i) * 0.35 * (0.3 + S.frame.mid); const col = U.mix(o.pal.primary, o.pal.accent, i / 16);
          c.fillStyle = col; c.globalAlpha = 0.25 + v * 0.7; c.shadowColor = col; c.shadowBlur = 8 * o.adj.glow * o.q; c.beginPath(); c.arc(Math.cos(a) * r, Math.sin(a) * r * 0.6, 2 + v * 16 * o.intensity + S.beatPulse * 5, 0, TAU); c.fill(); }
        c.restore();
      }
      c.globalAlpha = 1; c.shadowBlur = 0;
    } }));

  R.register('Electric Energy', 'Abstract', 'Branching lightning arcs jump between the centre and the edges on every beat.', () => {
    const bolts = [];
    const make = (x0, y0, x1, y1, d) => { const pts = [[x0, y0]]; const n = 14; for (let i = 1; i < n; i++) { const t = i / n, j = (1 - Math.abs(t - 0.5) * 1.4) * d; pts.push([x0 + (x1 - x0) * t + (Math.random() - 0.5) * j, y0 + (y1 - y0) * t + (Math.random() - 0.5) * j]); } pts.push([x1, y1]); return pts; };
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.3); const cx = W / 2, cy = H / 2;
      if (S.frame.beat || Math.random() < S.frame.treble * 0.25) { const n = 1 + Math.floor(S.frame.beatI * 4 + S.frame.treble * 3); for (let i = 0; i < n; i++) { const a = Math.random() * TAU, r = Math.max(W, H) * (0.3 + Math.random() * 0.3); bolts.push({ p: make(cx, cy, cx + Math.cos(a) * r, cy + Math.sin(a) * r, Math.min(W, H) * 0.12), life: 1 }); } }
      c.lineJoin = 'round';
      for (let i = bolts.length - 1; i >= 0; i--) { const b = bolts[i]; b.life -= S.dt * 3; if (b.life <= 0) { bolts.splice(i, 1); continue; }
        c.strokeStyle = o.pal.particle; c.shadowColor = o.pal.glow; c.shadowBlur = 20 * o.adj.glow * o.q; c.globalAlpha = b.life; c.lineWidth = Math.max(1.5, W / 500) * (1 + b.life * 2); c.beginPath(); b.p.forEach((p, k) => k ? c.lineTo(p[0], p[1]) : c.moveTo(p[0], p[1])); c.stroke(); }
      if (bolts.length > 24) bolts.splice(0, bolts.length - 24);
      c.globalAlpha = 1; const r = Math.min(W, H) * (0.03 + S.frame.bass * 0.05), g = c.createRadialGradient(cx, cy, 0, cx, cy, r * 4); g.addColorStop(0, U.rgba(o.pal.glow, 0.8)); g.addColorStop(1, 'transparent'); c.fillStyle = g; c.fillRect(cx - r * 4, cy - r * 4, r * 8, r * 8); c.shadowBlur = 0;
    } };
  });

  R.register('Neon Fractals', 'Abstract', 'A recursive neon tree whose branches sway with the spectrum and bloom on beats.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.25); const maxD = Math.floor(7 + 2 * o.q); c.lineCap = 'round'; c.shadowBlur = 8 * o.adj.glow * o.q;
      const branch = (x, y, len, ang, d) => {
        if (d > maxD || len < 2) return; const x2 = x + Math.cos(ang) * len, y2 = y + Math.sin(ang) * len, col = U.mix(o.pal.primary, o.pal.accent, d / maxD);
        c.strokeStyle = col; c.shadowColor = col; c.lineWidth = Math.max(1, (maxD - d) * W / 900 + 0.5); c.beginPath(); c.moveTo(x, y); c.lineTo(x2, y2); c.stroke();
        const spread = 0.35 + S.frame.spec[(d * 8) % 64] * 0.6 * o.intensity + S.beatPulse * 0.15, sway = Math.sin(S.t * 0.7 + d) * 0.12 * (0.5 + S.frame.mid);
        branch(x2, y2, len * (0.72 + S.frame.bass * 0.06), ang - spread + sway, d + 1); branch(x2, y2, len * (0.72 + S.frame.bass * 0.06), ang + spread + sway, d + 1);
      };
      branch(W / 2, H * 0.97, H * (0.2 + S.frame.bass * 0.04), -Math.PI / 2, 0); c.shadowBlur = 0;
    } }));

  R.register('Organic Waveforms', 'Abstract', 'A living blob outline deformed by the waveform and noise, like a pulsing cell.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.2); const cx = W / 2, cy = H / 2, m = Math.min(W, H) * 0.25, w = S.frame.wave;
      for (let l = 0; l < 4; l++) {
        c.beginPath(); for (let i = 0; i <= 120; i++) { const a = i / 120 * TAU, k = i % 120, r = m * (1 + l * 0.25) * (1 + w[(k * 2) % w.length] * 0.8 * o.intensity + U.smoothNoise(Math.cos(a) * 2 + S.t * 0.4 + l, Math.sin(a) * 2) * 0.35 + S.frame.bass * 0.2); i ? c.lineTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r) : c.moveTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r); } c.closePath();
        c.strokeStyle = U.mix(o.pal.primary, o.pal.secondary, l / 3); c.lineWidth = Math.max(2, W / 400); c.shadowColor = c.strokeStyle; c.shadowBlur = 14 * o.adj.glow * o.q; c.globalAlpha = 0.9 - l * 0.15; c.stroke(); if (l === 0) { c.fillStyle = U.rgba(o.pal.primary, 0.08 + S.beatPulse * 0.12); c.fill(); }
      }
      c.globalAlpha = 1; c.shadowBlur = 0;
    } }));

  R.register('Procedural Geometry', 'Abstract', 'Rotating nested polygons; the side count and scale follow the frequency bands.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.22); const cx = W / 2, cy = H / 2, m = Math.min(W, H) * 0.45;
      for (let i = 0; i < 14; i++) { const v = S.frame.spec[i * 4], sides = 3 + (i % 5) + Math.floor(S.frame.treble * 3), r = m * (0.12 + i * 0.06) * (1 + v * 0.35 * o.intensity), a0 = S.t * (0.2 + i * 0.03) * o.adj.rotation * (i % 2 ? -1 : 1);
        c.beginPath(); for (let k = 0; k <= sides; k++) { const a = a0 + k / sides * TAU; k ? c.lineTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r) : c.moveTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r); }
        c.strokeStyle = U.mix(o.pal.primary, o.pal.accent, i / 14); c.shadowColor = c.strokeStyle; c.shadowBlur = 8 * o.adj.glow * o.q; c.lineWidth = Math.max(1.5, W / 500); c.globalAlpha = 0.45 + v * 0.55; c.stroke(); }
      c.globalAlpha = 1; c.shadowBlur = 0;
    } }));
})();
