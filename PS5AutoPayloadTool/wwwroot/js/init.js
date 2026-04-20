'use strict';

async function init() {
  initTheme();
  document.getElementById('btn-theme').addEventListener('click', toggleTheme);

  // Connection
  document.getElementById('device-select').addEventListener('change', onDeviceSelect);
  document.getElementById('ps5-ip').addEventListener('input', onIpChange);
  document.getElementById('ps5-ip').addEventListener('blur',  validateIp);
  document.getElementById('btn-manage-devices').addEventListener('click', openDeviceModal);

  // Advanced Mode
  document.getElementById('advanced-mode-toggle').addEventListener('change', e => {
    state.advancedMode = e.target.checked;
    applyAdvancedMode();
    scheduleSave();
  });

  // Payloads
  document.getElementById('btn-refresh-payloads').addEventListener('click', refreshPayloads);
  document.getElementById('payload-upload').addEventListener('change', e => uploadPayloads(e.target.files));
  document.getElementById('payload-search').addEventListener('input', e => {
    state.payloadSearch = e.target.value;
    renderPayloads();
  });
  document.getElementById('payload-select-all').addEventListener('change', e => {
    const checked  = e.target.checked;
    if (!state.selectedPayloads) state.selectedPayloads = new Set();
    const filtered = getFilteredPayloads();
    if (checked) filtered.forEach(p => state.selectedPayloads.add(p.name));
    else         state.selectedPayloads.clear();
    renderPayloads();
  });
  document.getElementById('btn-bulk-deselect').addEventListener('click', () => {
    state.selectedPayloads.clear(); renderPayloads();
  });
  document.getElementById('btn-bulk-delete').addEventListener('click', bulkDeleteSelected);

  // Builder panels
  document.getElementById('btn-add-payload').addEventListener('click', () => builderTogglePanel('payload'));
  document.getElementById('btn-add-delay').addEventListener('click',   () => builderTogglePanel('delay'));
  document.getElementById('btn-add-wait').addEventListener('click',    () => builderTogglePanel('wait'));
  document.getElementById('builder-payload-search').addEventListener('input', builderUpdatePayloadDropdown);
  document.getElementById('panel-payload-select').addEventListener('change', builderAddPayloadStepFromSelect);
  document.getElementById('btn-panel-payload-ok').addEventListener('click', builderAddPayloadStep);
  document.getElementById('btn-panel-payload-x').addEventListener('click',  () => builderTogglePanel('payload'));
  document.getElementById('btn-panel-delay-ok').addEventListener('click', () => {
    const ms = parseInt(document.getElementById('panel-delay-ms').value, 10);
    if (ms > 0) { builderAddDelayStep(ms); document.getElementById('panel-delay-ms').value = ''; }
    else showToast('Enter valid milliseconds!');
  });
  document.getElementById('btn-panel-delay-x').addEventListener('click',  () => builderTogglePanel('delay'));
  document.querySelectorAll('.delay-preset').forEach(btn =>
    btn.addEventListener('click', () => builderAddDelayStep(parseInt(btn.dataset.ms, 10))));
  document.getElementById('btn-panel-wait-ok').addEventListener('click', builderAddWaitStep);
  document.getElementById('btn-panel-wait-x').addEventListener('click',  () => builderTogglePanel('wait'));

  // Builder save / run
  document.getElementById('btn-builder-save').addEventListener('click', builderSave);
  document.getElementById('btn-builder-run').addEventListener('click', () => {
    if (state.execState === 'running' || state.execState === 'paused') stopAutoload();
    else builderRunDirect();
  });
  document.getElementById('builder-profile-name').addEventListener('input', scheduleSave);

  // Sources
  document.getElementById('btn-add-source').addEventListener('click', toggleAddSourcePanel);
  document.getElementById('btn-source-fetch').addEventListener('click', addSource);
  document.getElementById('btn-source-import').addEventListener('click', importSelected);
  document.getElementById('btn-check-all-updates').addEventListener('click', checkAllUpdates);
  document.getElementById('btn-update-all').addEventListener('click', updateAll);
  document.getElementById('source-repo-input').addEventListener('keydown', e => {
    if (e.key === 'Enter') addSource();
  });

  // Profiles
  document.getElementById('btn-refresh-profiles').addEventListener('click', refreshProfiles);

  // Port check
  document.getElementById('btn-check-once').addEventListener('click', checkPortOnce);
  document.getElementById('btn-wait-port').addEventListener('click',  waitForPort);

  // Log
  document.getElementById('btn-clear-log').addEventListener('click', clearLog);

  // Device modal
  document.getElementById('btn-add-device').addEventListener('click', addDevice);
  document.getElementById('btn-close-device-modal').addEventListener('click', closeDeviceModal);
  document.getElementById('device-modal').addEventListener('click', e => {
    if (e.target === e.currentTarget) closeDeviceModal();
  });
  document.getElementById('new-device-ip').addEventListener('keydown', e => {
    if (e.key === 'Enter') addDevice();
  });

  // Boot sequence
  await loadDevicesFromServer();
  await loadPersistedState();
  await refreshSources();
  updateSourcesSummary();
  try {
    const cfg = await api(`${BASE}/api/config`);
    const ip  = document.getElementById('ps5-ip');
    if (cfg.ps5_ip && !ip.value) { ip.value = cfg.ps5_ip; onIpChange(); }
  } catch (_) {}
  await loadVersion();
  renderPayloadFilters();
  await refreshPayloads();
  updatePayloadsSummary();
  updateBuilderSummary();
  await refreshProfiles();
  updateProfilesSummary();
  initCollapsible();
  connectWS();
  if (state.sources.length) checkAllUpdates();
}

document.addEventListener('DOMContentLoaded', init);
