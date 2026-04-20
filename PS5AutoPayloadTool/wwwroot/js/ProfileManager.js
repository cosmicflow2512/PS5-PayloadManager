'use strict';

// ── Profile manager ───────────────────────────────────────────────
async function refreshProfiles() {
  try {
    state.profiles = await api(`${BASE}/api/autoload/profiles`);
    renderProfileList();
    renderFavorites();
    updateProfilesSummary();
  } catch (e) { log('Load profiles: ' + e.message, 'error'); }
}

function renderProfileList() {
  const el = document.getElementById('profile-list');
  if (!el) return;
  if (!state.profiles.length) { el.innerHTML = '<div class="empty-state">No flows created yet.</div>'; return; }

  el.innerHTML = '';
  const isActive  = state.execState === 'running' || state.execState === 'paused';
  const running   = state.runningProfile;

  state.profiles.forEach(name => {
    const isThisOne  = isActive && running === name;
    const isOtherOne = isActive && running !== name;
    const isFav      = state.favorites.includes(name);

    const item = document.createElement('div');
    item.className = 'profile-item fade';

    const nameEl = document.createElement('span');
    nameEl.className = 'profile-name'; nameEl.textContent = name.replace(/\.txt$/i, '');

    const favBtn = document.createElement('button');
    favBtn.className   = 'p-fav' + (isFav ? ' p-fav-active' : '');
    favBtn.textContent = '⭐'; favBtn.title = isFav ? 'Remove from Quick Start' : 'Pin to Quick Start';
    favBtn.addEventListener('click', () => { toggleFavorite(name); });

    const editBtn = document.createElement('button');
    editBtn.className = 'btn btn-sm'; editBtn.textContent = '✎';
    editBtn.addEventListener('click', () => editProfile(name));

    const runBtn = document.createElement('button');
    runBtn.className   = isThisOne ? 'btn btn-sm btn-danger' : 'btn btn-sm btn-primary';
    runBtn.textContent = isThisOne ? '■ Stop' : '▶ Run';
    runBtn.disabled    = isOtherOne;
    runBtn.addEventListener('click', () => {
      if (isThisOne) stopAutoload();
      else           runProfile(name, runBtn);
    });

    const delBtn = document.createElement('button');
    delBtn.className = 'btn btn-sm btn-danger'; delBtn.textContent = '✕';
    delBtn.addEventListener('click', async () => {
      if (!confirm(`Delete flow '${name.replace(/\.txt$/i, '')}'?`)) return;
      await api(`${BASE}/api/autoload/content/${encodeURIComponent(name)}`, { method: 'DELETE' });
      state.favorites = state.favorites.filter(f => f !== name);
      await refreshProfiles();
      scheduleSave();
    });

    item.appendChild(nameEl);
    item.appendChild(favBtn);
    item.appendChild(editBtn);
    item.appendChild(runBtn);
    item.appendChild(delBtn);
    el.appendChild(item);
  });
}

function toggleFavorite(name) {
  const idx = state.favorites.indexOf(name);
  if (idx >= 0) state.favorites.splice(idx, 1);
  else          state.favorites.push(name);
  renderProfileList();
  renderFavorites();
  scheduleSave();
}

async function runProfile(name, btn) {
  const host = getHost();
  if (!host) { showToast('Enter a PS5 IP first'); return; }
  const continueOnError = document.getElementById('continue-on-error')?.checked ?? false;
  if (btn) { btn.disabled = true; }
  try {
    await api(`${BASE}/api/autoload/run`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ host, profile: name, continue_on_error: continueOnError }),
    });
    log(`Running flow: ${name.replace(/\.txt$/i, '')}`, 'info');
  } catch (e) {
    log(`Run failed: ${e.message}`, 'error');
    if (btn) btn.disabled = false;
  }
}

async function editProfile(name) {
  try {
    const r = await api(`${BASE}/api/autoload/parse/${encodeURIComponent(name)}`);
    builder.steps = (r.steps || []).map(s => ({
      type:        s.type,
      filename:    s.filename || '',
      portOverride: s.portOverride ?? null,
      autoPort:    s.autoPort || (s.filename?.endsWith('.lua') ? 9026 : 9021),
      ms:          s.ms || 0,
      port:        s.port || 9021,
      timeout:     s.timeout || 60,
      intervalMs:  s.intervalMs || 500,
      version:     s.version || null,
    }));
    document.getElementById('builder-profile-name').value = name.replace(/\.txt$/i, '');
    renderBuilderSteps();
    updateBuilderSummary();
    document.getElementById('section-builder')?.scrollIntoView({ behavior: 'smooth' });
    scheduleSave();
  } catch (e) { log(`Edit profile error: ${e.message}`, 'error'); }
}
