'use strict';

// ── Base path (no ingress prefix on Windows) ─────────────────────
const BASE = '';

// ── Global state ──────────────────────────────────────────────────
const state = {
  ws:             null,
  wsRetries:      0,
  payloads:       [],
  profiles:       [],
  devices:        [],
  sources:        [],
  favorites:      [],       // profile names pinned to Quick Start
  payloadFavs:    [],       // payload names starred
  payloadFilter:  'all',
  payloadSearch:  '',
  selectedPayloads: new Set(),
  execState:      'idle',
  runningProfile: '',
  advancedMode:   false,
  updateResults:  [],
  builderName:    '',
};

// ── Builder (separate from state for clarity) ─────────────────────
const builder = { steps: [] };

// ── Theme ─────────────────────────────────────────────────────────
function initTheme() {
  const saved = sessionStorage.getItem('ps5_theme') || 'dark';
  document.documentElement.setAttribute('data-theme', saved);
  document.getElementById('btn-theme').textContent = saved === 'dark' ? '☀' : '🌙';
}

function toggleTheme() {
  const cur  = document.documentElement.getAttribute('data-theme') || 'dark';
  const next = cur === 'dark' ? 'light' : 'dark';
  document.documentElement.setAttribute('data-theme', next);
  sessionStorage.setItem('ps5_theme', next);
  document.getElementById('btn-theme').textContent = next === 'dark' ? '☀' : '🌙';
}

// ── Advanced mode ─────────────────────────────────────────────────
function applyAdvancedMode() {
  document.body.classList.toggle('advanced-mode', state.advancedMode);
  document.getElementById('advanced-mode-toggle').checked = state.advancedMode;
}

// ── Persistent state save (debounced) ────────────────────────────
let _saveTimer = null;
function scheduleSave() {
  clearTimeout(_saveTimer);
  _saveTimer = setTimeout(_doSave, 800);
}

async function _doSave() {
  try {
    const ip    = document.getElementById('ps5-ip')?.value || '';
    const name  = document.getElementById('builder-profile-name')?.value || '';
    const payload = {
      ps5_ip:        ip,
      favorites:     state.favorites,
      payloadFavs:   state.payloadFavs,
      payloadFilter: state.payloadFilter,
      advancedMode:  state.advancedMode,
      builderName:   name,
      builderSteps:  builder.steps,
    };
    await api(`${BASE}/api/state`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
  } catch (_) { /* non-critical */ }
}

async function loadPersistedState() {
  try {
    const s = await api(`${BASE}/api/state`);
    if (s.ps5_ip) document.getElementById('ps5-ip').value = s.ps5_ip;
    if (Array.isArray(s.favorites))   state.favorites   = s.favorites;
    if (Array.isArray(s.payloadFavs)) state.payloadFavs = s.payloadFavs;
    if (s.payloadFilter) state.payloadFilter = s.payloadFilter;
    if (typeof s.advancedMode === 'boolean') state.advancedMode = s.advancedMode;
    if (s.builderName) document.getElementById('builder-profile-name').value = s.builderName;
    if (Array.isArray(s.builderSteps)) {
      builder.steps = s.builderSteps;
      renderBuilderSteps();
    }
    applyAdvancedMode();
  } catch (_) { /* ok */ }
}

// ── API helper ────────────────────────────────────────────────────
async function api(url, opts = {}) {
  const res = await fetch(BASE + url.replace(BASE, ''), opts);
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  const ct = res.headers.get('content-type') || '';
  if (ct.includes('application/json')) return res.json();
  return res.text();
}

// ── Host helper ───────────────────────────────────────────────────
function getHost() {
  return (document.getElementById('ps5-ip')?.value || '').trim();
}

// ── IP validation ─────────────────────────────────────────────────
function validateIp() {
  const val  = document.getElementById('ps5-ip')?.value || '';
  const warn = document.getElementById('ip-warn');
  if (!warn) return;
  const ok = /^(\d{1,3}\.){3}\d{1,3}$/.test(val.trim()) || val.trim() === '';
  warn.style.display = ok ? 'none' : 'block';
}

// ── Version ───────────────────────────────────────────────────────
async function loadVersion() {
  try {
    const v = await api(`${BASE}/api/version`);
    const el = document.getElementById('version-badge');
    if (el) el.textContent = `v${v.version || '–'}`;
  } catch (_) { /* ok */ }
}

// ── Toast ─────────────────────────────────────────────────────────
function showToast(msg, duration = 2200) {
  let el = document.querySelector('.ps5-toast');
  if (!el) {
    el = document.createElement('div');
    el.className = 'ps5-toast';
    document.body.appendChild(el);
  }
  el.textContent = msg;
  el.classList.add('ps5-toast-show');
  clearTimeout(el._t);
  el._t = setTimeout(() => el.classList.remove('ps5-toast-show'), duration);
}

// ── Formatters ────────────────────────────────────────────────────
function formatBytes(n) {
  if (n < 1024)       return `${n} B`;
  if (n < 1048576)    return `${(n/1024).toFixed(1)} KB`;
  return `${(n/1048576).toFixed(1)} MB`;
}

function formatDate(s) {
  if (!s) return '';
  try { return new Date(s).toLocaleDateString(); } catch { return s; }
}

function displayVersion(v) {
  return v && v !== 'folder' ? v : (v === 'folder' ? 'folder' : '–');
}
