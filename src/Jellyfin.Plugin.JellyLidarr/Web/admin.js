(function () {
  'use strict';
  const page = document.getElementById('jellylidarr-config');
  if (!page || page.dataset.ready) return;
  page.dataset.ready = 'true';
  const $ = id => page.querySelector('#' + id);
  let config = null, optionsReady = false, working = false;
  function status(text, error = false) { $('jla-status').textContent = text; $('jla-status').dataset.error = String(error); }
  function buttons() {
    $('jla-connect').disabled = working || !config;
    $('jla-save').disabled = working || !config || !optionsReady;
    $('jla-save-hint').textContent = optionsReady ? 'Defaults apply to all new requests.' : 'Load connection options before saving.';
  }
  async function call(path, body) {
    const response = await ApiClient.ajax({url: ApiClient.getUrl('JellyLidarr/settings' + path), type: body ? 'PUT' : 'GET', dataType:'json', contentType:'application/json', ...(body ? {data:JSON.stringify(body)} : {})});
    if (response && typeof response.json === 'function') {
      if (!response.ok) throw new Error(await response.text() || 'Request failed');
      return response.status === 204 ? null : response.json();
    }
    return response;
  }
  function fillOptions(id, items, selected) {
    if (!Array.isArray(items)) throw new Error('Invalid options response. Check that JellyLidarr 1.0.0.8 is loaded after restarting Jellyfin.');
    const select = $(id);
    select.replaceChildren(new Option(items.length ? 'Select an option' : 'No options configured in Lidarr', ''));
    items.forEach(item => {
      if (item.id == null || typeof item.name !== 'string') throw new Error('Lidarr option is missing its ID or name.');
      select.append(new Option(item.name, String(item.id)));
    });
    select.disabled = !items.length;
    select.value = items.some(x => Number(x.id) === Number(selected)) ? String(selected) : items.length === 1 ? String(items[0].id) : '';
  }
  async function loadOptions() {
    optionsReady = false;
    const data = await call('/options');
    fillOptions('jla-root', data?.rootFolders, config.rootFolderId);
    fillOptions('jla-quality', data?.qualityProfiles, config.qualityProfileId);
    fillOptions('jla-meta', data?.metadataProfiles, config.metadataProfileId);
    if (!data.rootFolders.length || !data.qualityProfiles.length || !data.metadataProfiles.length) throw new Error('Connected, but Lidarr has no root folders or profiles. Configure the missing options in Lidarr first.');
    optionsReady = true;
    $('jla-connection-status').textContent = `${data.rootFolders.length} folders · ${data.qualityProfiles.length} quality profiles · ${data.metadataProfiles.length} metadata profiles loaded`;
  }
  function renderUsers(users) {
    if (!Array.isArray(users)) throw new Error('Invalid users response.');
    $('jla-users').replaceChildren();
    users.filter(x => !x.isAdministrator).forEach(user => {
      if (!user.id || !user.name) throw new Error('User response is missing its ID or name.');
      const row = document.createElement('div'); row.className='jla-user';
      const label=document.createElement('label'); const select=document.createElement('select');
      select.id='jla-role-' + user.id; select.dataset.user=user.id;
      label.htmlFor=select.id; label.textContent=user.name;
      [['Viewer','Viewer'],['Requester','Requester'],['TrustedRequester','Trusted requester'],['Approver','Approver']].forEach(([value,name]) => select.append(new Option(name,value)));
      select.value=config.userRoles?.[user.id] || 'Viewer';
      row.append(label,select); $('jla-users').append(row);
    });
    if (!$('jla-users').children.length) $('jla-users').textContent='No non-administrator users found.';
  }
  function payload() {
    if (!config) throw new Error('Configuration has not loaded.');
    const userRoles={}; page.querySelectorAll('[data-user]').forEach(x=>userRoles[x.dataset.user]=x.value);
    return {lidarrUrl:$('jla-url').value.trim(),lidarrApiKey:$('jla-key').value.trim()||null,hasApiKey:config.hasApiKey,
      rootFolderId:Number($('jla-root').value)||config.rootFolderId,qualityProfileId:Number($('jla-quality').value)||config.qualityProfileId,
      metadataProfileId:Number($('jla-meta').value)||config.metadataProfileId,monitorMode:$('jla-monitor').value,
      pollingSeconds:Number($('jla-poll').value),importTimeoutHours:Number($('jla-timeout').value),userRoles};
  }
  async function save() {
    const body=payload(); await call('',body);
    config={...body,hasApiKey:config.hasApiKey||Boolean(body.lidarrApiKey),lidarrApiKey:null}; $('jla-key').value='';
    $('jla-key').placeholder='Saved — leave blank to keep it';
  }
  async function run(action) {
    if(working) return;
    working=true; buttons();
    try { await action(); } catch(error) {
      let message=error?.message || 'Request failed. Check Jellyfin server logs.';
      if(typeof error?.text==='function') message=await error.text() || message;
      status(message,true);
    } finally { working=false; buttons(); }
  }
  $('jla-connect').onclick=()=>run(async()=>{
    if(!$('jla-url').reportValidity()) return;
    status('Testing connection and loading Lidarr options…');
    await save(); await loadOptions(); status('Connected. Select the download defaults and save configuration.');
  });
  $('jla-form').onsubmit=event=>{event.preventDefault();run(async()=>{
    if(!optionsReady) throw new Error('Test the connection before saving.');
    if(!['jla-root','jla-quality','jla-meta'].every(id=>$(id).value)) throw new Error('Select a root folder, quality profile, and metadata profile.');
    await save(); status('Configuration saved.');
  });};
  $('jla-open').onclick=()=>window.Dashboard?.navigate ? Dashboard.navigate('configurationpage?name=JellyLidarrPortal') : location.assign('configurationpage?name=JellyLidarrPortal');
  page.querySelectorAll('[data-section]').forEach(button=>button.onclick=()=>{
    page.querySelectorAll('[data-section]').forEach(x=>x.setAttribute('aria-pressed',String(x===button)));
    page.querySelectorAll('[data-panel]').forEach(x=>x.hidden=x.dataset.panel!==button.dataset.section);
  });
  run(async()=>{
    const [settings,users]=await Promise.all([call(''),call('/users')]);
    if(typeof settings?.lidarrUrl!=='string') throw new Error('Invalid settings response. Restart Jellyfin to load JellyLidarr 1.0.0.8.');
    config=settings;
    $('jla-url').value=config.lidarrUrl; $('jla-poll').value=config.pollingSeconds; $('jla-timeout').value=config.importTimeoutHours;
    $('jla-monitor').value=config.monitorMode; if(config.hasApiKey) $('jla-key').placeholder='Saved — leave blank to keep it';
    renderUsers(users);
    if(config.hasApiKey) {await loadOptions();status('Configuration loaded. Lidarr is connected.');}
    else status('Enter your Lidarr address and API key, then test the connection.');
  });
}());
