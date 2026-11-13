// Environmental scenes + extra generative presets.
(() => {
  const R = Reactor, U = R.util, TAU = Math.PI * 2;

  R.register('Digital Galaxy', 'Environment', 'A flat-lit spiral galaxy built from the spectrum, with a pulsing core and dust lanes.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.14); const cx = W / 2, cy = H / 2, m = Math.min(W, H) * 0.46; c.globalCompositeOperation = 'lighter';
      for (let arm = 0; arm < 4; arm++) for (let i = 0; i < 64; i++) {
        const v = S.frame.spec[i], t = i / 63, a = arm * TAU / 4 + t * 5 + S.t * 0.12 * o.adj.rotation, r = t * m * (1 + S.frame.bass * 0.1), n = Math.floor((2 + v * 8) * o.q);
        for (let k = 0; k < n; k++) { const jitter = (Math.random() - 0.5) * (0.08 + t * 0.15), x = cx + Math.cos(a + jitter) * (r + (Math.random() - 0.5) * 20), y = cy + Math.sin(a + jitter) * (r + (Math.random() - 0.5) * 20) * 0.62;
          c.fillStyle = U.mix(o.pal.accent, o.pal.primary, t); c.globalAlpha = 0.15 + v * 0.7; const s = (1 + v * 3) * (W / 1200); c.fillRect(x, y, s, s); }
      }
      const g = c.createRadialGradient(cx, cy, 0, cx, cy, m * 0.3); g.addColorStop(0, U.rgba(o.pal.particle, 0.9)); g.addColorStop(0.3, U.rgba(o.pal.glow, 0.4 + S.beatPulse * 0.4)); g.addColorStop(1, 'transparent'); c.globalAlpha = 1; c.fillStyle = g; c.fillRect(0, 0, W, H);
      c.globalCompositeOperation = 'source-over';
    } }));

  R.register('Underwater World', 'Environment', 'Caustic light, rising bubbles and swaying kelp; the bass swells the whole sea.', () => {
    const bub = Array.from({ length: 70 }, () => ({ x: Math.random(), y: Math.random(), r: 2 + Math.random() * 7, s: 0.04 + Math.random() * 0.1 }));
    return { draw(c, W, H, S, o) {
      const g = c.createLinearGradient(0, 0, 0, H); g.addColorStop(0, U.mix(o.pal.primary, o.pal.background, 0.7)); g.addColorStop(1, o.pal.background); c.fillStyle = g; c.fillRect(0, 0, W, H);
      c.globalCompositeOperation = 'lighter'; for (let i = 0; i < 6; i++) { const x = (i / 6 + Math.sin(S.t * 0.2 + i) * 0.05) * W; const gg = c.createLinearGradient(x, 0, x + W * 0.1, H * 0.8); gg.addColorStop(0, U.rgba(o.pal.glow, 0.10 + S.frame.mid * 0.12)); gg.addColorStop(1, 'transparent'); c.fillStyle = gg; c.beginPath(); c.moveTo(x, 0); c.lineTo(x + W * 0.08, 0); c.lineTo(x + W * 0.2, H * 0.85); c.lineTo(x + W * 0.04, H * 0.85); c.fill(); }
      c.globalCompositeOperation = 'source-over';
      for (let k = 0; k < 28; k++) { const x = (k + 0.5) / 28 * W, h = H * (0.1 + S.frame.spec[k * 2] * 0.35 * o.intensity), sw = Math.sin(S.t * 1.2 + k) * 18 * (0.5 + S.frame.bass); c.strokeStyle = U.mix(o.pal.secondary, o.pal.accent, k / 28); c.lineWidth = Math.max(3, W / 160); c.lineCap = 'round'; c.beginPath(); c.moveTo(x, H); c.quadraticCurveTo(x + sw, H - h * 0.5, x + sw * 1.6, H - h); c.stroke(); }
      const n = Math.floor(bub.length * o.q * o.adj.particles); c.strokeStyle = U.rgba(o.pal.particle, 0.6); c.lineWidth = 1.2;
      for (let i = 0; i < Math.min(n, bub.length); i++) { const b = bub[i]; b.y -= b.s * S.dt * (1 + S.frame.energy * 4); b.x += Math.sin(S.t + i) * 0.0004; if (b.y < -0.05) { b.y = 1.05; b.x = Math.random(); } c.beginPath(); c.arc(b.x * W, b.y * H, b.r * (W / 1200) * (1 + S.frame.bass * 0.5), 0, TAU); c.stroke(); }
    } };
  });

  R.register('Cyberpunk City', 'Environment', 'A skyline of neon towers; each window-lit building is a frequency band.', () => {
    const rnd = (i) => (Math.sin(i * 91.7) * 43758.5453) % 1;
    return { draw(c, W, H, S, o) {
      const g = c.createLinearGradient(0, 0, 0, H); g.addColorStop(0, o.pal.background); g.addColorStop(0.7, U.mix(o.pal.secondary, o.pal.background, 0.55)); g.addColorStop(1, o.pal.background); c.fillStyle = g; c.fillRect(0, 0, W, H);
      const sun = Math.min(W, H) * (0.12 + S.frame.bass * 0.03); c.fillStyle = U.rgba(o.pal.accent, 0.7); c.beginPath(); c.arc(W * 0.72, H * 0.35, sun, 0, TAU); c.fill();
      const n = 36, bw = W / n;
      for (let layer = 0; layer < 2; layer++) for (let i = 0; i < n; i++) {
        const v = S.frame.spec[Math.floor(i * 64 / n)], base = 0.2 + Math.abs(rnd(i + layer * 50)) * 0.3, h = H * (base + v * 0.3 * o.intensity) * (layer ? 0.7 : 1), x = i * bw, y = H - h;
        c.fillStyle = layer ? '#0a0c14' : '#10131f'; c.fillRect(x + (layer ? bw * 0.3 : 0), y, bw * 0.92, h);
        const col = U.mix(o.pal.primary, o.pal.accent, i / n); c.fillStyle = col; c.shadowColor = col; c.shadowBlur = 8 * o.adj.glow * o.q; c.fillRect(x + (layer ? bw * 0.3 : 0), y, bw * 0.92, 3);
        c.shadowBlur = 0; const rows = Math.floor(h / (H * 0.03)); for (let r = 1; r < rows; r++) for (let k = 0; k < 3; k++) if (Math.abs(rnd(i * 13 + r * 7 + k + layer * 99)) < 0.35 + v * 0.5) { c.fillStyle = U.rgba(k % 2 ? o.pal.particle : col, 0.7); c.fillRect(x + (layer ? bw * 0.3 : 0) + bw * (0.12 + k * 0.28), y + r * H * 0.03, bw * 0.14, H * 0.014); }
      }
      if (S.beatPulse > 0.2) { c.fillStyle = U.rgba(o.pal.glow, S.beatPulse * 0.12); c.fillRect(0, 0, W, H); }
    } };
  });

  R.register('Space Environment', 'Environment', 'Parallax star layers, drifting planets and a pulsing nearby star.', () => {
    const stars = Array.from({ length: 240 }, () => ({ x: Math.random(), y: Math.random(), z: Math.random() }));
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 1); const n = Math.floor(stars.length * o.q);
      for (let i = 0; i < Math.min(n, stars.length); i++) { const s = stars[i]; s.x -= S.dt * 0.01 * (0.3 + s.z) * (1 + S.frame.energy * 5) * o.adj.motion; if (s.x < 0) { s.x = 1; s.y = Math.random(); } c.fillStyle = U.rgba(o.pal.particle, 0.3 + s.z * 0.7); const sz = (0.6 + s.z * 1.8 + S.frame.treble * 1.2 * s.z) * (W / 1200); c.fillRect(s.x * W, s.y * H, sz, sz); }
      const planets = [[0.25, 0.62, 0.12, o.pal.secondary, 0.05], [0.78, 0.3, 0.07, o.pal.primary, 0.08]];
      planets.forEach(([px, py, pr, col, sp], i) => { const x = (px + Math.sin(S.t * sp + i) * 0.03) * W, y = (py + Math.cos(S.t * sp) * 0.02) * H, r = pr * Math.min(W, H) * (1 + S.frame.spec[10 + i * 20] * 0.12 * o.intensity); const g = c.createRadialGradient(x - r * 0.3, y - r * 0.3, r * 0.1, x, y, r); g.addColorStop(0, U.mix(col, '#ffffff', 0.35)); g.addColorStop(1, U.mix(col, '#000000', 0.75)); c.fillStyle = g; c.beginPath(); c.arc(x, y, r, 0, TAU); c.fill(); c.strokeStyle = U.rgba(col, 0.5); c.lineWidth = 2; c.beginPath(); c.ellipse(x, y, r * 1.6, r * 0.35, -0.4, 0, TAU); c.stroke(); });
      const sx = W * 0.5, sy = H * 0.18, sr = Math.min(W, H) * (0.05 + S.frame.bass * 0.04 + S.beatPulse * 0.02); const g = c.createRadialGradient(sx, sy, 0, sx, sy, sr * 5); g.addColorStop(0, U.rgba(o.pal.accent, 0.95)); g.addColorStop(0.2, U.rgba(o.pal.glow, 0.5)); g.addColorStop(1, 'transparent'); c.fillStyle = g; c.fillRect(sx - sr * 5, sy - sr * 5, sr * 10, sr * 10);
    } };
  });

  R.register('Aurora', 'Environment', 'Curtains of northern-lights ribbons that ripple with the spectrum over a dark horizon.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.2); c.globalCompositeOperation = 'lighter';
      for (let l = 0; l < 4; l++) {
        const col = U.mix(o.pal.primary, o.pal.secondary, l / 3), cols = 70;
        for (let i = 0; i < cols; i++) { const u = i / cols, x = u * W, v = S.frame.spec[Math.floor(u * 63)], top = H * (0.15 + l * 0.06) + Math.sin(u * 5 + S.t * 0.5 * o.adj.motion + l) * H * 0.06, h = H * (0.15 + v * 0.35 * o.intensity + S.frame.bass * 0.05);
          const g = c.createLinearGradient(0, top, 0, top + h); g.addColorStop(0, 'transparent'); g.addColorStop(0.3, U.rgba(col, 0.35)); g.addColorStop(1, 'transparent'); c.fillStyle = g; c.fillRect(x, top, W / cols + 1, h); }
      }
      c.globalCompositeOperation = 'source-over'; c.fillStyle = '#04060a'; c.beginPath(); c.moveTo(0, H); for (let x = 0; x <= W; x += 20) c.lineTo(x, H * 0.9 - Math.abs(Math.sin(x * 0.01) * H * 0.05 + Math.sin(x * 0.027) * H * 0.04)); c.lineTo(W, H); c.fill();
    } }));

  R.register('Cosmic Nebula', 'Environment', 'Billowing nebula clouds with hot stars; bass makes the gas surge.', () => {
    let cv = null, cx = null, w = 0, h = 0, img = null;
    return { draw(c, W, H, S, o) {
      const nw = Math.round(80 * Math.max(0.5, o.q)), nh = Math.round(45 * Math.max(0.5, o.q)); if (!cv || nw !== w) { cv = document.createElement('canvas'); w = cv.width = nw; h = cv.height = nh; cx = cv.getContext('2d'); img = cx.createImageData(w, h); }
      const d = img.data, p1 = U.hexToRgb(o.pal.primary), p2 = U.hexToRgb(o.pal.secondary), p3 = U.hexToRgb(o.pal.accent), bg = U.hexToRgb(o.pal.background), t = S.t * 0.15 * o.adj.motion, sc = 3 + S.frame.bass * 1.5;
      for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
        const u = x / w * sc, v = y / h * sc, n = 0.5 * U.smoothNoise(u + t, v) + 0.3 * U.smoothNoise(u * 2 - t, v * 2 + t) + 0.2 * U.smoothNoise(u * 4, v * 4 - t), k = Math.max(0, n - 0.35) * 2.2 * (0.6 + S.frame.energy * o.intensity), i = (y * w + x) * 4, m = U.smoothNoise(u * 1.7, v * 1.7 + 5);
        d[i] = bg[0] + (p1[0] * (1 - m) + p2[0] * m - bg[0]) * k + p3[0] * k * k * 0.3; d[i + 1] = bg[1] + (p1[1] * (1 - m) + p2[1] * m - bg[1]) * k + p3[1] * k * k * 0.3; d[i + 2] = bg[2] + (p1[2] * (1 - m) + p2[2] * m - bg[2]) * k + p3[2] * k * k * 0.3; d[i + 3] = 255;
      }
      cx.putImageData(img, 0, 0); c.imageSmoothingEnabled = true; c.drawImage(cv, 0, 0, W, H);
      c.fillStyle = o.pal.particle; for (let i = 0; i < 60; i++) { const sx = U.noise(i, 1) * W, sy = U.noise(i, 2) * H, tw = 0.4 + 0.6 * Math.abs(Math.sin(S.t * 2 + i)); c.globalAlpha = tw * (0.4 + S.frame.treble); c.fillRect(sx, sy, 2 * (W / 1200), 2 * (W / 1200)); } c.globalAlpha = 1;
    } };
  });

  R.register('Digital Rain', 'Environment', 'Matrix-style falling glyph columns; each column listens to one frequency band.', () => {
    let cols = [], cw = 0; const chars = 'アイウエオカキクケコサシスセソタチツテトナニヌネノ0123456789ABCDEF<>/\\+=*';
    return { draw(c, W, H, S, o) {
      const size = Math.max(10, Math.round(W / (o.q < 0.7 ? 60 : 90))); const n = Math.floor(W / size);
      if (cols.length !== n || cw !== size) { cols = Array.from({ length: n }, () => ({ y: Math.random() * -50, s: 0.5 + Math.random() })); cw = size; }
      U.fade(c, W, H, o.pal.background, 0.14); c.font = `${size}px monospace`;
      for (let i = 0; i < n; i++) { const col = cols[i], v = S.frame.spec[Math.floor(i * 64 / n)]; col.y += (6 + v * 40 * o.intensity + S.beatPulse * 8) * col.s * S.dt * o.adj.motion * 3;
        if (col.y * size > H + 200 && Math.random() > 0.97 - v * 0.2) { col.y = Math.random() * -10; col.s = 0.5 + Math.random(); }
        const ch = chars[(Math.random() * chars.length) | 0]; c.fillStyle = o.pal.particle; c.fillText(ch, i * size, col.y * size); c.fillStyle = U.rgba(o.pal.primary, 0.8); c.fillText(chars[(Math.random() * chars.length) | 0], i * size, (col.y - 1) * size); }
    } };
  });

  R.register('Abstract Landscapes', 'Environment', 'Layered procedural hills at dusk; each ridge breathes with its own frequency band.', () => ({
    draw(c, W, H, S, o) {
      const g = c.createLinearGradient(0, 0, 0, H); g.addColorStop(0, o.pal.background); g.addColorStop(0.6, U.mix(o.pal.secondary, o.pal.background, 0.45)); g.addColorStop(1, U.mix(o.pal.accent, o.pal.background, 0.7)); c.fillStyle = g; c.fillRect(0, 0, W, H);
      c.fillStyle = U.rgba(o.pal.accent, 0.8); c.beginPath(); c.arc(W * 0.3, H * 0.36, Math.min(W, H) * (0.08 + S.frame.bass * 0.02), 0, TAU); c.fill();
      for (let l = 0; l < 6; l++) { const base = H * (0.45 + l * 0.1), amp = H * (0.05 + S.frame.spec[l * 10] * 0.12 * o.intensity), ph = S.t * 0.05 * (l + 1) * o.adj.motion + l * 10;
        c.beginPath(); c.moveTo(0, H); for (let x = 0; x <= W; x += 10) { const u = x / W; c.lineTo(x, base - (U.smoothNoise(u * (3 + l) + ph, l) - 0.5) * amp * 3); } c.lineTo(W, H); c.closePath();
        c.fillStyle = U.mix(o.pal.secondary, '#000000', 0.25 + l * 0.12); c.globalAlpha = 0.95; c.fill(); c.strokeStyle = U.rgba(o.pal.primary, 0.35); c.lineWidth = 2; c.stroke(); }
      c.globalAlpha = 1;
    } }));

  // ---- extras ----
  R.register('Beat Pulse', 'Abstract', 'Soft, flicker-safe colour wash that breathes with the beat (max ~4 pulses per second).', () => {
    let lastPulse = 0, level = 0;
    return { draw(c, W, H, S, o) {
      if (S.frame.beat && S.t - lastPulse > 0.25) { lastPulse = S.t; level = Math.min(1, 0.5 + S.frame.beatI * 0.5); } level *= Math.pow(0.02, S.dt);
      U.fade(c, W, H, o.pal.background, 1); const g = c.createRadialGradient(W / 2, H / 2, 0, W / 2, H / 2, Math.max(W, H) * 0.7); g.addColorStop(0, U.rgba(o.pal.primary, 0.15 + level * 0.5 * o.intensity)); g.addColorStop(0.6, U.rgba(o.pal.secondary, 0.08 + level * 0.25)); g.addColorStop(1, 'transparent'); c.fillStyle = g; c.fillRect(0, 0, W, H);
      const m = Math.min(W, H); c.strokeStyle = U.rgba(o.pal.glow, 0.5 + level * 0.5); c.lineWidth = 3; c.beginPath(); c.arc(W / 2, H / 2, m * (0.15 + level * 0.08), 0, TAU); c.stroke();
    } };
  });

  R.register('Bass Ripples', 'Particles', 'Concentric ripples on a dark pool: bass drops create big, slow rings.', () => {
    const rp = [];
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.15); if (S.frame.beat) rp.push({ x: W * (0.3 + Math.random() * 0.4), y: H * (0.3 + Math.random() * 0.4), r: 0, a: 1, s: 0.15 + S.frame.bass * 0.3 });
      for (let i = rp.length - 1; i >= 0; i--) { const p = rp[i]; p.r += S.dt * Math.min(W, H) * p.s * 2 * o.adj.motion; p.a -= S.dt * 0.35; if (p.a <= 0) { rp.splice(i, 1); continue; } for (let k = 0; k < 3; k++) { c.strokeStyle = U.mix(o.pal.primary, o.pal.secondary, k / 2); c.globalAlpha = p.a * (1 - k * 0.3); c.lineWidth = Math.max(1.5, W / 500) * (3 - k); c.beginPath(); c.ellipse(p.x, p.y, p.r - k * 12, (p.r - k * 12) * 0.45, 0, 0, TAU); c.stroke(); } }
      c.globalAlpha = 1;
    } };
  });

  R.register('Pixel Mosaic', 'Abstract', 'A grid of tiles; each tile lights by a frequency band, with beats flipping tile colours.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 1); const cols = 24, rows = 14, cw = W / cols, rh = H / rows;
      for (let y = 0; y < rows; y++) for (let x = 0; x < cols; x++) { const v = S.frame.spec[(x * 2 + y * 3) % 64] * o.intensity, k = (x + y + Math.floor(S.t * 0.7 + S.beatPulse * 2)) % 3; c.fillStyle = [o.pal.primary, o.pal.secondary, o.pal.accent][k]; c.globalAlpha = 0.08 + v * 0.9; const s = 0.5 + v * 0.5; c.fillRect(x * cw + cw * (1 - s) / 2, y * rh + rh * (1 - s) / 2, cw * s - 2, rh * s - 2); }
      c.globalAlpha = 1;
    } }));

  R.register('Sound Rose', 'Abstract', 'A polar rose curve whose petal count and reach follow the music.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.12); const cx = W / 2, cy = H / 2, m = Math.min(W, H) * 0.42, k = 2 + Math.floor(S.frame.mid * 5) + Math.floor(S.t * 0.1) % 3;
      c.beginPath(); for (let i = 0; i <= 600; i++) { const a = i / 600 * TAU * 4, r = m * (0.35 + 0.65 * Math.abs(Math.cos(k * a / 2 + S.t * 0.3 * o.adj.rotation))) * (0.7 + S.frame.bass * 0.5 * o.intensity); i ? c.lineTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r) : c.moveTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r); }
      c.strokeStyle = U.mix(o.pal.primary, o.pal.accent, S.frame.treble); c.lineWidth = Math.max(1.5, W / 500); c.shadowColor = o.pal.glow; c.shadowBlur = 14 * o.adj.glow * o.q; c.stroke(); c.shadowBlur = 0;
    } }));
})();
