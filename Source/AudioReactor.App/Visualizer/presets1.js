// Classic visualizers. Every preset reads the live analysis frame (S.frame) — no canned animation.
(() => {
  const R = Reactor, U = R.util;

  R.register('Spectrum Bars', 'Classic', 'Log-spaced frequency bars with peak caps.', () => {
    const peaks = new Array(64).fill(0);
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.35);
      const sp = S.frame.spec, n = sp.length, bw = W / n;
      for (let i = 0; i < n; i++) {
        const v = Math.min(1, sp[i] * o.intensity), h = v * H * 0.85;
        peaks[i] = Math.max(h, peaks[i] - H * 0.004);
        const g = c.createLinearGradient(0, H, 0, H - h);
        g.addColorStop(0, o.pal.primary); g.addColorStop(1, o.pal.secondary);
        c.fillStyle = g; c.shadowBlur = 12 * o.adj.glow * o.q; c.shadowColor = o.pal.glow;
        c.fillRect(i * bw + bw * 0.12, H - h, bw * 0.76, h);
        c.fillStyle = o.pal.accent; c.fillRect(i * bw + bw * 0.12, H - peaks[i] - 4, bw * 0.76, 3);
      }
      c.shadowBlur = 0;
    } };
  });

  R.register('Circular Spectrum', 'Classic', 'Radial spectrum that pulses with the bass.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.3);
      const cx = W / 2, cy = H / 2, base = Math.min(W, H) * (0.18 + S.frame.bass * 0.06 + S.beatPulse * 0.03), n = S.frame.spec.length;
      c.lineWidth = Math.max(2, W / 400); c.lineCap = 'round';
      for (let i = 0; i < n * 2; i++) {
        const k = i < n ? i : 2 * n - 1 - i, a = (i / (n * 2)) * Math.PI * 2 + S.t * 0.15 * o.adj.rotation;
        const len = S.frame.spec[k] * Math.min(W, H) * 0.3 * o.intensity;
        c.strokeStyle = U.mix(o.pal.primary, o.pal.secondary, k / n);
        c.beginPath(); c.moveTo(cx + Math.cos(a) * base, cy + Math.sin(a) * base);
        c.lineTo(cx + Math.cos(a) * (base + len + 2), cy + Math.sin(a) * (base + len + 2)); c.stroke();
      }
      c.strokeStyle = U.rgba(o.pal.glow, 0.6); c.lineWidth = 2; c.beginPath(); c.arc(cx, cy, base * 0.95, 0, 7); c.stroke();
    } }));

  R.register('Mirrored Spectrum', 'Classic', 'Symmetric spectrum mirrored around the horizon.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.4);
      const sp = S.frame.spec, n = sp.length, bw = W / (n * 2);
      for (let i = 0; i < n; i++) {
        const h = sp[i] * H * 0.45 * o.intensity, col = U.mix(o.pal.primary, o.pal.accent, i / n);
        c.fillStyle = col; c.shadowColor = col; c.shadowBlur = 10 * o.adj.glow * o.q;
        for (const x of [W / 2 + i * bw, W / 2 - (i + 1) * bw]) { c.fillRect(x + 1, H / 2 - h, bw - 2, h * 2 + 2); }
      }
      c.shadowBlur = 0;
    } }));

  R.register('Oscilloscope', 'Classic', 'Phosphor-style oscilloscope trace of the live waveform.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.22);
      const w = S.frame.wave, n = w.length;
      c.strokeStyle = o.pal.primary; c.lineWidth = Math.max(2, W / 500); c.shadowColor = o.pal.glow; c.shadowBlur = 16 * o.adj.glow * o.q;
      c.beginPath();
      for (let i = 0; i < n; i++) { const x = i / (n - 1) * W, y = H / 2 + w[i] * H * 0.4 * o.intensity * 2; i ? c.lineTo(x, y) : c.moveTo(x, y); }
      c.stroke(); c.shadowBlur = 0;
      c.strokeStyle = 'rgba(255,255,255,0.06)'; c.lineWidth = 1;
      for (let i = 1; i < 10; i++) { c.beginPath(); c.moveTo(0, H * i / 10); c.lineTo(W, H * i / 10); c.stroke(); c.beginPath(); c.moveTo(W * i / 10, 0); c.lineTo(W * i / 10, H); c.stroke(); }
    } }));

  R.register('Waveform', 'Classic', 'Filled mirrored waveform that glows on beats.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.3);
      const w = S.frame.wave, n = w.length, amp = H * 0.35 * o.intensity * 2;
      const g = c.createLinearGradient(0, 0, W, 0); g.addColorStop(0, o.pal.primary); g.addColorStop(0.5, o.pal.accent); g.addColorStop(1, o.pal.secondary);
      c.fillStyle = g; c.globalAlpha = 0.85 + S.beatPulse * 0.15; c.shadowBlur = (10 + S.beatPulse * 30) * o.adj.glow * o.q; c.shadowColor = o.pal.glow;
      c.beginPath(); c.moveTo(0, H / 2);
      for (let i = 0; i < n; i++) c.lineTo(i / (n - 1) * W, H / 2 - Math.abs(w[i]) * amp);
      for (let i = n - 1; i >= 0; i--) c.lineTo(i / (n - 1) * W, H / 2 + Math.abs(w[i]) * amp);
      c.closePath(); c.fill(); c.globalAlpha = 1; c.shadowBlur = 0;
    } }));

  R.register('Stereo Waveform', 'Classic', 'Left and right channels drawn as separate traces.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.25);
      const draw = (arr, y0, col) => {
        c.strokeStyle = col; c.shadowColor = col; c.shadowBlur = 12 * o.adj.glow * o.q; c.lineWidth = Math.max(2, W / 500); c.beginPath();
        arr.forEach((v, i) => { const x = i / (arr.length - 1) * W, y = y0 + v * H * 0.22 * o.intensity * 2; i ? c.lineTo(x, y) : c.moveTo(x, y); }); c.stroke();
      };
      draw(S.frame.waveL, H * 0.3, o.pal.primary); draw(S.frame.waveR, H * 0.7, o.pal.accent); c.shadowBlur = 0;
    } }));

  R.register('Frequency Waterfall', 'Classic', 'Scrolling spectrogram: time flows downward, colour is energy.', () => {
    let off = null, octx = null, ow = 0, oh = 0;
    return { draw(c, W, H, S, o) {
      const cols = 64, rows = Math.max(60, Math.floor(H / (o.q < 0.7 ? 8 : 4)));
      if (!off || ow !== cols || oh !== rows) { off = document.createElement('canvas'); off.width = ow = cols; off.height = oh = rows; octx = off.getContext('2d'); octx.fillStyle = o.pal.background; octx.fillRect(0, 0, ow, oh); }
      octx.drawImage(off, 0, 0, cols, rows - 1, 0, 1, cols, rows - 1);
      const sp = S.frame.spec;
      for (let i = 0; i < cols; i++) { const v = Math.min(1, sp[i] * o.intensity); octx.fillStyle = U.hsl(260 - v * 200 + (S.t * 10), 90, 8 + v * 55); octx.fillRect(i, 0, 1, 1); }
      c.imageSmoothingEnabled = true; c.drawImage(off, 0, 0, cols, rows, 0, 0, W, H);
    } };
  });

  R.register('Particle Galaxy', '3D', 'Spiral galaxy of stars; arms spin faster and flare with the music.', () => {
    const N = 900, st = Array.from({ length: N }, (_, i) => ({ r: Math.random(), a: Math.random() * 6.283, z: Math.random(), arm: i % 3 }));
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.2);
      const cx = W / 2, cy = H / 2, sc = Math.min(W, H) * 0.45 * (1 + S.frame.bass * 0.15 * o.adj.zoom), n = Math.floor(N * o.q * o.adj.particles);
      c.globalCompositeOperation = 'lighter';
      for (let i = 0; i < Math.min(N, n); i++) {
        const s = st[i], spin = S.t * (0.15 + S.frame.mid * 0.5) * o.adj.rotation;
        const ang = s.a + s.r * 6 + s.arm * 2.094 + spin * (1.2 - s.r);
        const rr = s.r * sc, x = cx + Math.cos(ang) * rr, y = cy + Math.sin(ang) * rr * 0.55 + (s.z - 0.5) * 30;
        c.fillStyle = U.mix(o.pal.primary, o.pal.secondary, s.r); c.globalAlpha = 0.35 + S.frame.treble * 0.65;
        const sz = (1.2 + s.z * 2 + S.frame.energy * 2 + S.beatPulse * 2) * (W / 1200);
        c.fillRect(x, y, sz, sz);
      }
      c.globalAlpha = 1; c.globalCompositeOperation = 'source-over';
      const g = c.createRadialGradient(cx, cy, 0, cx, cy, sc * 0.35); g.addColorStop(0, U.rgba(o.pal.glow, 0.35 + S.beatPulse * 0.4)); g.addColorStop(1, 'transparent');
      c.fillStyle = g; c.fillRect(0, 0, W, H);
    } };
  });

  R.register('Neon Tunnel', '3D', 'Infinite square tunnel; speed follows energy, walls flash on beats.', () => {
    let z = 0;
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.35);
      z += (0.4 + S.frame.energy * 2.5 + S.beatPulse * 1.5) * S.dt * o.adj.motion;
      const cx = W / 2 + Math.sin(S.t * 0.7) * W * 0.05 * o.adj.camera, cy = H / 2 + Math.cos(S.t * 0.5) * H * 0.05 * o.adj.camera, rings = Math.floor(22 * o.q) + 6;
      c.lineWidth = Math.max(1.5, W / 600); c.shadowBlur = 10 * o.adj.glow * o.q;
      for (let i = rings; i >= 0; i--) {
        const d = (i - (z % 1)) / rings, s = Math.pow(1 - d, 2.2) * Math.max(W, H) * 0.9; if (s <= 1) continue;
        const col = U.mix(o.pal.primary, o.pal.accent, (i + Math.floor(z)) % 7 / 7); c.strokeStyle = col; c.shadowColor = col;
        c.globalAlpha = Math.min(1, (1 - d) * 1.3 + S.beatPulse * 0.3);
        const rot = S.t * 0.2 * o.adj.rotation + i * 0.02; c.save(); c.translate(cx, cy); c.rotate(rot); c.strokeRect(-s / 2, -s / 2, s, s); c.restore();
      }
      c.globalAlpha = 1; c.shadowBlur = 0;
    } };
  });

  R.register('Rotating Frequency Rings', '3D', 'Concentric rings, one per band group, tilting in 3D.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.25);
      const bands = [S.frame.bass, S.frame.lowMid, S.frame.mid, S.frame.highMid, S.frame.treble], cx = W / 2, cy = H / 2, m = Math.min(W, H);
      c.lineWidth = Math.max(2, W / 300);
      bands.forEach((b, k) => {
        const r = m * (0.1 + k * 0.075) * (1 + b * 0.35 * o.intensity), tilt = 0.35 + Math.sin(S.t * 0.4 + k) * 0.25, rot = S.t * (0.3 + k * 0.15) * o.adj.rotation * (k % 2 ? -1 : 1);
        const col = U.mix(o.pal.primary, o.pal.accent, k / 4); c.strokeStyle = col; c.shadowColor = col; c.shadowBlur = (8 + b * 25) * o.adj.glow * o.q;
        c.beginPath();
        for (let i = 0; i <= 90; i++) { const a = i / 90 * 6.283, rr = r * (1 + 0.06 * Math.sin(a * (k + 3) + rot * 3) * b); const x = Math.cos(a + rot) * rr, y = Math.sin(a + rot) * rr * tilt; i ? c.lineTo(cx + x, cy + y) : c.moveTo(cx + x, cy + y); }
        c.stroke();
      });
      c.shadowBlur = 0;
    } }));

  R.register('Neon Grid Landscape', '3D', 'Synthwave grid with mountains built from the spectrum.', () => {
    let z = 0;
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 1);
      const hor = H * 0.5; z += (0.3 + S.frame.energy * 1.5) * S.dt * o.adj.motion;
      const g = c.createLinearGradient(0, 0, 0, hor); g.addColorStop(0, o.pal.background); g.addColorStop(1, U.rgba(o.pal.secondary, 0.6)); c.fillStyle = g; c.fillRect(0, 0, W, hor);
      const sunR = Math.min(W, H) * (0.14 + S.frame.bass * 0.04); const sg = c.createLinearGradient(0, hor - sunR * 2, 0, hor); sg.addColorStop(0, o.pal.accent); sg.addColorStop(1, o.pal.primary);
      c.fillStyle = sg; c.beginPath(); c.arc(W / 2, hor - sunR * 0.6, sunR, 0, 7); c.fill();
      c.strokeStyle = o.pal.primary; c.shadowColor = o.pal.glow; c.shadowBlur = 8 * o.adj.glow * o.q; c.lineWidth = Math.max(1.2, W / 800);
      const rows = 18;
      for (let i = 0; i < rows; i++) {
        const d = (i + (1 - (z % 1))) / rows, y = hor + Math.pow(d, 2.2) * (H - hor); c.globalAlpha = 0.25 + d * 0.75;
        c.beginPath();
        const n = S.frame.spec.length;
        for (let k = 0; k <= 64; k++) {
          const xn = k / 64, side = Math.abs(xn - 0.5) * 2; const amp = S.frame.spec[Math.min(n - 1, Math.floor(side * (n - 1)))] * (1 - d) * H * 0.18 * o.intensity * o.adj.depth;
          const x = W / 2 + (xn - 0.5) * W * (0.2 + d * 2.2); const yy = y - amp * (side > 0.12 ? 1 : 0.2);
          k ? c.lineTo(x, yy) : c.moveTo(x, yy);
        }
        c.stroke();
      }
      c.globalAlpha = 0.4;
      for (let k = -10; k <= 10; k++) { c.beginPath(); c.moveTo(W / 2 + k * W * 0.02, hor); c.lineTo(W / 2 + k * W * 0.2, H); c.stroke(); }
      c.globalAlpha = 1; c.shadowBlur = 0;
    } };
  });

  R.register('Starfield', 'Particles', 'Warp-speed starfield; stars streak with the bass.', () => {
    const stars = Array.from({ length: 700 }, () => ({ x: Math.random() * 2 - 1, y: Math.random() * 2 - 1, z: Math.random() }));
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.25 + 0.2 * (1 - S.frame.bass));
      const sp = (0.15 + S.frame.bass * 1.6 + S.beatPulse * 1.2) * S.dt * o.adj.particleSpeed * o.adj.motion, cx = W / 2, cy = H / 2, n = Math.floor(stars.length * o.q * o.adj.particles);
      c.strokeStyle = o.pal.particle; c.lineCap = 'round';
      for (let i = 0; i < Math.min(stars.length, n); i++) {
        const s = stars[i], pz = s.z; s.z -= sp; if (s.z <= 0.01) { s.x = Math.random() * 2 - 1; s.y = Math.random() * 2 - 1; s.z = 1; continue; }
        const x = cx + s.x / s.z * W * 0.25, y = cy + s.y / s.z * H * 0.25, px = cx + s.x / pz * W * 0.25, py = cy + s.y / pz * H * 0.25;
        c.globalAlpha = Math.min(1, (1 - s.z) * 1.4); c.lineWidth = (1 - s.z) * 3 * (W / 1200) + 0.5;
        c.strokeStyle = i % 5 === 0 ? o.pal.accent : o.pal.particle; c.beginPath(); c.moveTo(px, py); c.lineTo(x, y); c.stroke();
      }
      c.globalAlpha = 1;
    } };
  });
})();
