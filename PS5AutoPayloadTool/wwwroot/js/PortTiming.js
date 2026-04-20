'use strict';

// ── Port timing (stubs — server stores no timing data on Windows) ─
function recordPortTiming(port, durationMs) {
  // Best-effort, non-blocking
  fetch(`${BASE}/api/timing/record`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ port, duration_ms: durationMs }),
  }).catch(() => {});
}

async function loadTimingStats() {
  // No-op in Windows version — timing panel not shown
}

async function clearTimingStats() {
  await api(`${BASE}/api/timing`, { method: 'DELETE' });
  showToast('Timing data cleared');
}
