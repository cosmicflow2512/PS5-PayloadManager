'use strict';

// ── WebSocket ─────────────────────────────────────────────────────
function connectWS() {
  const dot   = document.getElementById('ws-indicator');
  dot.className = 'ws-dot ws-connecting';
  const proto = location.protocol === 'https:' ? 'wss' : 'ws';
  const ws    = new WebSocket(`${proto}://${location.host}${BASE}/ws`);
  state.ws    = ws;

  ws.onopen = () => {
    dot.className   = 'ws-dot ws-online';
    state.wsRetries = 0;
    log('Connected ✓', 'success');
    ws._ping = setInterval(() => {
      if (ws.readyState === 1) ws.send(JSON.stringify({ type: 'ping' }));
    }, 25000);
  };

  ws.onmessage = ({ data }) => {
    try { handleWS(JSON.parse(data)); } catch (_) { /* ignore */ }
  };

  ws.onclose = () => {
    dot.className = 'ws-dot ws-offline';
    clearInterval(ws._ping);
    const delay = Math.min(1000 * Math.pow(1.5, state.wsRetries++), 20000);
    if (state.wsRetries <= 12) setTimeout(connectWS, delay);
    else log('Connection lost. Please reload.', 'error');
  };

  ws.onerror = () => ws.close();
}

function handleWS(msg) {
  if (msg.type === 'pong') return;
  if (msg.type === 'exec_state') {
    state.execState      = msg.state;
    state.runningProfile = msg.profile || '';
    handleExecState(msg.state, msg.profile || '');
    return;
  }
  if (msg.type === 'status') {
    log(msg.message, msg.level || 'info');
    handleBuilderStepStatus(msg);
  }
}
