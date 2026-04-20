'use strict';

// ── Payload list ──────────────────────────────────────────────────
async function refreshPayloads() {
  try {
    state.payloads = await api(`${BASE}/api/payloads`);
    renderPayloads();
    updatePayloadsSummary();
  } catch (e) { log('Load payloads: ' + e.message, 'error'); }
}

function renderPayloads() {
  const el = document.getElementById('payload-list');
  if (!el) return;
  const list = getFilteredPayloads();
  if (!list.length) { el.innerHTML = '<div class="empty-state">No payloads found.</div>'; _syncSelectAll(false); renderBulkBar(); return; }

  el.innerHTML = '';
  list.forEach(p => el.appendChild(_buildPayloadItem(p)));
  _syncSelectAll(null);
  renderBulkBar();
}

function _buildPayloadItem(p) {
  const ext    = p.name.split('.').pop().toLowerCase();
  const isFav  = state.payloadFavs.includes(p.name);
  const isSel  = state.selectedPayloads.has(p.name);
  const hasUpd = state.updateResults.some(u => u.filename === p.name);

  const wrap = document.createElement('div');
  wrap.className = 'payload-item fade' + (isFav ? ' payload-fav' : '') + (isSel ? ' payload-selected' : '');

  // Top row
  const top = document.createElement('div');
  top.className = 'p-row-top';

  const cb = document.createElement('input');
  cb.type = 'checkbox'; cb.className = 'p-checkbox'; cb.checked = isSel;
  cb.addEventListener('change', () => {
    if (cb.checked) state.selectedPayloads.add(p.name);
    else            state.selectedPayloads.delete(p.name);
    wrap.classList.toggle('payload-selected', cb.checked);
    renderBulkBar();
    _syncSelectAll(null);
  });

  const badge = document.createElement('span');
  badge.className = `badge badge-${ext === 'lua' ? 'lua' : 'elf'}`;
  badge.textContent = ext.toUpperCase();

  const nameEl = document.createElement('span');
  nameEl.className = 'p-name'; nameEl.textContent = p.name;

  const fav = document.createElement('button');
  fav.className = 'p-fav' + (isFav ? ' p-fav-active' : '');
  fav.textContent = '⭐'; fav.title = isFav ? 'Remove from favorites' : 'Add to favorites';
  fav.addEventListener('click', e => { e.stopPropagation(); togglePayloadFavorite(p.name); });

  if (hasUpd) {
    const upd = document.createElement('span');
    upd.className = 'payload-update-warn'; upd.textContent = '↑ Update';
    top.appendChild(cb); top.appendChild(badge); top.appendChild(nameEl); top.appendChild(upd); top.appendChild(fav);
  } else {
    top.appendChild(cb); top.appendChild(badge); top.appendChild(nameEl); top.appendChild(fav);
  }

  // Bottom row
  const bot = document.createElement('div');
  bot.className = 'p-row-bottom';

  const meta = document.createElement('span');
  meta.className = 'p-meta'; meta.textContent = formatBytes(p.size);

  const sendBtn = document.createElement('button');
  sendBtn.className = 'p-send'; sendBtn.textContent = '▶ Send';
  sendBtn.addEventListener('click', e => { e.stopPropagation(); sendDirect(p.name); });

  const delBtn = document.createElement('button');
  delBtn.className = 'p-del'; delBtn.textContent = '✕'; delBtn.title = 'Delete';
  delBtn.addEventListener('click', async e => {
    e.stopPropagation();
    if (!confirm(`Delete ${p.name}?`)) return;
    await api(`${BASE}/api/payloads/${encodeURIComponent(p.name)}`, { method: 'DELETE' });
    await refreshPayloads();
    builderUpdatePayloadDropdown();
    log(`Deleted ${p.name}`, 'info');
  });

  bot.appendChild(meta);

  // Version selector if remote
  if (p.repo && p.allVersions?.length > 1) {
    const verLabel = document.createElement('span');
    verLabel.className = 'p-ver-label'; verLabel.textContent = 'ver:';
    const verSel = document.createElement('select');
    verSel.className = 'p-ver-select';
    p.allVersions.forEach(v => {
      const opt = document.createElement('option');
      opt.value = v.tag; opt.textContent = v.tag;
      if (v.tag === p.version) opt.selected = true;
      verSel.appendChild(opt);
    });
    verSel.addEventListener('change', async () => {
      const tag = verSel.value;
      const ver = p.allVersions.find(v => v.tag === tag);
      if (!ver) return;
      try {
        await api(`${BASE}/api/payloads/${encodeURIComponent(p.name)}/switch-version`, {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ version: tag, download_url: ver.downloadUrl || ver.download_url }),
        });
        log(`Switched ${p.name} → ${tag}`, 'success');
        await refreshPayloads();
      } catch (e) { log(`Switch failed: ${e.message}`, 'error'); }
    });
    bot.appendChild(verLabel); bot.appendChild(verSel);
  } else if (p.repo) {
    const verSpan = document.createElement('span');
    verSpan.className = 'source-ver'; verSpan.textContent = displayVersion(p.version);
    bot.appendChild(verSpan);
  } else {
    const local = document.createElement('span');
    local.className = 'p-local-file'; local.textContent = 'local';
    bot.appendChild(local);
  }

  bot.appendChild(delBtn);

  // Source row
  if (p.repo) {
    const src = document.createElement('div');
    src.className = 'p-row-source';
    const badge2 = document.createElement('span');
    badge2.className = 'source-badge'; badge2.textContent = p.repo;
    src.appendChild(badge2);

    if (hasUpd) {
      const upd2 = state.updateResults.find(u => u.filename === p.name);
      const updBtn = document.createElement('button');
      updBtn.className = 'btn btn-xs source-update-btn';
      updBtn.textContent = `↑ ${upd2?.newVersion || 'Update'}`;
      updBtn.addEventListener('click', async () => {
        if (!upd2) return;
        try {
          await api(`${BASE}/api/payloads/import`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              repo: p.repo, asset_name: p.name, download_url: upd2.downloadUrl,
              version: upd2.newVersion, release_published_at: upd2.publishedAt,
              asset_size: upd2.assetSize, release_id: upd2.releaseId,
            }),
          });
          state.updateResults = state.updateResults.filter(u => u.filename !== p.name);
          log(`Updated ${p.name} → ${upd2.newVersion}`, 'success');
          await refreshPayloads();
        } catch (e) { log(`Update failed: ${e.message}`, 'error'); }
      });
      src.appendChild(updBtn);
    }
    wrap.appendChild(top); wrap.appendChild(bot); wrap.appendChild(src);
  } else {
    wrap.appendChild(top); wrap.appendChild(bot);
  }

  return wrap;
}

function togglePayloadFavorite(name) {
  const idx = state.payloadFavs.indexOf(name);
  if (idx >= 0) state.payloadFavs.splice(idx, 1);
  else          state.payloadFavs.push(name);
  renderPayloads();
  scheduleSave();
}

async function sendDirect(name, portOverride) {
  const host = getHost();
  if (!host) { showToast('Enter a PS5 IP first'); return; }
  log(`Sending ${name} …`, 'info');
  try {
    const body = { host, filename: name };
    if (portOverride) body.port = portOverride;
    const r = await api(`${BASE}/api/send`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    log(r.message, r.ok ? 'success' : 'error');
  } catch (e) { log(`Send error: ${e.message}`, 'error'); }
}

async function bulkDeleteSelected() {
  const names = [...state.selectedPayloads];
  if (!names.length) return;
  if (!confirm(`Delete ${names.length} payload(s)?`)) return;
  for (const name of names) {
    await api(`${BASE}/api/payloads/${encodeURIComponent(name)}`, { method: 'DELETE' });
  }
  state.selectedPayloads.clear();
  await refreshPayloads();
  builderUpdatePayloadDropdown();
  log(`Deleted ${names.length} payload(s)`, 'info');
}

async function uploadPayloads(files) {
  if (!files?.length) return;
  const prog = document.getElementById('upload-progress');
  const bar  = document.getElementById('upload-bar');
  const lbl  = document.getElementById('upload-label');
  prog.style.display = 'block';
  const form = new FormData();
  Array.from(files).forEach(f => form.append('files', f));
  lbl.textContent = 'Uploading …';
  bar.style.width  = '30%';
  try {
    const r = await fetch(`${BASE}/api/payloads/upload`, { method: 'POST', body: form });
    const j = await r.json();
    bar.style.width = '100%';
    lbl.textContent = `Uploaded: ${(j.saved || []).join(', ') || 'none'}`;
    log(`Uploaded: ${(j.saved || []).join(', ')}`, 'success');
    await refreshPayloads();
    builderUpdatePayloadDropdown();
  } catch (e) {
    lbl.textContent = 'Upload failed: ' + e.message;
    log('Upload failed: ' + e.message, 'error');
  }
  setTimeout(() => { prog.style.display = 'none'; bar.style.width = '0'; }, 2000);
  document.getElementById('payload-upload').value = '';
}

// ── Bulk bar ──────────────────────────────────────────────────────
function renderBulkBar() {
  const bar   = document.getElementById('bulk-action-bar');
  const count = document.getElementById('bulk-count');
  const n     = state.selectedPayloads.size;
  bar.style.display = n > 0 ? 'flex' : 'none';
  if (count) count.textContent = `${n} selected`;
}

function _syncSelectAll(force) {
  const cb      = document.getElementById('payload-select-all');
  if (!cb) return;
  const filtered = getFilteredPayloads();
  if (force !== null) { cb.checked = !!force; cb.indeterminate = false; return; }
  const selCount = filtered.filter(p => state.selectedPayloads.has(p.name)).length;
  if (selCount === 0)               { cb.checked = false; cb.indeterminate = false; }
  else if (selCount === filtered.length) { cb.checked = true;  cb.indeterminate = false; }
  else                               { cb.checked = false; cb.indeterminate = true; }
}
