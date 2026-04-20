'use strict';

// ── Quick Start Bar ───────────────────────────────────────────────
function renderFavorites() {
  const card = document.getElementById('favorites-card');
  const list = document.getElementById('favorites-list');
  card.style.display = '';
  list.innerHTML = '';

  const valid    = state.favorites.filter(f => state.profiles.includes(f));
  if (!valid.length) {
    const empty = document.createElement('div');
    empty.className = 'qs-empty';
    empty.innerHTML =
      '<span class="qs-empty-title">Quick Start is empty</span>' +
      '<span class="qs-empty-sub">Pin a flow to enable one-click execution</span>';
    const goBtn = document.createElement('button');
    goBtn.className = 'btn btn-sm qs-goto-builder';
    goBtn.textContent = '→ Go to Builder';
    goBtn.addEventListener('click', () => {
      document.getElementById('section-builder')?.scrollIntoView({ behavior: 'smooth' });
    });
    empty.appendChild(goBtn);
    list.appendChild(empty);
    return;
  }

  const isActive = state.execState === 'running' || state.execState === 'paused';
  const running  = state.runningProfile;

  valid.forEach(name => {
    const isThisOne  = isActive && running === name;
    const isOtherOne = isActive && running !== name;

    const tile = document.createElement('button');
    tile.className = 'qs-tile' + (isThisOne ? ' qs-tile-stop' : '');
    tile.disabled  = isOtherOne;
    tile.title     = isThisOne
      ? `Stop ${name.replace(/\.txt$/i, '')}`
      : `Run ${name.replace(/\.txt$/i, '')}`;

    const icon  = document.createElement('span');
    icon.className   = 'qs-tile-icon';
    icon.textContent = isThisOne ? '■' : '▶';

    const label = document.createElement('span');
    label.className   = 'qs-tile-label';
    label.textContent = name.replace(/\.txt$/i, '');

    tile.appendChild(icon);
    tile.appendChild(label);
    tile.addEventListener('click', () => {
      if (isThisOne) stopAutoload();
      else           runProfile(name, tile);
    });

    list.appendChild(tile);
  });
}
