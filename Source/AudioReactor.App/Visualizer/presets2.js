// Particle effects + extra classic visualizers.
(() => {
  const R = Reactor, U = R.util, TAU = Math.PI * 2;

  R.register('Exploding Particles', 'Particles', 'Every detected beat detonates a burst of particles from the centre.', () => {
    const ps = [];
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.22);
      const cx = W / 2, cy = H / 2, m = Math.min(W, H);
      if (S.frame.beat) {
        const n = Math.floor((50 + S.frame.beatI * 120) * o.q * o.adj.particles), hue = S.t * 40;
        for (let i = 0; i < n; i++) { const a = Math.random() * TAU, v = (0.2 + Math.random() * 0.9) * m * 0.9 * o.adj.particleSpeed; ps.push({ x: cx, y: cy, vx: Math.cos(a) * v, vy: Math.sin(a) * v, life: 1, h: hue + Math.random() * 60 }); }
      }
      c.globalCompositeOperation = 'lighter';
      for (let i = ps.length - 1; i >= 0; i--) {
        const p = ps[i]; p.x += p.vx * S.dt; p.y += p.vy * S.dt; p.vx *= 0.985; p.vy *= 0.985; p.life -= S.dt * 0.6;
        if (p.life <= 0) { ps.splice(i, 1); continue; }
        c.fillStyle = i % 3 ? U.mix(o.pal.primary, o.pal.accent, p.life) : o.pal.particle; c.globalAlpha = p.life;
        const s = (2 + p.life * 4) * (W / 1200); c.fillRect(p.x, p.y, s, s);
      }
      if (ps.length > 2500) ps.splice(0, ps.length - 2500);
      c.globalAlpha = 1; c.globalCompositeOperation = 'source-over';
      const r = m * (0.04 + S.frame.bass * 0.05); const g = c.createRadialGradient(cx, cy, 0, cx, cy, r * 3); g.addColorStop(0, U.rgba(o.pal.glow, 0.6)); g.addColorStop(1, 'transparent'); c.fillStyle = g; c.fillRect(cx - r * 3, cy - r * 3, r * 6, r * 6);
    } };
  });

  R.register('Particle Rain', 'Particles', 'Streaks of light fall faster and denser with the volume; treble sparks the splashes.', () => {
    const drops = Array.from({ length: 400 }, () => ({ x: Math.random(), y: Math.random(), z: 0.3 + Math.random() * 0.7 }));
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.3);
      const n = Math.floor(drops.length * o.q * o.adj.particles * (0.4 + S.frame.energy * 0.9)), sp = (0.3 + S.frame.volume * 1.6) * o.adj.particleSpeed;
      c.lineCap = 'round';
      for (let i = 0; i < Math.min(n, drops.length); i++) {
        const d = drops[i]; d.y += sp * d.z * S.dt * 1.2;
        if (d.y > 1) { d.y = -0.05; d.x = Math.random(); if (S.frame.treble > 0.35) { c.fillStyle = o.pal.accent; c.fillRect(d.x * W, H - 3, 3 + S.frame.treble * 8, 2); } }
        const x = d.x * W, y = d.y * H, len = (10 + S.frame.energy * 50) * d.z * (H / 800);
        c.strokeStyle = i % 4 ? U.rgba(o.pal.primary, d.z) : U.rgba(o.pal.secondary, d.z); c.lineWidth = d.z * 2.4 * (W / 1200) + 0.4;
        c.beginPath(); c.moveTo(x, y - len); c.lineTo(x, y); c.stroke();
      }
    } };
  });

  R.register('Energy Waves', 'Particles', 'Shockwave rings are emitted by the beat and travel outward.', () => {
    const waves = [];
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.2);
      if (S.frame.beat) waves.push({ r: 0, a: 1, w: 2 + S.frame.beatI * 8, col: Math.random() });
      const cx = W / 2, cy = H / 2, m = Math.max(W, H) * 0.7;
      for (let i = waves.length - 1; i >= 0; i--) {
        const w = waves[i]; w.r += S.dt * m * 0.45 * o.adj.motion; w.a -= S.dt * 0.4; if (w.a <= 0) { waves.splice(i, 1); continue; }
        c.strokeStyle = U.mix(o.pal.primary, o.pal.accent, w.col); c.globalAlpha = w.a; c.lineWidth = w.w * w.a * (W / 1200) + 1; c.shadowColor = o.pal.glow; c.shadowBlur = 14 * o.adj.glow * o.q;
        c.beginPath();
        for (let k = 0; k <= 72; k++) { const a = k / 72 * TAU, rr = w.r * (1 + 0.04 * S.frame.spec[k % 64] * o.intensity); k ? c.lineTo(cx + Math.cos(a) * rr, cy + Math.sin(a) * rr) : c.moveTo(cx + Math.cos(a) * rr, cy + Math.sin(a) * rr); }
        c.stroke();
      }
      c.globalAlpha = 1; c.shadowBlur = 0;
      c.fillStyle = o.pal.glow; c.beginPath(); c.arc(cx, cy, Math.min(W, H) * (0.015 + S.frame.bass * 0.03), 0, TAU); c.fill();
    } };
  });

  R.register('Particle Vortex', 'Particles', 'Particles spiral into a vortex whose spin follows the mids.', () => {
    const ps = Array.from({ length: 1200 }, () => ({ a: Math.random() * TAU, r: Math.random(), s: 0.4 + Math.random() }));
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.18);
      const cx = W / 2, cy = H / 2, m = Math.min(W, H) * 0.48, n = Math.floor(ps.length * o.q * o.adj.particles);
      c.globalCompositeOperation = 'lighter';
      for (let i = 0; i < Math.min(n, ps.length); i++) {
        const p = ps[i]; p.a += (0.4 + S.frame.mid * 3) * p.s * S.dt * o.adj.rotation / (0.3 + p.r); p.r -= S.dt * (0.05 + S.frame.bass * 0.25) * p.s; if (p.r < 0.02) { p.r = 1; p.a = Math.random() * TAU; }
        const rr = p.r * m, x = cx + Math.cos(p.a) * rr, y = cy + Math.sin(p.a) * rr * 0.8, s = (1 + (1 - p.r) * 3 + S.beatPulse * 2) * (W / 1200);
        c.fillStyle = U.mix(o.pal.secondary, o.pal.primary, 1 - p.r); c.fillRect(x, y, s, s);
      }
      c.globalCompositeOperation = 'source-over';
    } };
  });

  R.register('Fireflies', 'Particles', 'Drifting glowing fireflies that brighten and swarm with the treble.', () => {
    const f = Array.from({ length: 90 }, () => ({ x: Math.random(), y: Math.random(), p: Math.random() * TAU, s: 0.05 + Math.random() * 0.1 }));
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.25);
      const n = Math.floor(f.length * o.q * o.adj.particles); c.globalCompositeOperation = 'lighter';
      for (let i = 0; i < Math.min(n, f.length); i++) {
        const p = f[i]; p.p += S.dt * (1 + S.frame.treble * 4);
        p.x += Math.cos(p.p * 0.7 + i) * p.s * S.dt * (1 + S.frame.mid * 3); p.y += Math.sin(p.p * 0.5 + i * 2) * p.s * S.dt * (1 + S.frame.mid * 3);
        p.x = (p.x + 1) % 1; p.y = (p.y + 1) % 1;
        const glow = 0.25 + 0.75 * (0.5 + 0.5 * Math.sin(p.p * 2)) * (0.5 + S.frame.treble), r = (8 + S.frame.energy * 14 + S.beatPulse * 10) * (W / 1200);
        const g = c.createRadialGradient(p.x * W, p.y * H, 0, p.x * W, p.y * H, r); g.addColorStop(0, U.rgba(i % 2 ? o.pal.particle : o.pal.accent, glow)); g.addColorStop(1, 'transparent');
        c.fillStyle = g; c.fillRect(p.x * W - r, p.y * H - r, r * 2, r * 2);
      }
      c.globalCompositeOperation = 'source-over';
    } };
  });

  R.register('Magnetic Particles', 'Particles', 'Iron-filing-like particles are pulled between two poles that swing with the bass.', () => {
    const ps = Array.from({ length: 700 }, () => ({ x: Math.random(), y: Math.random(), vx: 0, vy: 0 }));
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.22);
      const a = S.t * 0.6 * o.adj.rotation, d = 0.22 + S.frame.bass * 0.2;
      const poles = [{ x: 0.5 + Math.cos(a) * d, y: 0.5 + Math.sin(a) * d * 0.8, s: 1 }, { x: 0.5 - Math.cos(a) * d, y: 0.5 - Math.sin(a) * d * 0.8, s: -1 }];
      const n = Math.floor(ps.length * o.q * o.adj.particles); c.lineCap = 'round'; c.lineWidth = Math.max(1, W / 700);
      for (let i = 0; i < Math.min(n, ps.length); i++) {
        const p = ps[i]; let fx = 0, fy = 0;
        for (const q of poles) { const dx = q.x - p.x, dy = q.y - p.y, r2 = dx * dx + dy * dy + 0.004; const f = q.s * (0.002 + S.frame.mid * 0.004) / r2; fx += dx * f; fy += dy * f; }
        p.vx = (p.vx + fx) * 0.9; p.vy = (p.vy + fy) * 0.9; p.x += p.vx; p.y += p.vy;
        if (p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1 || Math.hypot(p.vx, p.vy) > 0.2) { p.x = Math.random(); p.y = Math.random(); p.vx = p.vy = 0; }
        const sp = Math.min(1, Math.hypot(p.vx, p.vy) * 90); c.strokeStyle = U.mix(o.pal.secondary, o.pal.primary, sp); c.globalAlpha = 0.3 + sp * 0.7;
        c.beginPath(); c.moveTo(p.x * W, p.y * H); c.lineTo((p.x - p.vx * 6) * W, (p.y - p.vy * 6) * H); c.stroke();
      }
      c.globalAlpha = 1;
      for (const q of poles) { c.fillStyle = q.s > 0 ? o.pal.accent : o.pal.primary; c.shadowColor = c.fillStyle; c.shadowBlur = 20 * o.adj.glow; c.beginPath(); c.arc(q.x * W, q.y * H, 6 + S.beatPulse * 10, 0, TAU); c.fill(); }
      c.shadowBlur = 0;
    } };
  });

  R.register('Reactive Dust', 'Particles', 'Fine dust motes drift in turbulent noise currents; loud passages whip them up.', () => {
    const d = Array.from({ length: 1000 }, () => ({ x: Math.random(), y: Math.random(), z: Math.random() }));
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.15);
      const n = Math.floor(d.length * o.q * o.adj.particles), turb = 0.02 + S.frame.energy * 0.25;
      for (let i = 0; i < Math.min(n, d.length); i++) {
        const p = d[i], a = U.smoothNoise(p.x * 4 + S.t * 0.2, p.y * 4) * TAU * 2;
        p.x += Math.cos(a) * turb * S.dt * (0.5 + p.z) * o.adj.particleSpeed; p.y += Math.sin(a) * turb * S.dt * (0.5 + p.z) * o.adj.particleSpeed - 0.01 * S.dt;
        p.x = (p.x + 1) % 1; p.y = (p.y + 1) % 1;
        c.fillStyle = U.rgba(i % 5 ? o.pal.particle : o.pal.accent, 0.2 + p.z * 0.6 * (0.5 + S.frame.treble)); const s = (0.8 + p.z * 2 + S.beatPulse * 1.5) * (W / 1200); c.fillRect(p.x * W, p.y * H, s, s);
      }
    } };
  });

  // ---- more classics ----
  R.register('LED Matrix', 'Classic', 'Retro LED bar-graph display with falling peak dots.', () => {
    const pk = new Array(32).fill(0);
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 1);
      const cols = 32, rows = 18, cw = W / cols, rh = H / rows;
      for (let i = 0; i < cols; i++) {
        const v = S.frame.spec[Math.floor(i * 64 / cols)] * o.intensity, lit = Math.round(v * rows); pk[i] = Math.max(lit, pk[i] - S.dt * 6);
        for (let r = 0; r < rows; r++) {
          const on = r < lit, hot = r / rows; c.fillStyle = on ? (hot > 0.8 ? o.pal.accent : U.mix(o.pal.primary, o.pal.secondary, hot)) : 'rgba(255,255,255,0.05)';
          if (on) { c.shadowColor = c.fillStyle; c.shadowBlur = 6 * o.adj.glow * o.q; } else c.shadowBlur = 0;
          c.fillRect(i * cw + cw * 0.1, H - (r + 1) * rh + rh * 0.12, cw * 0.8, rh * 0.76);
        }
        c.shadowBlur = 0; c.fillStyle = o.pal.particle; c.fillRect(i * cw + cw * 0.1, H - (Math.floor(pk[i]) + 1) * rh - rh * 0.8, cw * 0.8, rh * 0.3);
      }
    } };
  });

  R.register('Radial Waveform', 'Classic', 'The waveform wrapped into a ring that breathes with the bass.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.22);
      const cx = W / 2, cy = H / 2, base = Math.min(W, H) * (0.22 + S.frame.bass * 0.05), w = S.frame.wave, n = w.length;
      c.lineWidth = Math.max(2, W / 400); c.shadowColor = o.pal.glow; c.shadowBlur = 14 * o.adj.glow * o.q;
      for (let pass = 0; pass < 2; pass++) {
        c.strokeStyle = pass ? o.pal.accent : o.pal.primary; c.beginPath();
        for (let i = 0; i <= n; i++) { const v = w[i % n], a = i / n * TAU + S.t * 0.2 * o.adj.rotation * (pass ? -1 : 1), r = base * (1 + v * (0.9 + pass * 0.4) * o.intensity * 1.5); i ? c.lineTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r) : c.moveTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r); }
        c.stroke();
      }
      c.shadowBlur = 0;
    } }));

  R.register('Spectrum Ridgelines', 'Classic', 'Stacked scrolling spectrum slices, in the style of a pulsar plot.', () => {
    const hist = [];
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 1);
      hist.unshift(Array.from(S.frame.spec)); if (hist.length > 28) hist.pop();
      const top = H * 0.22, bot = H * 0.92, rows = hist.length; c.lineWidth = Math.max(1.2, W / 800);
      for (let r = rows - 1; r >= 0; r--) {
        const y0 = top + (bot - top) * (1 - r / rows), sc = 1 - r / rows * 0.4; c.beginPath();
        hist[r].forEach((v, i) => { const x = W * (0.12 + 0.76 * i / 63), y = y0 - v * H * 0.14 * o.intensity * sc; i ? c.lineTo(x, y) : c.moveTo(x, y); });
        c.lineTo(W * 0.88, y0 + 4); c.lineTo(W * 0.12, y0 + 4); c.closePath(); c.fillStyle = o.pal.background; c.fill();
        c.strokeStyle = U.mix(o.pal.secondary, o.pal.primary, 1 - r / rows); c.globalAlpha = 0.4 + 0.6 * (1 - r / rows); c.stroke();
      }
      c.globalAlpha = 1;
    } };
  });

  R.register('Lissajous Scope', 'Classic', 'Vectorscope: left vs right channel plotted as a phosphor XY trace.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.15);
      const l = S.frame.waveL, r = S.frame.waveR, cx = W / 2, cy = H / 2, sc = Math.min(W, H) * 0.42 * o.intensity * 2;
      c.strokeStyle = o.pal.primary; c.shadowColor = o.pal.glow; c.shadowBlur = 12 * o.adj.glow * o.q; c.lineWidth = Math.max(1.5, W / 600); c.beginPath();
      for (let i = 0; i < l.length; i++) { const x = cx + (l[i] - r[i]) * sc * 0.7071, y = cy - (l[i] + r[i]) * sc * 0.7071; i ? c.lineTo(x, y) : c.moveTo(x, y); }
      c.stroke(); c.shadowBlur = 0; c.strokeStyle = 'rgba(255,255,255,0.08)'; c.beginPath(); c.moveTo(cx, 0); c.lineTo(cx, H); c.moveTo(0, cy); c.lineTo(W, cy); c.stroke();
    } }));

  R.register('VU Meters', 'Classic', 'Analog needle meters for bass, mids and treble with beat-lit lamps.', () => {
    const needle = [0, 0, 0];
    return { draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 1);
      const vals = [S.frame.bass, (S.frame.lowMid + S.frame.mid) / 2, (S.frame.highMid + S.frame.treble) / 2], names = ['BASS', 'MID', 'TREBLE'], m = Math.min(W / 3.2, H * 0.7);
      for (let k = 0; k < 3; k++) {
        needle[k] += (vals[k] * o.intensity - needle[k]) * Math.min(1, S.dt * 12);
        const cx = W * (0.2 + k * 0.3), cy = H * 0.68;
        c.strokeStyle = o.pal.secondary; c.lineWidth = Math.max(2, W / 500); c.beginPath(); c.arc(cx, cy, m * 0.5, Math.PI * 1.15, Math.PI * 1.85); c.stroke();
        for (let t = 0; t <= 10; t++) { const a = Math.PI * (1.15 + 0.7 * t / 10); c.strokeStyle = t > 8 ? o.pal.accent : o.pal.primary; c.beginPath(); c.moveTo(cx + Math.cos(a) * m * 0.46, cy + Math.sin(a) * m * 0.46); c.lineTo(cx + Math.cos(a) * m * 0.54, cy + Math.sin(a) * m * 0.54); c.stroke(); }
        const a = Math.PI * (1.15 + 0.7 * Math.min(1, needle[k])); c.strokeStyle = o.pal.particle; c.shadowColor = o.pal.glow; c.shadowBlur = 10 * o.adj.glow; c.lineWidth = Math.max(2, W / 400);
        c.beginPath(); c.moveTo(cx, cy); c.lineTo(cx + Math.cos(a) * m * 0.5, cy + Math.sin(a) * m * 0.5); c.stroke(); c.shadowBlur = 0;
        c.fillStyle = k === 0 && S.beatPulse > 0.3 ? o.pal.accent : 'rgba(255,255,255,0.1)'; c.beginPath(); c.arc(cx, cy - m * 0.62, 8, 0, TAU); c.fill();
        c.fillStyle = o.pal.particle; c.font = `${Math.round(H / 28)}px Segoe UI`; c.textAlign = 'center'; c.fillText(names[k], cx, cy + m * 0.25);
      }
    } };
  });

  R.register('Heartbeat Rings', 'Classic', 'Nested rings that pulse on every beat and glow with the BPM.', () => ({
    draw(c, W, H, S, o) {
      U.fade(c, W, H, o.pal.background, 0.3);
      const cx = W / 2, cy = H / 2, m = Math.min(W, H), n = Math.floor(8 * Math.max(0.5, o.q));
      for (let i = n; i >= 1; i--) {
        const r = m * 0.06 * i * (1 + S.beatPulse * 0.12 * (n - i + 1) / n + S.frame.bass * 0.08), col = U.mix(o.pal.primary, o.pal.accent, i / n);
        c.strokeStyle = col; c.lineWidth = Math.max(2, W / 350) * (1 + S.beatPulse); c.shadowColor = col; c.shadowBlur = (6 + S.beatPulse * 24) * o.adj.glow * o.q; c.globalAlpha = 0.35 + 0.65 * i / n;
        c.beginPath(); c.arc(cx, cy, r, 0, TAU); c.stroke();
      }
      c.globalAlpha = 1; c.shadowBlur = 0; c.fillStyle = o.pal.particle; c.font = `${Math.round(m / 18)}px Segoe UI`; c.textAlign = 'center';
      if (S.frame.bpm) c.fillText(Math.round(S.frame.bpm) + ' BPM', cx, cy + m * 0.03);
    } }));
})();
