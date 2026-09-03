// Local-only UI fixture server. Does not connect to Jellyfin, Lidarr, or real users.
import http from 'node:http';
import {readFile} from 'node:fs/promises';
const web = new URL('../src/Jellyfin.Plugin.JellyLidarr/Web/', import.meta.url);
let config={lidarrUrl:'http://lidarr.test:8686',hasApiKey:true,lidarrApiKey:null,rootFolderId:1,qualityProfileId:2,metadataProfileId:3,monitorMode:'all',pollingSeconds:60,importTimeoutHours:24,userRoles:{alice:'Requester',bob:'Viewer'}};
const options={rootFolders:[{id:1,name:'/music'},{id:4,name:'/archive/music'}],qualityProfiles:[{id:2,name:'Lossless'},{id:5,name:'Any'}],metadataProfiles:[{id:3,name:'Standard'},{id:6,name:'Studio albums only'}]};
let requests=[{id:1,name:'Discovery',artistName:'Daft Punk',kind:'Album',state:'Downloading',userName:'Alice',failureReason:null},{id:2,name:'Kind of Blue',artistName:'Miles Davis',kind:'Album',state:'Available',userName:'Alice',failureReason:null}];
const bridge=`<style>body{margin:0;background:#101216;color:#eee;font:15px system-ui}a{color:#00a4dc}</style><div style="padding:8px 20px;background:#262d38;font:12px system-ui">LOCAL TEST DATA ONLY · <a href="/admin">Settings</a> · <a href="/portal">Music Requests</a></div><script>window.ApiClient={getUrl:(path,params)=>{const u=new URL('/'+path.replace(/^\\//,''),location.origin);Object.entries(params||{}).forEach(([k,v])=>u.searchParams.set(k,v));return u.href},ajax:async o=>{const r=await fetch(o.url,{method:o.type,headers:{'Content-Type':'application/json'},body:o.data});if(!r.ok)throw new Error(await r.text());return o.dataType==='json'?r.json():r}};window.Dashboard={navigate:p=>location.assign(p.includes('Portal')?'/portal':'/admin')};</script>`;
http.createServer(async(req,res)=>{
  const url=new URL(req.url,'http://127.0.0.1');
  function json(value){res.setHeader('Content-Type','application/json');res.end(JSON.stringify(value));}
  try{
    if(url.pathname==='/web/'){
      res.setHeader('Content-Type','text/html; charset=utf-8');
      res.end('<!doctype html><html><head><meta charset="utf-8"><title>Jellyfin header test</title></head><body>'+bridge+'<script>ApiClient.getCurrentUserId=()=>"alice";</script><header style="display:flex;padding:20px;border-bottom:1px solid #333;justify-content:space-between"><b>Jellyfin</b><div class="headerRight"></div></header><main style="padding:28px"><h1>Home</h1><p>Non-administrator fixture: Alice, Requester role.</p></main><script src="/JellyLidarr/assets/navigation.js"></script></body></html>');return;
    }
    if(['/admin','/portal'].includes(url.pathname)){
      const html=await readFile(new URL(url.pathname==='/admin'?'admin.html':'portal.html',web),'utf8');
      res.setHeader('Content-Type','text/html; charset=utf-8');res.end('<!doctype html><html><head><meta charset="utf-8"><title>JellyLidarr UI test</title></head><body>'+bridge+html.match(/<body[^>]*>([\s\S]*)<\/body>/i)[1]+'</body></html>');return;
    }
    if(url.pathname.startsWith('/JellyLidarr/assets/')){
      const name=url.pathname.split('/').pop();if(!['admin.css','admin.js','style.css','app.js','navigation.js','portal.html'].includes(name))throw new Error('Unknown asset');
      res.setHeader('Content-Type',name.endsWith('.css')?'text/css':name.endsWith('.html')?'text/html; charset=utf-8':'text/javascript');res.end(await readFile(new URL(name,web)));return;
    }
    if(url.pathname==='/JellyLidarr/settings' && req.method==='PUT'){
      let body='';for await(const chunk of req)body+=chunk;
      const input=JSON.parse(body);config={...input,hasApiKey:true,lidarrApiKey:null};json({success:true});return;
    }
    if(url.pathname==='/JellyLidarr/settings')return json(config);
    if(url.pathname==='/JellyLidarr/settings/options')return json(options);
    if(url.pathname==='/JellyLidarr/settings/users')return json([{id:'admin',name:'Administrator',isAdministrator:true},{id:'alice',name:'Alice',isAdministrator:false},{id:'bob',name:'Bob',isAdministrator:false}]);
    if(url.pathname==='/JellyLidarr/me')return json({id:'alice',name:'Alice',role:'Requester',isAdministrator:false});
    if(url.pathname==='/JellyLidarr/requests')return json(requests);
    if(url.pathname==='/JellyLidarr/search')return json([{kind:'Album',musicBrainzId:'test-a',name:'Discovery',artistName:'Daft Punk',available:false,requestId:1,requestState:'Downloading'},{kind:'Album',musicBrainzId:'test-b',name:'Random Access Memories',artistName:'Daft Punk',available:true,jellyfinItemId:'test-item'},{kind:'Album',musicBrainzId:'test-c',name:'Homework',artistName:'Daft Punk',available:false}]);
    res.writeHead(404);res.end('Not found');
  }catch(error){res.writeHead(500);res.end(error.message);}
}).listen(18765,'127.0.0.1',()=>console.log('UI fixture: http://127.0.0.1:18765/admin'));
