'use strict';

// ── Log ───────────────────────────────────────────────────────────
const _logHistory = [];
const MAX_LOG     = 500;

function log(msg, level = 'info') {
  const now  = new Date();
  const ts   = `${String(now.getHours()).padStart(2,'0')}:${String(now.getMinutes()).padStart(2,'0')}:${String(now.getSeconds()).padStart(2,'0')}`;
  const entry = { ts, msg, level };
  _logHistory.push(entry);
  if (_logHistory.length > MAX_LOG) _logHistory.shift();

  const filter = document.getElementById('log-filter')?.value || 'all';
  if (_matchesFilter(entry, filter)) _appendLogEntry(entry);
}

function _appendLogEntry({ ts, msg, level }) {
  const el  = document.getElementById('status-log');
  if (!el) return;
  const div = document.createElement('div');
  div.className = `log-entry log-${level}`;
  div.innerHTML = `<span class="log-ts">${ts}</span> ${_esc(msg)}`;
  el.appendChild(div);
  el.scrollTop = el.scrollHeight;
}

function clearLog() {
  _logHistory.length = 0;
  const el = document.getElementById('status-log');
  if (el) el.innerHTML = '';
}

function applyLogFilter() {
  const filter = document.getElementById('log-filter')?.value || 'all';
  const el     = document.getElementById('status-log');
  if (!el) return;
  el.innerHTML = '';
  _logHistory.filter(e => _matchesFilter(e, filter)).forEach(_appendLogEntry);
}

function _matchesFilter(entry, filter) {
  if (filter === 'all')     return true;
  if (filter === 'port')    return /port/i.test(entry.msg);
  if (filter === 'payload') return /send|payload|import/i.test(entry.msg);
  return true;
}

function _esc(s) {
  return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}

// ── Export log ────────────────────────────────────────────────────
async function exportLog(fmt) {
  try {
    const entries = _logHistory.map(e => `[${e.ts}] [${e.level}] ${e.msg}`);
    const resp    = await fetch(`${BASE}/api/logs/export`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ entries }),
    });
    const blob = await resp.blob();
    const cd   = resp.headers.get('content-disposition') || '';
    const m    = cd.match(/filename="?([^"]+)"?/);
    const name = m ? m[1] : `ps5_log.txt`;
    const url  = URL.createObjectURL(blob);
    const a    = Object.assign(document.createElement('a'), { href: url, download: name });
    a.click();
    URL.revokeObjectURL(url);
  } catch (e) { showToast('Export failed: ' + e.message); }
}

// ── Port check ────────────────────────────────────────────────────
async function checkPortOnce() {
  const host    = getHost();
  const port    = parseInt(document.getElementById('check-port')?.value || '9021');
  const timeout = parseFloat(document.getElementById('check-timeout')?.value || '10');
  if (!host) { showToast('Enter a PS5 IP first'); return; }
  if (!port || port < 1 || port > 65535) { showToast('Invalid port'); return; }

  const result = document.getElementById('port-check-result');
  result.style.display = 'block';
  result.className = '';
  result.textContent = 'Checking …';

  try {
    const r = await api(`${BASE}/api/port/check`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ host, port, timeout, interval: 0.5 }),
    });
    result.className = r.open ? 'res-ok' : 'res-fail';
    result.textContent = r.open ? `✔ Port ${port} is open` : `✗ Port ${port} is closed`;
    log(`Port ${port}: ${r.open ? 'open' : 'closed'}`, r.open ? 'success' : 'warn');
  } catch (e) {
    result.className  = 'res-fail';
    result.textContent = `Error: ${e.message}`;
    log(`Port check error: ${e.message}`, 'error');
  }
}

async function waitForPort() {
  const host     = getHost();
  const port     = parseInt(document.getElementById('check-port')?.value || '9021');
  const timeout  = parseFloat(document.getElementById('check-timeout')?.value || '10');
  const interval = parseFloat(document.getElementById('check-interval')?.value || '500') / 1000;
  if (!host) { showToast('Enter a PS5 IP first'); return; }

  const result = document.getElementById('port-check-result');
  result.style.display = 'block';
  result.className  = '';
  result.textContent = `Waiting for port ${port} …`;
  log(`Waiting for port ${port} (up to ${timeout}s) …`, 'info');

  try {
    const r = await api(`${BASE}/api/port/wait`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ host, port, timeout, interval }),
    });
    result.className  = r.open ? 'res-ok' : 'res-fail';
    result.textContent = r.open ? `✔ Port ${port} opened` : `✗ Port ${port} timed out`;
    log(`Port ${port}: ${r.open ? 'opened' : 'timed out'}`, r.open ? 'success' : 'warn');
  } catch (e) {
    result.className  = 'res-fail';
    result.textContent = `Error: ${e.message}`;
    log(`Wait port error: ${e.message}`, 'error');
  }
}
