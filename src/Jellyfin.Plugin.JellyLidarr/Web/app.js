(function () {
  'use strict';
  const root = document.getElementById('jellylidarr');
  if (!root || root.dataset.ready) return;
  root.dataset.ready = 'true';
  const $ = (id) => document.getElementById(id);
  let me;
  let pendingArtist;

  function url(path, params) {
    if (window.ApiClient && ApiClient.getUrl) return ApiClient.getUrl('JellyLidarr/' + path, params);
    const target = new URL('JellyLidarr/' + path, location.origin + '/');
    Object.entries(params || {}).forEach(([key, value]) => value !== '' && value != null && target.searchParams.set(key, value));
    return target.toString();
  }
  async function api(path, options, params) {
    const request = { url: url(path, params), type: options?.method || 'GET', dataType: 'json', contentType: 'application/json' };
    if (options?.body) request.data = options.body;
    try {
      if (window.ApiClient && ApiClient.ajax) return await ApiClient.ajax(request);
      const response = await fetch(request.url, { method: request.type, headers: { 'Content-Type': 'application/json' }, body: request.data });
      if (!response.ok) throw new Error(await response.text());
      return response.status === 204 ? null : response.json();
    } catch (error) { throw new Error(typeof error === 'string' ? error : error.message || 'Request failed'); }
  }
  function el(tag, className, text) { const node = document.createElement(tag); if (className) node.className = className; if (text != null) node.textContent = text; return node; }
  function toast(message) { const box = $('jl-toast'); box.textContent = message; box.classList.add('show'); setTimeout(() => box.classList.remove('show'), 3200); }
  function status(item) { return item.partial ? 'Partially available' : item.available ? 'Available' : item.requestState || 'Request'; }
  function canRequest() { return me && (me.isAdministrator || ['Requester', 'TrustedRequester', 'Approver'].includes(me.role)); }

  function resultCard(item) {
    const card = el('article', 'jl-card'); const cover = el('div', 'jl-cover');
    if (item.imageUrl) cover.style.backgroundImage = `url("${encodeURI(item.imageUrl).replaceAll('"', '%22')}")`;
    const body = el('div', 'jl-body'); body.append(el('h3', '', item.name), el('p', '', item.artistName || item.kind));
    const meta = el('div', 'jl-meta'); const pill = el('span', 'jl-pill ' + (item.available ? 'available' : item.requestState === 'Failed' ? 'failed' : ''), status(item)); meta.append(pill);
    if (item.available && item.jellyfinItemId) { const open = el('button', 'jl-ghost', 'Open'); open.onclick = () => location.hash = `#!/details?id=${item.jellyfinItemId}`; meta.append(open); }
    if ((!item.available || item.partial) && !item.requestId && canRequest()) { const request = el('button', '', item.partial ? 'Request missing' : 'Request'); request.onclick = () => requestItem(item, request); meta.append(request); }
    body.append(meta); card.append(cover, body); return card;
  }
  async function requestItem(item, button) {
    if (item.kind === 'Artist') {
      pendingArtist = { item, button }; $('jl-confirm-text').textContent = `${item.name} may add and monitor multiple albums using the administrator’s configured Lidarr profile.`; $('jl-confirm').showModal(); return;
    }
    await submitRequest(item, button);
  }
  async function submitRequest(item, button) {
    button.disabled = true;
    try { await api('requests', { method: 'POST', body: JSON.stringify({ kind: item.kind, musicBrainzId: item.musicBrainzId, name: item.name, artistName: item.artistName }) }); toast('Request submitted'); await search(); }
    catch (error) { toast(error.message); button.disabled = false; }
  }
  async function search(event) {
    event?.preventDefault(); const term = $('jl-term').value.trim(); if (term.length < 2) return;
    const output = $('jl-results'); output.replaceChildren(); $('jl-search-status').textContent = 'Searching…';
    try { const results = await api('search', null, { term, type: $('jl-kind').value, limit: 30 }); results.forEach(x => output.append(resultCard(x))); $('jl-search-status').textContent = results.length ? `${results.length} results` : 'No matching music found.'; }
    catch (error) { $('jl-search-status').textContent = error.message; }
  }
  function requestRow(item, approval) {
    const row = el('article', 'jl-row'); const info = el('div'); info.append(el('h3', '', item.name), el('p', '', `${item.artistName || item.kind} · ${item.state}${approval ? ` · requested by ${item.userName}` : ''}${item.failureReason ? ` · ${item.failureReason}` : ''}`)); row.append(info);
    const actions = el('div', 'jl-actions');
    const add = (label, action, ghost, promptReason) => { const b = el('button', ghost ? 'jl-ghost' : '', label); b.onclick = async () => { const reason = promptReason ? prompt('Reason for rejection:') : null; if (promptReason && !reason) return; b.disabled = true; try { await api(`requests/${item.id}/${action}`, { method: 'POST', body: action === 'reject' ? JSON.stringify({ reason }) : undefined }); await loadRequests(); } catch (e) { toast(e.message); b.disabled = false; } }; actions.append(b); };
    if (approval && item.state === 'Pending') { add('Reject', 'reject', true, true); add('Approve', 'approve'); }
    if (approval && item.state === 'Failed') add('Retry', 'retry');
    if (!['Available','Rejected','Cancelled'].includes(item.state)) add('Cancel', 'cancel', true);
    row.append(actions); return row;
  }
  async function loadRequests() {
    const mine = await api('requests'); const mineList = $('jl-mine-list'); mineList.replaceChildren(...mine.map(x => requestRow(x, false))); if (!mine.length) mineList.append(el('p', 'jl-status', 'You have no requests yet.'));
    if (me.isAdministrator || me.role === 'Approver') { const all = await api('requests', null, { all: true }); const queue = all.filter(x => ['Pending','Failed'].includes(x.state)); const list = $('jl-approval-list'); list.replaceChildren(...queue.map(x => requestRow(x, true))); if (!queue.length) list.append(el('p', 'jl-status', 'The queue is clear.')); }
  }
  root.querySelectorAll('[data-tab]').forEach(button => button.onclick = async () => { root.querySelectorAll('[data-tab]').forEach(x => x.classList.toggle('active', x === button)); root.querySelectorAll('.jl-panel').forEach(x => x.hidden = x.id !== `jl-${button.dataset.tab}`); if (button.dataset.tab !== 'discover') await loadRequests(); });
  $('jl-search-form').onsubmit = search; $('jl-refresh').onclick = () => loadRequests().then(() => toast('Statuses refreshed'));
  $('jl-admin').onclick = () => window.Dashboard?.navigate ? Dashboard.navigate('configurationpage?name=jellylidarr-admin') : (location.href = 'configurationpage?name=jellylidarr-admin');
  $('jl-confirm').addEventListener('close', () => { if ($('jl-confirm').returnValue === 'confirm' && pendingArtist) submitRequest(pendingArtist.item, pendingArtist.button); pendingArtist = null; });
  api('me').then(user => { me = user; if (user.isAdministrator || user.role === 'Approver') $('jl-approval-tab').hidden = false; if (user.isAdministrator) $('jl-admin').hidden = false; }).catch(e => toast(e.message));
  const timer = setInterval(() => { if (!document.hidden) loadRequests().catch(() => {}); }, 15000); window.addEventListener('beforeunload', () => clearInterval(timer));
}());
