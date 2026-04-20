'use strict';

// ── Config backup / restore ───────────────────────────────────────
async function exportConfig() {
  try {
    const resp = await fetch(`${BASE}/api/backup`);
    const blob = await resp.blob();
    const url  = URL.createObjectURL(blob);
    const a    = Object.assign(document.createElement('a'), { href: url, download: 'ps5_backup.json' });
    a.click(); URL.revokeObjectURL(url);
    log('Config exported', 'success');
  } catch (e) { showToast('Export failed: ' + e.message); }
}

async function resetConfig() {
  if (!confirm('Factory reset? All config and payloads will be deleted (a backup is created first).')) return;
  try {
    await api(`${BASE}/api/config/reset`, { method: 'POST' });
    showToast('Reset complete — reloading …');
    setTimeout(() => location.reload(), 1200);
  } catch (e) { showToast('Reset failed: ' + e.message); }
}

// ── Import modal state ────────────────────────────────────────────
let _importData = null;

function importConfig() {
  const input = document.createElement('input');
  input.type  = 'file'; input.accept = '.json';
  input.addEventListener('change', async () => {
    const file = input.files[0];
    if (!file) return;
    try {
      const text = await file.text();
      _importData = JSON.parse(text);
      _showImportModal(_importData);
    } catch (e) { showToast('Invalid backup file: ' + e.message); }
  });
  input.click();
}

function _showImportModal(data) {
  const preview = document.getElementById('import-preview');
  const lines   = [];
  if (data.sources)  lines.push(`Sources: ${(data.sources  || []).length}`);
  if (data.payloads) lines.push(`Payloads: ${Object.keys(data.payloads || {}).length}`);
  if (data.profiles) lines.push(`Flows: ${Object.keys(data.profiles || {}).length}`);
  preview.innerHTML = lines.map(l => `<div>${l}</div>`).join('');

  document.getElementById('import-warnings').innerHTML = '';
  _validateImportDependencies(data);
  document.getElementById('import-modal').style.display = 'flex';
}

function _validateImportDependencies(data) {
  const warnings = document.getElementById('import-warnings');
  const payloadNames = new Set(Object.keys(data.payloads || {}));
  const profiles = data.profiles || {};
  let broken = 0;
  for (const [, content] of Object.entries(profiles)) {
    const lines = String(content).split('\n');
    for (const line of lines) {
      const m = line.trim().match(/^(\S+\.(?:elf|lua|bin))/i);
      if (m && !payloadNames.has(m[1])) broken++;
    }
  }
  if (broken > 0) {
    const el = document.createElement('div');
    el.className = 'import-warn import-warn-info';
    el.textContent = `${broken} flow step(s) reference payloads not in this backup.`;
    warnings.appendChild(el);
  }
}

async function _confirmImport() {
  if (!_importData) return;
  const mode     = document.querySelector('input[name="imp-mode"]:checked')?.value || 'merge';
  const sources  = document.getElementById('imp-sources')?.checked  ?? true;
  const payloads = document.getElementById('imp-payloads')?.checked ?? true;
  const flows    = document.getElementById('imp-flows')?.checked    ?? true;
  const profiles = document.getElementById('imp-profiles')?.checked ?? true;
  const settings = document.getElementById('imp-settings')?.checked ?? true;

  try {
    await api(`${BASE}/api/backup/restore-selective`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ backup: _importData, mode, sources, payloads, flows, profiles, settings }),
    });
    document.getElementById('import-modal').style.display = 'none';
    showToast('Import complete — reloading …');
    setTimeout(() => location.reload(), 1200);
  } catch (e) { showToast('Import failed: ' + e.message); }
}

document.addEventListener('DOMContentLoaded', () => {
  document.getElementById('btn-import-cancel')?.addEventListener('click',  () => {
    document.getElementById('import-modal').style.display = 'none';
  });
  document.getElementById('btn-import-confirm')?.addEventListener('click', _confirmImport);
  document.getElementById('import-modal')?.addEventListener('click', e => {
    if (e.target === e.currentTarget) e.currentTarget.style.display = 'none';
  });
});
