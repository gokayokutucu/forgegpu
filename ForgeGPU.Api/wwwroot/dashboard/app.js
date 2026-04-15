const BAND_ROWS = [
  { band: 'W1_2', topic: 'forgegpu.jobs.w1_2' },
  { band: 'W3_5', topic: 'forgegpu.jobs.w3_5' },
  { band: 'W6_10', topic: 'forgegpu.jobs.w6_10' },
  { band: 'W11_20', topic: 'forgegpu.jobs.w11_20' },
  { band: 'W21_40', topic: 'forgegpu.jobs.w21_40' },
  { band: 'W41Plus', topic: 'forgegpu.jobs.w41_plus' }
];

const REFRESH_MS = 5000;
const summaryGrid = document.getElementById('summaryGrid');
const bandTableBody = document.getElementById('bandTableBody');
const machineGrid = document.getElementById('machineGrid');
const heatmapGrid = document.getElementById('heatmapGrid');
const eventsTableBody = document.getElementById('eventsTableBody');
const deferredSummary = document.getElementById('deferredSummary');
const deferredTableBody = document.getElementById('deferredTableBody');
const lastUpdated = document.getElementById('lastUpdated');
const transportStatus = document.getElementById('transportStatus');
document.getElementById('refreshButton').addEventListener('click', refresh);
let signalRConnection = null;

function number(value) {
  return value ?? 0;
}

function fmtTime(value) {
  if (!value) return '--';
  return new Date(value).toLocaleTimeString();
}

function statusClass(value) {
  const text = String(value || '').toLowerCase();
  if (text.includes('live') || text.includes('idle') || text.includes('available')) return 'good';
  if (text.includes('busy') || text.includes('starting')) return 'warn';
  return 'bad';
}

function heatColor(percent) {
  const p = Math.max(0, Math.min(100, Number(percent) || 0));
  const hue = Math.max(0, 120 - Math.round(p * 1.2));
  return `hsl(${hue} 76% 62%)`;
}

function card(label, value, detail = '') {
  return `<div class="metric-card"><div class="label">${label}</div><div class="value">${value}</div>${detail ? `<div class="small">${detail}</div>` : ''}</div>`;
}

async function loadJson(url) {
  const response = await fetch(url, { cache: 'no-store' });
  if (!response.ok) {
    throw new Error(`${url} returned ${response.status}`);
  }
  return response.json();
}

function setTransportStatus(label, tone) {
  transportStatus.textContent = label;
  transportStatus.className = `chip ${tone}`;
}

function renderSummary(metrics) {
  const jobs = metrics.jobs;
  const scheduler = metrics.scheduler;
  const batching = metrics.batching;
  const workers = metrics.workers;

  summaryGrid.innerHTML = [
    card('Accepted Jobs', number(jobs.totalAccepted)),
    card('Completed Jobs', number(jobs.totalCompleted)),
    card('Deferred Jobs', number(jobs.totalDeferred), `Current pending: ${number(metrics.queue.deferredPendingCount)}`),
    card('Successful Dispatches', number(scheduler.successfulDispatches)),
    card('Scheduler Deferrals', number(scheduler.deferrals)),
    card('Total Batches', number(batching.totalBatchesFormed), `Avg size: ${batching.averageBatchSize}`),
    card('Retries', number(jobs.totalRetried), `Dead letters: ${number(jobs.deadLetterCount)}`),
    card('Job Utilization', `${workers.jobUtilizationPercent}%`, `${workers.activeJobs}/${workers.jobCapacity} active jobs`),
    card('VRAM Utilization', `${workers.vramUtilizationPercent}%`, `${workers.reservedVramMb}/${workers.totalVramMb} MB reserved`),
    card('Queue Wait (avg)', `${metrics.latency.averageQueueWaitMs} ms`),
    card('Execution (avg)', `${metrics.latency.averageExecutionMs} ms`),
    card('Total Latency (avg)', `${metrics.latency.averageTotalLatencyMs} ms`)
  ].join('');
}

function renderBands(metrics) {
  const accepted = metrics.jobs.acceptedByWeightBand || {};
  const completed = metrics.jobs.completedByWeightBand || {};
  const deferred = metrics.jobs.deferredByWeightBand || {};
  const dispatched = metrics.scheduler.dispatchesByWeightBand || {};
  const credits = metrics.scheduler.bandCredits || {};
  const buffers = metrics.queue.currentBandBufferDepths || {};
  const publishedByTopic = metrics.queue.ingressPublishedByTopic || {};
  const consumedByTopic = metrics.queue.ingressConsumedByTopic || {};
  const lagByTopic = metrics.queue.ingressLagByTopic || {};

  bandTableBody.innerHTML = BAND_ROWS.map(({ band, topic }) => `
    <tr>
      <td>${band}</td>
      <td>${number(buffers[band])}</td>
      <td>${number(credits[band])}</td>
      <td>${number(accepted[band])}</td>
      <td>${number(dispatched[band])}</td>
      <td>${number(completed[band])}</td>
      <td>${number(deferred[band])}</td>
      <td class="code">${topic}</td>
      <td>${number(publishedByTopic[topic])}</td>
      <td>${number(consumedByTopic[topic])}</td>
      <td>${number(lagByTopic[topic])}</td>
    </tr>`).join('');
}

function renderMachines(machineSnapshot) {
  const machines = machineSnapshot.machines || [];
  machineGrid.innerHTML = machines.map(machine => {
    const meta = machine.durable;
    const live = machine.live;
    const availability = machine.availability;

    return `
      <article class="machine-card">
        <h3>${meta.name}</h3>
        <div class="machine-meta code">${machine.machineId}</div>
        <div class="status-row">
          <span class="chip ${meta.enabled ? 'good' : 'bad'}">${meta.enabled ? 'Enabled' : 'Disabled'}</span>
          <span class="chip ${statusClass(availability.livenessState)}">${availability.livenessState}</span>
          <span class="chip ${statusClass(live.runtimeStatus)}">${live.runtimeStatus}</span>
          <span class="chip ${statusClass(live.actorStatus)}">${live.actorStatus}</span>
        </div>
        <div class="metric-line">
          <div class="label"><span>Capacity</span><span>${live.usedCapacityUnits}/${meta.totalCapacityUnits}</span></div>
          <div class="bar"><span style="width:${live.capacityUtilizationPercent}%"></span></div>
        </div>
        <div class="metric-line">
          <div class="label"><span>GPU VRAM</span><span>${live.reservedVramMb}/${meta.gpuVramMb} MB</span></div>
          <div class="bar"><span style="width:${live.gpuVramUtilizationPercent}%"></span></div>
        </div>
        <div class="metric-line">
          <div class="label"><span>Parallel Workers</span><span>${live.activeJobCount}/${meta.maxParallelWorkers}</span></div>
          <div class="bar"><span style="width:${live.parallelUtilizationPercent}%"></span></div>
        </div>
        <div class="kv small">
          <div><strong>CPU</strong><br>${meta.cpuScore}</div>
          <div><strong>RAM</strong><br>${meta.ramMb} MB</div>
          <div><strong>Batch</strong><br>${live.currentBatchSize || 0}</div>
          <div><strong>Heartbeat</strong><br>${fmtTime(live.lastHeartbeatUtc)}</div>
        </div>
        <div class="chip-row compact-top">
          ${(meta.supportedModels || []).map(model => `<span class="chip">${model}</span>`).join('')}
        </div>
        <div class="small"><strong>Running jobs</strong>: ${(live.runningJobIds || []).length ? live.runningJobIds.map(id => `<span class="code">${id.slice(0, 8)}</span>`).join(', ') : 'none'}</div>
      </article>`;
  }).join('');
}

function renderHeatmap(machineSnapshot) {
  const machines = machineSnapshot.machines || [];
  const rows = [
    '<div class="heatmap-header"></div>',
    '<div class="heatmap-header">Capacity</div>',
    '<div class="heatmap-header">VRAM</div>',
    '<div class="heatmap-header">Active Load</div>'
  ];

  machines.forEach(machine => {
    const live = machine.live;
    rows.push(`<div class="heatmap-header heatmap-label code">${machine.machineId}</div>`);
    rows.push(`<div class="heatmap-cell" style="background:${heatColor(live.capacityUtilizationPercent)}">${live.capacityUtilizationPercent}%</div>`);
    rows.push(`<div class="heatmap-cell" style="background:${heatColor(live.gpuVramUtilizationPercent)}">${live.gpuVramUtilizationPercent}%</div>`);
    rows.push(`<div class="heatmap-cell" style="background:${heatColor(live.parallelUtilizationPercent)}">${live.parallelUtilizationPercent}%</div>`);
  });

  heatmapGrid.innerHTML = rows.join('');
}

function renderEvents(events) {
  const interestingKinds = new Set(['IngressConsumed', 'BandSelected', 'JobDispatched', 'JobDeferred', 'BatchFormed', 'RetryScheduled', 'DeadLettered', 'MachineLivenessChanged']);
  const rows = (events || []).filter(event => interestingKinds.has(event.kind)).slice(0, 40);
  eventsTableBody.innerHTML = rows.map(event => `
    <tr>
      <td>${fmtTime(event.timestampUtc)}</td>
      <td>${event.kind}</td>
      <td class="code">${event.jobId ? event.jobId.slice(0, 8) : '--'}</td>
      <td>${event.weightBand || '--'}</td>
      <td>${event.exactWeight ?? '--'}</td>
      <td>${event.machineId || '--'}</td>
      <td>${event.creditBefore != null || event.creditAfter != null ? `${event.creditBefore ?? 0} → ${event.creditAfter ?? 0}` : '--'}</td>
      <td>${event.reason || '--'}</td>
      <td>${event.summary}</td>
    </tr>`).join('');
}

function renderDeferred(metrics, events) {
  const pendingReasons = metrics.queue.pendingReasons || {};
  const deferredEvents = (events || []).filter(event => event.kind === 'JobDeferred').slice(0, 20);

  deferredSummary.innerHTML = `
    <div class="summary-grid">
      <div class="mini-card"><div class="label">Current Deferred Count</div><div class="value">${number(metrics.queue.deferredPendingCount)}</div></div>
      <div class="mini-card"><div class="label">Deferral Reasons</div><div class="small">${Object.keys(pendingReasons).length ? Object.entries(pendingReasons).map(([key, value]) => `${key}: ${value}`).join('<br>') : 'No deferred jobs currently pending.'}</div></div>
    </div>`;

  deferredTableBody.innerHTML = deferredEvents.map(event => `
    <tr>
      <td>${fmtTime(event.timestampUtc)}</td>
      <td class="code">${event.jobId ? event.jobId.slice(0, 8) : '--'}</td>
      <td>${event.weightBand || '--'}</td>
      <td>${event.reason || '--'}</td>
      <td>${event.summary}</td>
    </tr>`).join('');
}

function applySnapshot(metrics, machines, events, sourceLabel) {
  renderSummary(metrics);
  renderBands(metrics);
  renderMachines(machines);
  renderHeatmap(machines);
  renderEvents(events);
  renderDeferred(metrics, events);
  lastUpdated.textContent = `Last updated: ${new Date().toLocaleTimeString()} (${sourceLabel})`;
}

async function refresh() {
  try {
    const [metrics, machines, events] = await Promise.all([
      loadJson('/metrics'),
      loadJson('/machines'),
      loadJson('/events/recent?limit=120')
    ]);

    applySnapshot(metrics, machines, events, 'poll');
  } catch (error) {
    console.error(error);
    lastUpdated.textContent = `Last updated: error (${error.message})`;
  }
}

async function connectSignalR() {
  if (!window.signalR) {
    setTransportStatus('Polling fallback', 'warn');
    return;
  }

  signalRConnection = new window.signalR.HubConnectionBuilder()
    .withUrl('/hubs/dashboard')
    .withAutomaticReconnect()
    .build();

  signalRConnection.on('dashboardUpdate', update => {
    if (!update) {
      return;
    }

    setTransportStatus('Live via SignalR', 'good');
    applySnapshot(update.metrics, update.machines, update.events || [], 'signalr');
  });

  signalRConnection.onreconnecting(() => {
    setTransportStatus('Reconnecting...', 'warn');
  });

  signalRConnection.onreconnected(() => {
    setTransportStatus('Live via SignalR', 'good');
  });

  signalRConnection.onclose(() => {
    setTransportStatus('Polling fallback', 'warn');
  });

  try {
    await signalRConnection.start();
    setTransportStatus('Live via SignalR', 'good');
  } catch (error) {
    console.error(error);
    setTransportStatus('Polling fallback', 'warn');
  }
}

refresh();
connectSignalR();
setInterval(refresh, REFRESH_MS);
