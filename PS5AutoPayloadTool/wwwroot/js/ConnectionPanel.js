'use strict';

// ── Connection Panel ──────────────────────────────────────────────
async function loadDevicesFromServer() {
  try {
    const data  = await api(`${BASE}/api/devices`);
    state.devices = data.devices || [];
    renderDeviceDropdown();
  } catch (e) { log('Load devices: ' + e.message, 'warn'); }
}

async function saveDevicesToServer() {
  try {
    await api(`${BASE}/api/devices`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ devices: state.devices }),
    });
  } catch (e) { log('Save devices: ' + e.message, 'error'); }
}

function renderDeviceDropdown() {
  const sel = document.getElementById('device-select');
  const cur = sel.value;
  while (sel.options.length > 1) sel.remove(1);
  state.devices.forEach(d => {
    const opt = document.createElement('option');
    opt.value = d.id; opt.textContent = `${d.name}  (${d.ip})`;
    sel.appendChild(opt);
  });
  if (cur) sel.value = cur;
}

function onDeviceSelect() {
  const id  = document.getElementById('device-select').value;
  if (!id) return;
  const dev = state.devices.find(d => d.id === id);
  if (dev) { document.getElementById('ps5-ip').value = dev.ip; scheduleSave(); }
}

function onIpChange() {
  const el    = document.getElementById('ps5-ip');
  const clean = el.value.replace(/,/g, '.').replace(/[^0-9.]/g, '');
  if (clean !== el.value) el.value = clean;
  const match = state.devices.find(d => d.ip === clean.trim());
  document.getElementById('device-select').value = match ? match.id : '';
  scheduleSave();
}

function openDeviceModal() {
  renderDeviceListModal();
  document.getElementById('device-modal').style.display = 'flex';
}

function closeDeviceModal() {
  document.getElementById('device-modal').style.display = 'none';
  document.getElementById('new-device-name').value = '';
  document.getElementById('new-device-ip').value   = '';
}

function renderDeviceListModal() {
  const container = document.getElementById('device-list-modal');
  container.innerHTML = '';
  if (!state.devices.length) {
    container.innerHTML = '<div class="empty-state">No saved devices</div>';
    return;
  }
  state.devices.forEach(d => {
    const el   = document.createElement('div');
    el.className = 'device-entry fade';
    const name = document.createElement('span');
    name.className = 'dev-name'; name.textContent = d.name;
    const ip   = document.createElement('span');
    ip.className   = 'dev-ip';   ip.textContent   = d.ip;
    const selBtn = document.createElement('button');
    selBtn.className = 'btn btn-sm'; selBtn.textContent = 'Select';
    selBtn.addEventListener('click', () => {
      document.getElementById('ps5-ip').value        = d.ip;
      document.getElementById('device-select').value = d.id;
      closeDeviceModal(); scheduleSave();
    });
    const delBtn = document.createElement('button');
    delBtn.className = 'btn btn-sm btn-danger'; delBtn.textContent = '✕';
    delBtn.addEventListener('click', async () => {
      state.devices = state.devices.filter(x => x.id !== d.id);
      await saveDevicesToServer();
      renderDeviceDropdown(); renderDeviceListModal();
    });
    el.appendChild(name); el.appendChild(ip);
    el.appendChild(selBtn); el.appendChild(delBtn);
    container.appendChild(el);
  });
}

async function addDevice() {
  const name = document.getElementById('new-device-name').value.trim();
  const ip   = document.getElementById('new-device-ip').value.trim();
  if (!name || !ip) { alert('Enter name and IP!'); return; }
  const id = Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
  state.devices.push({ id, name, ip });
  await saveDevicesToServer();
  renderDeviceDropdown(); renderDeviceListModal();
  document.getElementById('new-device-name').value = '';
  document.getElementById('new-device-ip').value   = '';
  log(`Device '${name}' (${ip}) saved`, 'success');
}
