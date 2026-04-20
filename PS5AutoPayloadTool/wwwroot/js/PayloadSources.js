'use strict';

// ── Payload Sources ───────────────────────────────────────────────
let _editingSourceIdx = -1;
let _detectedPayloads = [];

async function refreshSources() {
  try {
    state.sources = await api(`${BASE}/api/sources`);
    renderSourcesList();
    updateSourcesSummary();
  } catch (e) { log('Load sources: ' + e.message, 'error'); }
}

function renderSourcesList() {
  const el = document.getElementById('sources-list');
  if (!el) return;
  if (!state.sources.length) { el.innerHTML = '<div class="empty-state">No sources added yet.</div>'; return; }

  el.innerHTML = '';
  state.sources.forEach((src, idx) => {
    const item = document.createElement('div');
    item.className = 'source-item fade';

    const main = document.createElement('div');
    main.className = 'source-main-row';

    const info = document.createElement('div');
    info.className = 'source-info';
    const repo = document.createElement('span');
    repo.className = 'source-repo';
    repo.textContent = src.displayName || src.repo;
    const filter = document.createElement('span');
    filter.className = 'source-filter';
    const parts = [];
    if (src.filter)     parts.push(src.filter);
    if (src.sourceType && src.sourceType !== 'auto') parts.push(src.sourceType);
    if (src.folder)     parts.push(src.folder);
    filter.textContent = parts.join(' · ') || 'auto';
    info.appendChild(repo); info.appendChild(filter);

    const btns = document.createElement('div');
    btns.className = 'source-btns';

    const scanBtn = document.createElement('button');
    scanBtn.className = 'btn btn-sm'; scanBtn.textContent = '↻ Scan';
    scanBtn.addEventListener('click', () => _checkSourceUpdates(src, item, idx));

    const editBtn = document.createElement('button');
    editBtn.className = 'btn btn-sm'; editBtn.textContent = '✎';
    editBtn.addEventListener('click', () => _openEditSource(src, idx));

    const delBtn = document.createElement('button');
    delBtn.className = 'btn btn-sm btn-danger'; delBtn.textContent = '✕';
    delBtn.addEventListener('click', async () => {
      const [owner, ...rest] = src.repo.split('/');
      const repoName = rest.join('/');
      await api(`${BASE}/api/sources/${owner}/${repoName}`, { method: 'DELETE' });
      await refreshSources();
      log(`Removed source: ${src.repo}`, 'info');
    });

    btns.appendChild(scanBtn); btns.appendChild(editBtn); btns.appendChild(delBtn);
    main.appendChild(info); main.appendChild(btns);
    item.appendChild(main);
    el.appendChild(item);
  });
}

function toggleAddSourcePanel() {
  const panel = document.getElementById('source-add-panel');
  if (!panel) return;
  const open = panel.style.display !== 'none';
  if (open) { _closeSourcePanel(); return; }
  _editingSourceIdx = -1;
  document.getElementById('source-panel-title').textContent = 'Add Source';
  document.getElementById('source-repo-input').value    = '';
  document.getElementById('source-display-input').value = '';
  document.getElementById('source-filter-input').value  = '';
  document.querySelector('input[name="src-type"][value="auto"]').checked = true;
  document.getElementById('source-add-status').textContent = '';
  document.getElementById('source-detected').style.display = 'none';
  document.getElementById('btn-source-save').style.display = 'none';
  _onSourceTypeChange();
  panel.style.display = 'block';
}

function _closeSourcePanel() {
  const panel = document.getElementById('source-add-panel');
  if (panel) panel.style.display = 'none';
  _editingSourceIdx = -1;
  _detectedPayloads = [];
}

function _openEditSource(src, idx) {
  _editingSourceIdx = idx;
  document.getElementById('source-panel-title').textContent = 'Edit Source';
  document.getElementById('source-repo-input').value    = src.repo;
  document.getElementById('source-display-input').value = src.displayName || '';
  document.getElementById('source-filter-input').value  = src.filter || '';
  const typeInput = document.querySelector(`input[name="src-type"][value="${src.sourceType || 'auto'}"]`);
  if (typeInput) typeInput.checked = true;
  _onSourceTypeChange();
  if (src.sourceType === 'folder' && src.folder) {
    document.getElementById('source-folder-hint').textContent = `Current: ${src.folder}`;
  }
  document.getElementById('source-add-status').textContent = '';
  document.getElementById('source-detected').style.display = 'none';
  document.getElementById('btn-source-save').style.display = 'inline-flex';
  document.getElementById('source-add-panel').style.display = 'block';
}

async function saveSourceConfig() {
  const idx = _editingSourceIdx;
  if (idx < 0 || idx >= state.sources.length) return;
  const src   = state.sources[idx];
  const [owner, ...rest] = src.repo.split('/');
  const filter = document.getElementById('source-filter-input')?.value.trim() || '';
  const type   = document.querySelector('input[name="src-type"]:checked')?.value || 'auto';
  const folder = document.getElementById('source-folder-select')?.value || '';
  const disp   = document.getElementById('source-display-input')?.value.trim() || '';
  await api(`${BASE}/api/sources/${owner}/${rest.join('/')}`, {
    method: 'PUT', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ filter, source_type: type, folder, display_name: disp, repo: src.repo }),
  });
  showToast('Source saved');
  _closeSourcePanel();
  await refreshSources();
}

async function addSource() {
  const repo   = document.getElementById('source-repo-input')?.value.trim();
  const filter = document.getElementById('source-filter-input')?.value.trim() || '';
  const type   = document.querySelector('input[name="src-type"]:checked')?.value || 'auto';
  const folder = document.getElementById('source-folder-select')?.value || '';
  const disp   = document.getElementById('source-display-input')?.value.trim() || '';
  const status = document.getElementById('source-add-status');

  if (!repo) { showToast('Enter a repository'); return; }
  status.textContent = 'Scanning …'; status.className = 'source-status loading';

  try {
    const r = await api(`${BASE}/api/sources`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ repo, filter, source_type: type, folder, display_name: disp }),
    });
    status.textContent = `Found ${(r.detected || []).length} payload(s)`; status.className = 'source-status ok';
    _detectedPayloads  = r.detected || [];
    _renderDetected(_detectedPayloads);
    await refreshSources();
    log(`Source added: ${r.repo} (${_detectedPayloads.length} detected)`, 'success');
  } catch (e) {
    status.textContent = 'Error: ' + e.message; status.className = 'source-status error';
    log('Add source error: ' + e.message, 'error');
  }
}

function _renderDetected(items) {
  const wrap = document.getElementById('source-detected');
  const list = document.getElementById('source-detected-list');
  const importBtn = document.getElementById('btn-source-import');
  const count     = document.getElementById('detected-count');
  if (!items.length) { wrap.style.display = 'none'; return; }

  wrap.style.display = 'block';
  list.innerHTML = '';
  count.textContent  = `${items.length} found`;

  items.forEach((item, i) => {
    const row = document.createElement('div');
    row.className = 'detected-row';
    const top = document.createElement('div');
    top.className = 'detected-row-top';
    const cb = document.createElement('input');
    cb.type = 'checkbox'; cb.checked = true; cb.dataset.idx = i;
    cb.addEventListener('change', _updateImportBtn);
    const nameEl = document.createElement('span');
    nameEl.className = 'detected-name'; nameEl.textContent = item.name;
    const ver = document.createElement('span');
    ver.className = 'detected-ver'; ver.textContent = item.version || '';
    top.appendChild(cb); top.appendChild(nameEl); top.appendChild(ver);
    row.appendChild(top);
    list.appendChild(row);
  });

  importBtn.disabled = false;
  _updateImportBtn();
}

function _updateImportBtn() {
  const cb  = document.querySelectorAll('#source-detected-list input[type=checkbox]:checked');
  const btn = document.getElementById('btn-source-import');
  if (btn) btn.disabled = cb.length === 0;
  const count = document.getElementById('detected-count');
  if (count) count.textContent = `${document.querySelectorAll('#source-detected-list input[type=checkbox]').length} found`;
}

async function importSelected() {
  const checked = [...document.querySelectorAll('#source-detected-list input[type=checkbox]:checked')];
  if (!checked.length) return;
  let imported = 0;
  for (const cb of checked) {
    const item = _detectedPayloads[parseInt(cb.dataset.idx)];
    if (!item) continue;
    try {
      await api(`${BASE}/api/payloads/import`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          repo: item.repo, asset_name: item.asset_name || item.name,
          download_url: item.download_url, version: item.version || '',
          all_versions: item.all_versions || [],
          release_published_at: item.release_published_at || '',
          asset_updated_at:     item.asset_updated_at || '',
          asset_size:           item.asset_size || 0,
          release_id:           item.release_id || 0,
        }),
      });
      imported++;
    } catch (e) { log(`Import ${item.name} failed: ${e.message}`, 'error'); }
  }
  log(`Imported ${imported} payload(s)`, 'success');
  showToast(`Imported ${imported} payload(s)`);
  _closeSourcePanel();
  await refreshPayloads();
  builderUpdatePayloadDropdown();
}

async function checkAllUpdates() {
  const badge = document.getElementById('update-count-badge');
  if (badge) { badge.textContent = 'Checking …'; badge.className = 'update-count-badge'; }
  try {
    const updates = await api(`${BASE}/api/sources/check-updates`);
    state.updateResults = updates;
    if (updates.length) {
      if (badge) { badge.textContent = `${updates.length} update(s) available`; badge.className = 'update-count-badge warn'; }
      document.getElementById('btn-update-all').style.display = 'inline-flex';
    } else {
      if (badge) { badge.textContent = 'All up to date'; badge.className = 'update-count-badge ok'; }
      document.getElementById('btn-update-all').style.display = 'none';
    }
    renderPayloads();
  } catch (e) {
    if (badge) badge.textContent = '';
    log('Check updates error: ' + e.message, 'error');
  }
}

async function updateAll() {
  const updates = state.updateResults;
  if (!updates.length) return;
  let done = 0;
  for (const u of updates) {
    try {
      await api(`${BASE}/api/payloads/import`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          repo: '', asset_name: u.filename, download_url: u.downloadUrl,
          version: u.newVersion, release_published_at: u.publishedAt,
          asset_size: u.assetSize, release_id: u.releaseId,
          asset_updated_at: u.assetUpdatedAt || '',
        }),
      });
      done++;
    } catch (e) { log(`Update ${u.filename} failed: ${e.message}`, 'error'); }
  }
  state.updateResults = [];
  log(`Updated ${done} payload(s)`, 'success');
  document.getElementById('btn-update-all').style.display = 'none';
  await refreshPayloads();
}

function _onSourceTypeChange() {
  const type = document.querySelector('input[name="src-type"]:checked')?.value || 'auto';
  const wrap = document.getElementById('source-folder-wrap');
  if (wrap) wrap.style.display = type === 'folder' ? 'block' : 'none';
}

function _selectAllDetected() {
  document.querySelectorAll('#source-detected-list input[type=checkbox]').forEach(cb => cb.checked = true);
  _updateImportBtn();
}

function _deselectAllDetected() {
  document.querySelectorAll('#source-detected-list input[type=checkbox]').forEach(cb => cb.checked = false);
  _updateImportBtn();
}

async function loadRepoFolders() {
  const repo = document.getElementById('source-repo-input')?.value.trim();
  if (!repo) { showToast('Enter a repository first'); return; }
  const hint = document.getElementById('source-folder-hint');
  if (hint) hint.textContent = 'Loading …';
  try {
    const r   = await api(`${BASE}/api/sources/tree?repo=${encodeURIComponent(repo)}`);
    const sel = document.getElementById('source-folder-select');
    while (sel.options.length > 1) sel.remove(1);
    (r.folders || []).forEach(f => {
      const opt = document.createElement('option'); opt.value = f; opt.textContent = f; sel.appendChild(opt);
    });
    if (hint) hint.textContent = `${(r.folders || []).length} folder(s) found`;
  } catch (e) {
    if (hint) hint.textContent = 'Error: ' + e.message;
  }
}

async function _checkSourceUpdates(src, item, idx) {
  const panel = document.createElement('div');
  panel.className = 'source-check-panel';
  const status = document.createElement('div');
  status.className = 'source-check-status'; status.textContent = 'Checking …';
  panel.appendChild(status);
  const existing = item.querySelector('.source-check-panel');
  if (existing) existing.remove();
  item.appendChild(panel);

  try {
    const updates = state.updateResults.filter(u => {
      const pay = state.payloads.find(p => p.name === u.filename);
      return pay?.repo === src.repo;
    });
    if (updates.length) {
      status.textContent = `${updates.length} update(s) available`; status.className = 'source-check-status warn';
    } else {
      status.textContent = 'Up to date'; status.className = 'source-check-status ok';
    }
  } catch (e) {
    status.textContent = 'Error: ' + e.message; status.className = 'source-check-status';
  }
}
