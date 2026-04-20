'use strict';

// ── Payload filter tabs ───────────────────────────────────────────
const FILTERS = [
  { key: 'all',  label: 'All' },
  { key: 'favs', label: '⭐ Favorites' },
  { key: 'elf',  label: 'ELF' },
  { key: 'lua',  label: 'LUA' },
];

function renderPayloadFilters() {
  const el = document.getElementById('payload-filters');
  if (!el) return;
  el.innerHTML = '';
  FILTERS.forEach(f => {
    const btn = document.createElement('button');
    btn.className = 'filter-btn' + (state.payloadFilter === f.key ? ' filter-active' : '');
    btn.textContent = f.label;
    btn.addEventListener('click', () => setPayloadFilter(f.key));
    el.appendChild(btn);
  });
}

function setPayloadFilter(key) {
  state.payloadFilter = key;
  renderPayloadFilters();
  renderPayloads();
  scheduleSave();
}

function getFilteredPayloads() {
  let list = state.payloads.slice();

  // Type filter
  if (state.payloadFilter === 'elf')
    list = list.filter(p => p.name.toLowerCase().endsWith('.elf') || p.name.toLowerCase().endsWith('.bin'));
  else if (state.payloadFilter === 'lua')
    list = list.filter(p => p.name.toLowerCase().endsWith('.lua'));
  else if (state.payloadFilter === 'favs')
    list = list.filter(p => state.payloadFavs.includes(p.name));

  // Search
  const q = (state.payloadSearch || '').toLowerCase().trim();
  if (q) {
    const starts = list.filter(p => p.name.toLowerCase().startsWith(q));
    const rest   = list.filter(p => !p.name.toLowerCase().startsWith(q) && p.name.toLowerCase().includes(q));
    list = [...starts, ...rest];
  }

  // Favorites float to top (within current filter result)
  const favs  = list.filter(p => state.payloadFavs.includes(p.name));
  const other = list.filter(p => !state.payloadFavs.includes(p.name));
  return [...favs, ...other];
}
