'use strict';

// ── Step run status ───────────────────────────────────────────────
let _currentStepIdx  = -1;
const _stepRunStatus = {};

// ── Panel toggle ──────────────────────────────────────────────────
function builderTogglePanel(type) {
  ['payload', 'delay', 'wait'].forEach(t => {
    const el = document.getElementById(`panel-${t}`);
    if (el) el.style.display = (t === type && el.style.display === 'none') ? 'block' : 'none';
  });
  if (type === 'payload') builderUpdatePayloadDropdown();
}

// ── Add steps ─────────────────────────────────────────────────────
function builderAddPayloadStepFromSelect() {
  const sel  = document.getElementById('panel-payload-select');
  const name = sel?.value;
  if (!name) return;
  _addPayloadStep(name, null);
  sel.value = '';
  builderTogglePanel('payload');
}

function builderAddPayloadStep() {
  const sel  = document.getElementById('panel-payload-select');
  const port = document.getElementById('panel-payload-port');
  const name = sel?.value;
  if (!name) { showToast('Select a payload first'); return; }
  const p = port?.value ? parseInt(port.value) : null;
  if (p && (p < 1 || p > 65535)) { showToast('Invalid port'); return; }
  _addPayloadStep(name, p);
  if (sel) sel.value = '';
  if (port) port.value = '';
  builderTogglePanel('payload');
}

function _addPayloadStep(filename, portOverride) {
  const ext      = filename.split('.').pop().toLowerCase();
  const autoPort = ext === 'lua' ? 9026 : 9021;
  const pay      = state.payloads.find(p => p.name === filename);
  builder.steps.push({
    type: 'payload', filename,
    portOverride: portOverride || null,
    autoPort,
    version: pay?.version || null,
  });
  renderBuilderSteps();
  updateBuilderSummary();
  scheduleSave();
}

function builderAddDelayStep(ms) {
  builder.steps.push({ type: 'delay', ms });
  renderBuilderSteps();
  updateBuilderSummary();
  scheduleSave();
  builderTogglePanel('delay');
}

function builderAddWaitStep() {
  const port     = parseInt(document.getElementById('panel-wait-port')?.value || '9021');
  const timeout  = parseFloat(document.getElementById('panel-wait-to')?.value || '90');
  const interval = parseInt(document.getElementById('panel-wait-interval')?.value || '500');
  if (!port || port < 1 || port > 65535) { showToast('Invalid port'); return; }
  builder.steps.push({ type: 'wait_port', port, timeout, intervalMs: interval });
  renderBuilderSteps();
  updateBuilderSummary();
  scheduleSave();
  builderTogglePanel('wait');
}

// ── Render steps ──────────────────────────────────────────────────
function renderBuilderSteps() {
  const el = document.getElementById('builder-steps');
  if (!el) return;
  if (!builder.steps.length) {
    el.innerHTML = '<div class="empty-state">No steps yet. Choose a step type above.</div>';
    return;
  }
  el.innerHTML = '';
  builder.steps.forEach((step, idx) => {
    el.appendChild(_buildStepEl(step, idx));
  });
}

function _buildStepEl(step, idx) {
  const wrap = document.createElement('div');
  wrap.className   = 'builder-step fade';
  wrap.draggable   = true;
  wrap.dataset.idx = idx;
  _setupDrag(wrap, idx);

  const main = document.createElement('div');
  main.className = 'step-main';

  const handle = document.createElement('span');
  handle.className   = 'drag-handle'; handle.textContent = '⠿'; handle.title = 'Drag to reorder';

  const num = document.createElement('span');
  num.className = 'step-num'; num.textContent = idx + 1;

  const status = document.createElement('span');
  status.className = 'step-run-status';
  const st = _stepRunStatus[idx];
  status.textContent = st === 'running' ? '⏳' : st === 'done' ? '✔' : st === 'error' ? '✗' : '';
  status.classList.toggle('step-running', st === 'running');
  status.classList.toggle('step-done',    st === 'done');
  status.classList.toggle('step-error',   st === 'error');

  const upBtn = document.createElement('button');
  upBtn.className = 'btn btn-sm'; upBtn.textContent = '▲'; upBtn.disabled = idx === 0;
  upBtn.addEventListener('click', () => { _moveStep(idx, -1); });

  const dnBtn = document.createElement('button');
  dnBtn.className = 'btn btn-sm'; dnBtn.textContent = '▼'; dnBtn.disabled = idx === builder.steps.length - 1;
  dnBtn.addEventListener('click', () => { _moveStep(idx, 1); });

  const delBtn = document.createElement('button');
  delBtn.className = 'btn btn-sm btn-danger'; delBtn.textContent = '✕';
  delBtn.addEventListener('click', () => { builder.steps.splice(idx, 1); renderBuilderSteps(); updateBuilderSummary(); scheduleSave(); });

  main.appendChild(handle);
  main.appendChild(num);
  main.appendChild(status);

  if (step.type === 'payload') {
    const ext   = (step.filename || '').split('.').pop().toLowerCase();
    const label = document.createElement('span');
    label.className = `payload-label ${ext === 'lua' ? 'lua' : 'elf'}`;
    label.textContent = 'Payload';

    const name = document.createElement('span');
    name.className   = 'step-filename'; name.textContent = step.filename || '';
    name.title       = 'Click to change';
    name.addEventListener('click', () => _openInlineEdit(wrap, idx));

    const portSpan = document.createElement('span');
    portSpan.className   = 'step-autoport';
    portSpan.textContent = step.portOverride ? `:${step.portOverride}` : `:${step.autoPort}`;

    const btns = document.createElement('div');
    btns.className = 'step-btns';
    btns.appendChild(upBtn); btns.appendChild(dnBtn); btns.appendChild(delBtn);

    main.appendChild(label); main.appendChild(name); main.appendChild(portSpan); main.appendChild(btns);

    // Version row
    if (step.version) {
      const info = document.createElement('div');
      info.className = 'step-info-row';
      const verLabel = document.createElement('span');
      verLabel.className = 'step-ver-label'; verLabel.textContent = 'ver:';
      const verSel = document.createElement('select');
      verSel.className = 'step-ver-inline';
      const pay = state.payloads.find(p => p.name === step.filename);
      const versions = pay?.allVersions || [{ tag: step.version }];
      versions.forEach(v => {
        const opt = document.createElement('option');
        opt.value = v.tag; opt.textContent = v.tag;
        if (v.tag === step.version) opt.selected = true;
        verSel.appendChild(opt);
      });
      verSel.addEventListener('change', () => {
        step.version = verSel.value;
        scheduleSave();
      });
      info.appendChild(verLabel); info.appendChild(verSel);
      wrap.appendChild(main); wrap.appendChild(info);
    } else {
      wrap.appendChild(main);
    }

  } else if (step.type === 'delay') {
    const typeBadge = document.createElement('span');
    typeBadge.className = 'step-type step-delay'; typeBadge.textContent = 'DELAY';

    const msInput = document.createElement('input');
    msInput.type = 'number'; msInput.className = 'step-input'; msInput.value = step.ms;
    msInput.min  = 1; msInput.style.width = '72px';
    msInput.addEventListener('change', () => { step.ms = parseInt(msInput.value) || step.ms; scheduleSave(); });

    const unit = document.createElement('span');
    unit.className = 'step-unit'; unit.textContent = 'ms';

    const btns = document.createElement('div');
    btns.className = 'step-btns';
    btns.appendChild(upBtn); btns.appendChild(dnBtn); btns.appendChild(delBtn);

    main.appendChild(typeBadge); main.appendChild(msInput); main.appendChild(unit); main.appendChild(btns);
    wrap.appendChild(main);

  } else if (step.type === 'wait_port') {
    const typeBadge = document.createElement('span');
    typeBadge.className = 'step-type step-wait'; typeBadge.textContent = 'WAIT';

    const portInput = document.createElement('input');
    portInput.type = 'number'; portInput.className = 'step-input'; portInput.value = step.port;
    portInput.min  = 1; portInput.max = 65535;
    portInput.addEventListener('change', () => { step.port = parseInt(portInput.value) || step.port; scheduleSave(); });

    const toInput = document.createElement('input');
    toInput.type = 'number'; toInput.className = 'step-input step-input-sm'; toInput.value = step.timeout;
    toInput.min  = 1;
    toInput.addEventListener('change', () => { step.timeout = parseFloat(toInput.value) || step.timeout; scheduleSave(); });

    const toUnit = document.createElement('span'); toUnit.className = 'step-unit'; toUnit.textContent = 's';

    const btns = document.createElement('div');
    btns.className = 'step-btns';
    btns.appendChild(upBtn); btns.appendChild(dnBtn); btns.appendChild(delBtn);

    main.appendChild(typeBadge); main.appendChild(portInput); main.appendChild(toInput); main.appendChild(toUnit); main.appendChild(btns);
    wrap.appendChild(main);
  }

  return wrap;
}

function _openInlineEdit(wrap, idx) {
  const existing = wrap.querySelector('.step-edit-panel');
  if (existing) { existing.remove(); return; }
  const step = builder.steps[idx];
  const panel = document.createElement('div');
  panel.className = 'step-edit-panel';

  const sel = document.createElement('select');
  sel.className = 'step-edit-select';
  const none = document.createElement('option'); none.value = ''; none.textContent = '– Select –';
  sel.appendChild(none);
  state.payloads.forEach(p => {
    const opt = document.createElement('option');
    opt.value = p.name; opt.textContent = p.name;
    if (p.name === step.filename) opt.selected = true;
    sel.appendChild(opt);
  });

  const okBtn = document.createElement('button');
  okBtn.className = 'btn btn-sm btn-primary'; okBtn.textContent = '✔';
  okBtn.addEventListener('click', () => {
    if (sel.value) {
      step.filename = sel.value;
      const ext = sel.value.split('.').pop().toLowerCase();
      step.autoPort = ext === 'lua' ? 9026 : 9021;
    }
    renderBuilderSteps(); scheduleSave();
  });

  const xBtn = document.createElement('button');
  xBtn.className = 'btn btn-sm'; xBtn.textContent = '✕';
  xBtn.addEventListener('click', () => panel.remove());

  panel.appendChild(sel); panel.appendChild(okBtn); panel.appendChild(xBtn);
  wrap.appendChild(panel);
}

function _moveStep(idx, dir) {
  const newIdx = idx + dir;
  if (newIdx < 0 || newIdx >= builder.steps.length) return;
  [builder.steps[idx], builder.steps[newIdx]] = [builder.steps[newIdx], builder.steps[idx]];
  renderBuilderSteps(); scheduleSave();
}

// ── Drag & drop ───────────────────────────────────────────────────
let _dragIdx = -1;
function _setupDrag(el, idx) {
  el.addEventListener('dragstart', () => { _dragIdx = idx; el.classList.add('dragging'); });
  el.addEventListener('dragend',   () => { el.classList.remove('dragging'); _dragIdx = -1; });
  el.addEventListener('dragover',  e => { e.preventDefault(); el.classList.add('drag-over'); });
  el.addEventListener('dragleave', () => el.classList.remove('drag-over'));
  el.addEventListener('drop',      () => {
    el.classList.remove('drag-over');
    if (_dragIdx === -1 || _dragIdx === idx) return;
    const moved = builder.steps.splice(_dragIdx, 1)[0];
    builder.steps.splice(idx, 0, moved);
    renderBuilderSteps(); scheduleSave();
  });
}

// ── Payload dropdown ──────────────────────────────────────────────
function builderUpdatePayloadDropdown() {
  const sel    = document.getElementById('panel-payload-select');
  const search = (document.getElementById('builder-payload-search')?.value || '').toLowerCase();
  if (!sel) return;
  const cur = sel.value;
  while (sel.options.length > 1) sel.remove(1);
  const favs  = state.payloads.filter(p => state.payloadFavs.includes(p.name) && (!search || p.name.toLowerCase().includes(search)));
  const other = state.payloads.filter(p => !state.payloadFavs.includes(p.name) && (!search || p.name.toLowerCase().includes(search)));
  if (favs.length) {
    const grp = document.createElement('optgroup'); grp.label = '⭐ Favorites';
    favs.forEach(p => { const o = document.createElement('option'); o.value = p.name; o.textContent = p.name; grp.appendChild(o); });
    sel.appendChild(grp);
  }
  if (other.length) {
    const grp = document.createElement('optgroup'); grp.label = 'All payloads';
    other.forEach(p => { const o = document.createElement('option'); o.value = p.name; o.textContent = p.name; grp.appendChild(o); });
    sel.appendChild(grp);
  }
  if (cur) sel.value = cur;
}

// ── Save & run ────────────────────────────────────────────────────
async function builderSave() {
  const name = document.getElementById('builder-profile-name')?.value.trim();
  if (!name) { showToast('Enter a flow name'); return; }
  const content = _stepsToContent();
  await api(`${BASE}/api/autoload/content`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ profile: name, content }),
  });
  log(`Flow '${name}' saved`, 'success');
  showToast(`Saved: ${name}`);
  await refreshProfiles();
}

async function builderRunDirect() {
  const host = getHost();
  if (!host) { showToast('Enter a PS5 IP first'); return; }
  const name    = document.getElementById('builder-profile-name')?.value.trim() || '';
  const content = _stepsToContent();
  _currentStepIdx = -1;
  Object.keys(_stepRunStatus).forEach(k => delete _stepRunStatus[k]);
  renderBuilderSteps();
  await api(`${BASE}/api/autoload/run`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ host, profile: name, content, continue_on_error: false }),
  });
}

async function stopAutoload() {
  await api(`${BASE}/api/autoload/stop`, { method: 'POST' });
}

function _stepsToContent() {
  return builder.steps.map(s => {
    if (s.type === 'payload') {
      const port = s.portOverride || s.autoPort;
      const line = `${s.filename} ${port}`;
      return s.version ? `${line}\n# ~${s.filename} ${s.version}` : line;
    }
    if (s.type === 'delay')     return `!${s.ms}`;
    if (s.type === 'wait_port') return `?${s.port} ${s.timeout} ${s.intervalMs || 500}`;
    return '';
  }).filter(Boolean).join('\n');
}

// ── Status feedback from WS ───────────────────────────────────────
function handleBuilderStepStatus(msg) {
  const m = msg.message?.match(/^\[(\d+)\/\d+\]/);
  if (!m) return;
  const idx = parseInt(m[1]) - 1;
  if (msg.level === 'success') _stepRunStatus[idx] = 'done';
  else if (msg.level === 'error') _stepRunStatus[idx] = 'error';
  else { _currentStepIdx = idx; _stepRunStatus[idx] = 'running'; }
  renderBuilderSteps();
}

function handleExecState(execState, profile) {
  const btn = document.getElementById('btn-builder-run');
  if (!btn) return;
  if (execState === 'running' || execState === 'paused') {
    btn.textContent = '■ Stop';
    btn.classList.remove('btn-primary'); btn.classList.add('btn-danger');
  } else {
    btn.textContent = '▶ Run';
    btn.classList.remove('btn-danger'); btn.classList.add('btn-primary');
    if (execState === 'completed') {
      Object.keys(_stepRunStatus).forEach(k => { if (_stepRunStatus[k] !== 'error') _stepRunStatus[k] = 'done'; });
      renderBuilderSteps();
    } else if (execState === 'failed' || execState === 'stopped') {
      renderBuilderSteps();
    }
  }
  renderFavorites();
  renderProfileList();
}

// ── Export autoload ZIP ───────────────────────────────────────────
async function exportAutoloadZip() {
  if (!builder.steps.length) { showToast('No steps in builder'); return; }
  try {
    const resp = await fetch(`${BASE}/api/autoload/export-zip`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ steps: builder.steps }),
    });
    const ct = resp.headers.get('content-type') || '';
    if (ct.includes('application/zip')) {
      const blob = await resp.blob();
      const url  = URL.createObjectURL(blob);
      const a    = Object.assign(document.createElement('a'), { href: url, download: 'autoload.zip' });
      a.click(); URL.revokeObjectURL(url);
      log('autoload.zip exported', 'success');
    } else {
      const j = await resp.json();
      showToast(j.error || 'Export failed');
      log(j.error || 'Export failed', 'error');
    }
  } catch (e) { log('Export error: ' + e.message, 'error'); }
}
