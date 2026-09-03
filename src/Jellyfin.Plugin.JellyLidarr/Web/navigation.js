(function () {
  'use strict';
  if (window.jellyLidarrNavigationLoaded) return;
  window.jellyLidarrNavigationLoaded = true;
  let pending = false;
  const asset = name => ApiClient.getUrl('JellyLidarr/assets/' + name, {v:'1.0.0.9'});
  function ensureStyle() {
    if(document.getElementById('jellylidarr-portal-css')) return;
    const link=document.createElement('link');link.id='jellylidarr-portal-css';link.rel='stylesheet';link.href=asset('style.css');document.head.append(link);
  }
  async function openPortal() {
    if(pending || document.getElementById('jl-navigation-dialog')) return;
    const existing=document.getElementById('jellylidarr');
    if(existing){existing.scrollIntoView();return;}
    pending=true;
    const overlay=document.createElement('dialog');overlay.id='jl-navigation-dialog';overlay.setAttribute('aria-label','Music Requests');
    Object.assign(overlay.style,{width:'100vw',maxWidth:'100vw',height:'100dvh',maxHeight:'100dvh',padding:'0',margin:'0',border:'0',background:'#101216',color:'#eee'});
    const close=document.createElement('button');close.textContent='Close Music Requests';close.type='button';
    Object.assign(close.style,{position:'sticky',top:'12px',margin:'12px 24px 0 auto',display:'block',zIndex:'2',padding:'10px 16px',border:'1px solid #ffffff30',borderRadius:'6px',background:'#20252c',color:'#fff',cursor:'pointer'});
    close.onclick=()=>overlay.close();overlay.append(close);
    const message=document.createElement('p');message.textContent='Loading Music Requests…';message.style.padding='24px';message.setAttribute('role','status');overlay.append(message);
    overlay.addEventListener('close',()=>{overlay.querySelector('#jellylidarr')?.dispatchEvent(new Event('pagehide'));overlay.remove();pending=false;document.getElementById('jl-header-link')?.focus();});
    document.body.append(overlay);overlay.showModal();
    try {
      ensureStyle();
      const response=await fetch(asset('portal.html'));
      if(!response.ok) throw new Error('Could not load Music Requests.');
      const parsed=new DOMParser().parseFromString(await response.text(),'text/html');
      const root=parsed.getElementById('jellylidarr');
      if(!root) throw new Error('Music Requests page is missing.');
      root.querySelectorAll('script').forEach(x=>x.remove());
      root.className='jl-page';root.removeAttribute('data-role');
      if(!overlay.open)return;
      message.remove();overlay.append(document.importNode(root,true));
      const script=document.createElement('script');script.src=asset('app.js');script.onerror=()=>{message.textContent='Could not load Music Requests script.';overlay.append(message);};overlay.append(script);
    } catch(error) { message.textContent=error.message || 'Could not load Music Requests.'; }
    finally {pending=false;}
  }
  function sync() {
    const user=window.ApiClient?.getCurrentUserId?.();
    const link=document.getElementById('jl-header-link');
    if(!user){link?.remove();document.getElementById('jl-navigation-dialog')?.close();return;}
    if(link)return;
    const header=document.querySelector('.headerRight');
    if(!header)return;
    const button=document.createElement('button');button.id='jl-header-link';button.type='button';button.className='headerButton emby-button';button.title='Music Requests';button.setAttribute('aria-label','Music Requests');
    button.textContent='Music Requests';button.style.fontSize='13px';button.style.padding='8px 12px';button.onclick=openPortal;header.prepend(button);
  }
  let scheduled=false;
  const observer=new MutationObserver(()=>{if(scheduled)return;scheduled=true;requestAnimationFrame(()=>{scheduled=false;sync();});});
  observer.observe(document.documentElement,{childList:true,subtree:true});
  window.addEventListener('hashchange',sync);window.addEventListener('popstate',()=>document.getElementById('jl-navigation-dialog')?.close());
  let attempts=0;const startup=setInterval(()=>{sync();if(document.getElementById('jl-header-link')||++attempts>=60)clearInterval(startup);},500);
  sync();
}());
