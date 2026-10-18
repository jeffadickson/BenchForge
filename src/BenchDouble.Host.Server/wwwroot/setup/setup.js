'use strict';
const $ = id => document.getElementById(id);
let config = empty(), saved = null, inventory = null, discoveryAbort = null, reviewed = null;
function empty() { return {schemaVersion:1, controllers:[], instruments:[], roles:[]}; }
function clone(value) { return JSON.parse(JSON.stringify(value)); }
function status(message, error=false) { $('status').textContent=message; $('status').className='notice'+(error?' error':''); }
async function api(path, body, signal) {
  const response=await fetch('/api/v1/setup/'+path, {method:body===undefined?'GET':'POST', headers:body===undefined?{}:{'Content-Type':'application/json'}, body:body===undefined?undefined:JSON.stringify(body),signal});
  const value=await response.json(); if(!response.ok){const error=Error(value.error||JSON.stringify(value));error.httpStatus=response.status;throw error;} return value;
}
function guard(action) { return async()=>{try{await action();}catch(error){status(error.name==='AbortError'?'Discovery cancelled; saved configuration unchanged.':error.message,true);}}; }
function node(tag,text,parent) {const result=document.createElement(tag); if(text!==undefined)result.textContent=text; if(parent)parent.append(result);return result;}
function button(text, action, parent) {const result=node('button',text,parent);result.onclick=guard(action);return result;}
function input(label,value,action,parent) {const wrap=node('label',label,parent), result=node('input',undefined,wrap);result.value=value||'';result.onchange=()=>{action(result.value);changed();};return result;}
function changed(){ reviewed=null;$('reviewConfirmed').checked=false;$('reviewPanel').hidden=true;$('validation').replaceChildren();$('json').value=JSON.stringify(config,null,2); }
function render(){
 $('controllers').replaceChildren();
 for(const c of config.controllers){const row=node('div',undefined,$('controllers'));row.className='row';input('Controller name',c.id,value=>{const old=c.id;c.id=value;for(const i of config.instruments)for(const r of i.connections)if(r.controllerId===old)r.controllerId=value;render();},row);node('span',c.provider+' / '+c.address,row);
 const label=node('label',undefined,row);label.className='inline';const check=node('input',undefined,label);check.type='checkbox';check.checked=!!inventory&&c.confirmedDiscoveryId===inventory.id;check.disabled=!inventory;node('span',c.stableId?'Stable identity: '+c.stableId:'I identified this adapter',label);check.onchange=()=>{c.confirmedDiscoveryId=check.checked?inventory.id:null;changed();};}
 $('instruments').replaceChildren();
 for(const i of config.instruments){const item=node('div',undefined,$('instruments'));item.className='instrument';const row=node('div',undefined,item);row.className='row';node('strong',i.id,row);input('Friendly name',i.name,value=>i.name=value,row);button('Remove instrument',()=>{config.instruments=config.instruments.filter(x=>x!==i);config.roles=config.roles.filter(x=>x.instrumentId!==i.id);changed();render();},row);
 for(const route of i.connections){const r=node('div',undefined,item);r.className='route';const label=node('label',undefined,r);const radio=node('input',undefined,label);radio.type='radio';radio.name='route-'+i.id;radio.checked=i.selectedConnection===route.id;radio.onchange=()=>{i.selectedConnection=route.id;changed();};node('span',route.id+' · '+route.transport+' · '+(route.controllerId?route.controllerId+' / ':'')+route.address+(route.driverBinding?' · IVI: '+route.driverBinding.module.name+' / '+route.driverBinding.session+' (offline)':''),label);button('Remove',()=>{i.connections=i.connections.filter(x=>x!==route);if(i.selectedConnection===route.id)i.selectedConnection=i.connections[0]?.id||'';changed();render();},r);}
 if(i.connections.length>1){const label=node('label',undefined,item);label.className='inline';const check=node('input',undefined,label);check.type='checkbox';check.checked=i.associationConfirmed;node('span','I confirm these connections belong to the same physical instrument',label);check.onchange=()=>{i.associationConfirmed=check.checked;changed();};}
 const details=node('details',undefined,item);node('summary','Optional expected identity checks',details);const fields=node('div',undefined,details);fields.className='row';for(const field of ['manufacturer','model','serial'])input(field,i.expectedIdentity?.[field],value=>{i.expectedIdentity??={};i.expectedIdentity[field]=value||null;},fields);
 }
 $('roles').replaceChildren();for(const role of config.roles){const row=node('div',undefined,$('roles'));row.className='route';node('span',role.role+' → '+role.instrumentId,row);button('Remove',()=>{config.roles=config.roles.filter(x=>x!==role);changed();render();},row);}
 $('roleInstrument').replaceChildren();for(const i of config.instruments){const option=node('option',i.name+' ('+i.id+')',$('roleInstrument'));option.value=i.id;}
 $('json').value=JSON.stringify(config,null,2);
}
function addEndpoint(endpoint){
 let controllerId=null;
 if(endpoint.controllerAddress){let c=config.controllers.find(x=>x.provider===endpoint.provider&&x.address===endpoint.controllerAddress);if(!c){const detected=inventory?.controllers.find(x=>x.provider===endpoint.provider&&x.address===endpoint.controllerAddress);c={id:'adapter'+(config.controllers.length+1),provider:endpoint.provider,address:endpoint.controllerAddress,stableId:detected?.stableId||null,confirmedDiscoveryId:null};config.controllers.push(c);}controllerId=c.id;}
 const id=endpoint.resourceId||("Instrument-"+(config.instruments.length+1));let instrument=config.instruments.find(x=>x.id===id);
 if(!instrument){instrument={id,name:endpoint.identity?.model||id,selectedConnection:'',connections:[],associationConfirmed:false,expectedIdentity:null};config.instruments.push(instrument);}
 if(instrument.connections.some(x=>x.provider===endpoint.provider&&x.address===endpoint.address&&x.controllerId===controllerId&&x.transport===endpoint.transport))throw Error('That endpoint is already in the draft.');
 const route={id:'connection'+(instrument.connections.length+1),provider:endpoint.provider,transport:endpoint.transport,address:endpoint.address,controllerId};while(instrument.connections.some(x=>x.id===route.id))route.id+='x';instrument.connections.push(route);instrument.selectedConnection||=route.id;instrument.associationConfirmed=false;changed();render();status('Connection added to the draft. Confirm any alternate-route association before saving.');
}
$('discover').onclick=guard(async()=>{discoveryAbort=new AbortController();$('cancel').disabled=false;$('discover').disabled=true;try{const source=$('discoverySource').value;const discoveryPath=source==='simulation'?'discover':'discover-visa?scope='+encodeURIComponent(source)+(source==='Ethernet'?'&host='+encodeURIComponent($('discoveryHost').value):'');inventory=await api(discoveryPath,{},discoveryAbort.signal);$('discoveryLabel').textContent=inventory.endpoints.length+' endpoints · '+inventory.controllers.length+' controllers';$('diagnostics').replaceChildren();for(const message of inventory.diagnostics)node('p',message,$('diagnostics')).className='notice';$('inventory').replaceChildren();const table=node('table',undefined,$('inventory'));table.className='inventory';const header=node('tr',undefined,table);for(const text of ['Instrument evidence','Connection','Availability',''])node('th',text,header);for(const endpoint of inventory.endpoints){const row=node('tr',undefined,table);node('td',(endpoint.identity?.model||'Unknown model')+' / '+(endpoint.resourceId||'Unregistered')+(endpoint.identity?.serial?' / Serial: '+endpoint.identity.serial:''),row);node('td',endpoint.transport+' · '+(endpoint.controllerAddress?endpoint.controllerAddress+' / ':'')+endpoint.address,row);node('td',endpoint.busy?'Reserved — cached metadata':endpoint.resourceId?'Available':'Availability untested',row);button('Add connection',()=>addEndpoint(endpoint),node('td',undefined,row));}changed();render();status('Discovery complete. Identify adapters and select the connections you want.');}finally{$('cancel').disabled=true;$('discover').disabled=false;}});
$('cancel').onclick=()=>discoveryAbort?.abort();
$('manualAdd').onclick=guard(()=>{const controller=$('manualController').value.trim(),provider=$('manualProvider').value.trim();if(controller&&!config.controllers.some(x=>x.id===controller))config.controllers.push({id:controller,provider,address:$('manualControllerAddress').value.trim(),stableId:null,confirmedDiscoveryId:null});addEndpoint({resourceId:$('manualId').value.trim(),identity:{model:$('manualName').value.trim()},provider,transport:$('manualTransport').value,address:$('manualAddress').value.trim(),controllerAddress:controller?config.controllers.find(x=>x.id===controller).address:null});});
$('addRole').onclick=guard(()=>{const role=$('roleName').value.trim();if(!role||!$('roleInstrument').value)throw Error('Enter a role and select an instrument.');if(config.roles.some(x=>x.role.toLowerCase()===role.toLowerCase()))throw Error('That role already exists.');config.roles.push({role,instrumentId:$('roleInstrument').value});changed();render();});
$('validate').onclick=guard(async()=>{const tested=JSON.stringify(config);const problems=await api('validate',config);if(tested!==JSON.stringify(config)){status('Draft changed during validation. Validate the updated mappings.');return;}$('validation').replaceChildren();if(!problems.length)node('p','Supported connections validated. Review and save to make this configuration available.',$('validation')).className='notice pass';for(const p of problems)node('p',p.path+': '+p.message,$('validation')).className='notice error';});
$('review').onclick=()=>{reviewed=JSON.stringify(config);$('before').textContent=JSON.stringify(saved?.configuration||{},null,2);$('after').textContent=JSON.stringify(config,null,2);$('reviewConfirmed').checked=false;$('reviewPanel').hidden=false;};
$('save').onclick=guard(async()=>{if(!$('reviewConfirmed').checked||reviewed!==JSON.stringify(config))throw Error('Review and confirm the current mappings before saving.');saved=await api('configuration',{configuration:config,expectedRevision:saved?.revision||null,draft:$('draft').checked});config=clone(saved.configuration);changed();render();status(saved.draft?'Untested draft saved. It cannot reserve instruments until validated.':'Validated configuration saved. Existing leases retain their original route.');});
async function reload(){saved=await api('configuration');config=saved?clone(saved.configuration):empty();changed();render();status(saved?(saved.draft?'Loaded untested draft.':'Loaded saved configuration. Rediscover before validating controller bindings.'):'No saved configuration. Start with discovery or manual entry.');}
$('reload').onclick=guard(reload);$('cancelEdit').onclick=()=>{config=saved?clone(saved.configuration):empty();changed();render();status('Draft edits cancelled; saved configuration unchanged.');};
$('export').onclick=()=>{const blob=new Blob([JSON.stringify(config,null,2)],{type:'application/json'}),url=URL.createObjectURL(blob),a=node('a');a.href=url;a.download='bench-setup.json';a.click();URL.revokeObjectURL(url);};
$('import').onclick=guard(async()=>{const text=$('json').value;const response=await fetch('/api/v1/setup/validate',{method:'POST',headers:{'Content-Type':'application/json'},body:text});const value=await response.json();if(!response.ok)throw Error(value.error||'Invalid configuration');config=JSON.parse(text);changed();render();status('Imported into the draft. Connection validation and saving are separate steps.');});
$('file').onchange=guard(async()=>{$('json').value=await $('file').files[0].text();status('File loaded into the JSON editor. Apply it to the draft to review.');});


$('iviInspect').onclick=guard(async()=>{
 const file=$('iviFile').files[0];if(!file)throw Error('Choose an IVI configuration XML file.');
 if(file.size>2*1024*1024)throw Error('IVI configuration exceeds the 2 MiB limit.');
 $('iviCatalog').replaceChildren();$('iviConfirmed').checked=false;$('iviInspect').disabled=true;
 try{
 const response=await fetch('/api/v1/setup/ivi-catalog',{method:'POST',headers:{'Content-Type':'application/xml'},body:await file.text()});
 const catalog=await response.json();if(!response.ok)throw Error(catalog.error||'Cannot inspect IVI configuration.');
 const root=$('iviCatalog');node('p','Source: '+file.name+' · Revision: '+catalog.revision,root).className='muted';
 for(const diagnostic of catalog.diagnostics)node('p',diagnostic,root).className='notice';
 const table=node('table',undefined,root);table.className='inventory';const header=node('tr',undefined,table);
 for(const label of ['Logical name','Driver session','Driver module','Connection',''])node('th',label,header);
 for(const binding of catalog.logicalNames){const row=node('tr',undefined,table),session=catalog.sessions.find(s=>s.name===binding.session),asset=catalog.hardwareAssets.find(a=>a.name===session?.hardwareAsset);
 for(const text of [binding.name,binding.session||'Unresolved',session?.softwareModule||'Unresolved',asset?.resourceDescriptor||'Unresolved'])node('td',text,row);
 button('Use binding',async()=>{
  if(!$('iviConfirmed').checked)throw Error('Review and confirm the IVI driver, route and target instrument first.');
  const stamp=JSON.stringify(config);
  const updated=await api('ivi-bindings/apply',{configuration:config,catalogRevision:catalog.revision,logicalName:binding.name,instrumentId:$('iviInstrumentId').value.trim(),role:$('iviRole').value.trim()||null,confirmed:$('iviConfirmed').checked});
  if(stamp!==JSON.stringify(config))throw Error('Draft changed during import. Review and apply the binding again.');
  config=updated;$('draft').checked=true;$('iviConfirmed').checked=false;changed();render();status('Reviewed IVI binding added to offline draft. Review changes and save; no driver has been opened.');
 },node('td',undefined,row));}

 const details=node('details',undefined,root);node('summary','All sessions, modules, routes and settings',details);node('pre',JSON.stringify(catalog,null,2),details);
 status('IVI configuration inspected. Setup and saved configuration are unchanged; activation requires a separately registered provider and explicit review.');
 }finally{$('iviInspect').disabled=false;}
});

const iviStorageKey='benchforge.ivi.lease.v1';
let activeIvi=null, iviVerified=false, iviRestoring=true, iviFunctions=[];
const iviFunctionNames=['DC voltage','AC voltage','DC current','AC current','Resistance'];
async function loadIviFunctions(preferred){
 const lease=activeIvi?.lease;if(!lease)return;
 iviFunctions=[];$('iviFunction').replaceChildren();
 const value=await api('ivi/'+encodeURIComponent(lease.resourceId)+'/capabilities');
 if(activeIvi?.lease!==lease)return;
 iviFunctions=value.functions.filter(f=>Number.isInteger(f)&&f>=0&&f<iviFunctionNames.length);
 for(const f of iviFunctions){const option=node('option',iviFunctionNames[f],$('iviFunction'));option.value=String(f);}
 if(iviFunctions.includes(preferred))$('iviFunction').value=String(preferred);
}
function retainIvi(){
 if(!activeIvi)stopOwnerHeartbeat();
 try{
  if(activeIvi){const {resourceId,leaseId,revision}=activeIvi.lease;sessionStorage.setItem(iviStorageKey,JSON.stringify({resourceId,leaseId,revision}));}
  else { sessionStorage.removeItem(iviStorageKey); stopOwnerHeartbeat(); }
  return true;
 }catch{return false;}
}
async function refreshIvi(){
 if(!activeIvi)throw Error('No retained IVI lease in this tab.');
 iviVerified=false;
 const lease=activeIvi.lease;
 try{
  const freshness=Number($('readingFreshness').value);
  if(!Number.isFinite(freshness)||freshness<1||freshness>86400000)throw Error('Choose a reading freshness limit from 1 to 86400000 milliseconds.');
  const snapshot=await api('ivi/'+encodeURIComponent(lease.resourceId)+'/status',{leaseId:lease.leaseId,readingFreshnessMilliseconds:freshness});
  if(activeIvi?.lease!==lease)return;
  if(snapshot.resourceId?.toLowerCase()!==lease.resourceId.toLowerCase()||snapshot.revision!==lease.revision)throw Error('Session metadata changed; retain the token and investigate the host.');
  iviVerified=true;activeIvi.active=snapshot.state==='Ready'; syncOwnerHeartbeat(snapshot.heartbeat);
  await loadIviFunctions(snapshot.function);if(activeIvi?.lease!==lease)return;
  $('iviRuntime').textContent=JSON.stringify(snapshot,null,2);
  renderReadingHealth(snapshot);
  status('Retained IVI session: '+snapshot.state+'. No driver was reopened.',snapshot.state==='RecoveryRequired');
 }catch(error){
  if(activeIvi?.lease!==lease)return;
  $('readingHealth').textContent='Status unavailable. Previously displayed device and reading health are unconfirmed.';
  if(error.httpStatus===403){activeIvi=null;retainIvi();$('iviRuntime').textContent='Host no longer recognizes this lease. Physical state has not been verified. Inspect recovery after host restart.';try{await refreshRecovery();}catch{/* The recovery panel reports unavailable inspection. */}}
  throw error;
 }
}
function renderReadingHealth(snapshot){
 const health=snapshot.readingHealth;
 const age=typeof health?.ageMilliseconds==='number'?health.ageMilliseconds.toFixed(0)+' ms':'No successful reading';
 const owner=snapshot.heartbeat?.state||'Not enabled';
 $('readingHealth').textContent='Owner contact: '+owner+'\nDevice: '+snapshot.state+'\nCached reading: '+(health?.state||'Unspecified')+' · '+age+
  '\nFreshness limit: '+(health?.maximumAgeMilliseconds??'Unspecified')+' ms\nLast device success: '+(snapshot.lastDeviceSuccessUtc||'None')+
  '\nLast reading receipt: '+(snapshot.lastReadingUtc||'None')+'\nSnapshot at refresh; contact and freshness do not certify physical safety.';
 $('readingHealth').className=health?.state==='Stale'||health?.state==='Missing'||snapshot.state!=='Ready'?'notice error':'notice';
}
function parseIviLease(raw){
 const lease=JSON.parse(raw);
 if(!lease||Array.isArray(lease)||Object.keys(lease).some(k=>!['resourceId','leaseId','revision'].includes(k))||
    typeof lease.resourceId!=='string'||!/^[A-Za-z0-9_.-]{1,80}$/.test(lease.resourceId)||
    typeof lease.leaseId!=='string'||!/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(lease.leaseId)||
    typeof lease.revision!=='string'||lease.revision.length===0||lease.revision.length>128)
   throw Error('Retained lease details must contain resourceId, leaseId and revision from this host.');
 return lease;
}
async function restoreIvi(){
 try{
  const raw=sessionStorage.getItem(iviStorageKey);if(!raw)return;
  const lease=parseIviLease(raw);
  activeIvi={lease};await refreshIvi();
 }finally{iviRestoring=false;}
}
$('refreshIvi').onclick=guard(refreshIvi);
let iviPending=false;
function iviAction(action){return guard(async()=>{
 if(iviPending)throw Error('Wait for the pending IVI operation; it will not be replayed.');
 iviPending=true;
 const buttons=['activateIvi','configureIvi','measureIvi','closeIvi','recoverIvi','resumeIvi','reconcileRestart','refreshRecovery'];
 for(const id of buttons)$(id).disabled=true;
 try{await action();}
 catch(error){
  if(activeIvi){try{await refreshIvi();}catch{/* Retain the capability when the host is unreachable. */}}
  throw error;
 }finally{iviPending=false;for(const id of buttons)$(id).disabled=false;}
});}
function requireIvi(allowExpired=false){if(ownerHeartbeat?.lost&&!allowExpired)throw Error('Owner heartbeat lost; refresh status and use explicit provider recovery.');if(!iviVerified)throw Error('Refresh retained session status before instrument operations.');if(!activeIvi)throw Error('Activate a registered saved role first.');return activeIvi.lease;}
$('activateIvi').onclick=iviAction(async()=>{
 if(iviRestoring)throw Error('Wait for retained session inspection.');
 if(activeIvi)throw Error('Close or recover the retained IVI lease before another activation.');
 if(!saved)throw Error('Save and review the setup first.');
 if(!$('activateConfirmed').checked)throw Error('Confirm driver activation first.');
 $('activateIvi').disabled=true;$('activateConfirmed').checked=false;
 try{
 activeIvi=await api('ivi/roles/'+encodeURIComponent($('activateRole').value.trim())+'/activate',{expectedRevision:saved.revision,confirmed:true});
 iviVerified=true;const retained=retainIvi();
 await loadIviFunctions();
 $('iviRuntime').textContent=JSON.stringify(activeIvi,null,2);$('activateConfirmed').checked=false;
 status(activeIvi.active?'IVI driver activated under an exclusive host lease.':'Initialization failed; ownership retained. Review logs and use provider recovery.',!activeIvi.active);
 if(!retained)status('Driver response received, but this tab cannot retain the lease across reload. Keep this page open until close or recovery.',true);
 }finally{$('activateIvi').disabled=false;}
});
$('showIviLease').onclick=guard(()=>{
 if(!activeIvi)throw Error('No retained IVI lease in this tab.');
 const {resourceId,leaseId,revision}=activeIvi.lease;
 $('iviLeaseDetails').value=JSON.stringify({resourceId,leaseId,revision},null,2);
 status('Private lease details displayed. Store them securely if you need to resume in another tab.');
});
$('resumeIvi').onclick=iviAction(async()=>{
 if(iviRestoring||activeIvi)throw Error('Finish inspecting, closing or recovering this tab’s retained lease first.');
 if(!$('resumeConfirmed').checked)throw Error('Confirm inspection of the supplied lease first.');
 const lease=parseIviLease($('iviLeaseDetails').value);$('resumeConfirmed').checked=false;
 activeIvi={lease};iviVerified=false;const retained=retainIvi();
 await refreshIvi();$('iviLeaseDetails').value='';
 if(!retained)status('Lease inspected, but browser storage is unavailable. Keep this page open until close or recovery.',true);
});
$('configureIvi').onclick=iviAction(async()=>{
 const lease=requireIvi(),selected=$('iviFunction').value,func=Number(selected);
 if(selected===''||!iviFunctions.includes(func))throw Error('Select a declared provider function; refresh status if none are listed.');
 const positive=id=>{const text=$(id).value.trim();if(text==='')return null;const value=Number(text);if(!Number.isFinite(value)||value<=0)throw Error('Range and resolution must be positive finite values.');return value;};
 await api('ivi/'+encodeURIComponent(lease.resourceId)+'/configure',{leaseId:lease.leaseId,function:func,range:positive('iviRange'),resolution:positive('iviResolution')});
 status('DMM configured for '+iviFunctionNames[func]+'.');
});
$('measureIvi').onclick=iviAction(async()=>{const lease=requireIvi();const result=await api('ivi/'+encodeURIComponent(lease.resourceId)+'/measure',{leaseId:lease.leaseId});$('iviRuntime').textContent='Measurement #'+result.sequence+': '+result.value+' '+result.unit+' · '+iviFunctionNames[result.function]+'\nHost receipt: '+result.completedUtc+'\nConfiguration: '+result.revision+'\nSession: '+result.correlationId;status('DMM measurement completed.');});
$('closeIvi').onclick=iviAction(async()=>{const lease=requireIvi();const response=await fetch('/api/v1/reservations/'+lease.leaseId,{method:'DELETE'});if(!response.ok){const value=await response.json();throw Error(value.message||value.error||'Cleanup requires recovery; ownership retained.');}activeIvi=null;iviVerified=false;retainIvi();$('iviLeaseDetails').value='';$('iviRuntime').textContent='Driver closed; lease released.';status('IVI cleanup completed before releasing ownership.');});
$('recoverIvi').onclick=iviAction(async()=>{const lease=requireIvi(true);if(!$('recoverConfirmed').checked)throw Error('Confirm provider recovery first.');$('recoverConfirmed').checked=false;await api('ivi/'+encodeURIComponent(lease.resourceId)+'/recover',{leaseId:lease.leaseId,confirmed:true});activeIvi=null;iviVerified=false;retainIvi();$('iviLeaseDetails').value='';$('recoverConfirmed').checked=false;$('iviRuntime').textContent='Provider recovery completed; lease released.';status('Recovery succeeded. A new activation requires review.');});

let restartRecords=[],restartReviewValid=false,recoveryGeneration=0;
async function refreshRecovery(){
 const generation=++recoveryGeneration;restartReviewValid=false;$('restartConfirmed').checked=false;
 try{
 const records=await api('ivi/recovery');if(generation!==recoveryGeneration)return;
 restartRecords=records;restartReviewValid=true;$('restartResource').replaceChildren();
 for(const record of records){const option=node('option',record.resourceId+' · '+record.code+' · '+record.recordId,$('restartResource'));option.value=record.recordId;}
 $('restartRecords').textContent=records.length?JSON.stringify(records,null,2):'No unresolved startup ownership records.';
 }catch(error){if(generation!==recoveryGeneration)return;$('restartRecords').textContent='Recovery inspection unavailable. Refresh before reconciliation.';throw error;}
}
$('refreshRecovery').onclick=guard(refreshRecovery);
$('restartResource').onchange=()=>{$('restartConfirmed').checked=false;};
$('reconcileRestart').onclick=iviAction(async()=>{
 if(!restartReviewValid)throw Error('Refresh pending recovery before reconciliation.');
 const record=restartRecords.find(r=>r.recordId===$('restartResource').value);
 if(!record)throw Error('Inspect and select an unresolved ownership record first.');
 if(!record.canReconcile)throw Error('This record requires a matching qualified restart provider: '+record.code);
 if(!$('restartConfirmed').checked)throw Error('Confirm the qualified restart-recovery procedure first.');
 $('restartConfirmed').checked=false;
 await api('ivi/recovery/'+encodeURIComponent(record.resourceId)+'/reconcile',{recordId:record.recordId,confirmed:true});
 await refreshRecovery();status('Restart reconciliation completed. New activation still requires review.');
});

let ownerHeartbeat=null;
function stopOwnerHeartbeat(){
 const previous=ownerHeartbeat;ownerHeartbeat=null;
 if(previous){clearTimeout(previous.timer);previous.abort?.abort();}
}
function syncOwnerHeartbeat(snapshot){
 const lease=activeIvi?.lease;
 if(!snapshot||!lease){stopOwnerHeartbeat();$('ownerHeartbeat').textContent='No heartbeat-enabled owner inspected.';return;}
 if(ownerHeartbeat?.lease===lease){if(snapshot.state==='Expired'){ownerHeartbeat.lost=true;clearTimeout(ownerHeartbeat.timer);$('ownerHeartbeat').textContent='Owner expired; ownership retained for explicit recovery.';}return;}
 stopOwnerHeartbeat();
 if(!Number.isSafeInteger(snapshot.sequence)||snapshot.sequence<0||!Number.isSafeInteger(snapshot.ownerGeneration)||snapshot.ownerGeneration<=0||
    !Number.isFinite(snapshot.intervalMilliseconds)||snapshot.intervalMilliseconds<10||snapshot.intervalMilliseconds>60000)throw Error('Invalid host heartbeat metadata.');
 ownerHeartbeat={lease,hostEpoch:snapshot.hostEpoch,ownerGeneration:snapshot.ownerGeneration,
  peerEpoch:snapshot.peerEpoch&&snapshot.peerEpoch!=='00000000-0000-0000-0000-000000000000'?snapshot.peerEpoch:crypto.randomUUID(),
  sequence:snapshot.sequence,interval:snapshot.intervalMilliseconds,lost:snapshot.state==='Expired',timer:null,pending:null,abort:null};
 if(ownerHeartbeat.lost){$('ownerHeartbeat').textContent='Owner expired; ownership retained for explicit recovery.';return;}
 void renewOwnerHeartbeat();
}
async function renewOwnerHeartbeat(){
 const context=ownerHeartbeat;
 if(!context||context.lost)return;
 if(context.pending)return context.pending;
 clearTimeout(context.timer);
 context.pending=(async()=>{
  context.abort=new AbortController();const timeout=setTimeout(()=>context.abort.abort(),Math.min(context.interval,10000));
  try{
   const sequence=++context.sequence;if(!Number.isSafeInteger(sequence)){context.lost=true;throw Error('Heartbeat sequence exhausted.');}
   const ack=await api('ivi/'+encodeURIComponent(context.lease.resourceId)+'/heartbeat',{
    leaseId:context.lease.leaseId,hostEpoch:context.hostEpoch,ownerGeneration:context.ownerGeneration,peerEpoch:context.peerEpoch,sequence},context.abort.signal);
   if(ownerHeartbeat!==context)return;
   if(ack.hostEpoch!==context.hostEpoch||ack.ownerGeneration!==context.ownerGeneration||ack.peerEpoch!==context.peerEpoch||ack.sequence!==sequence||ack.state!=='Healthy'){
    context.lost=true;throw Error('Heartbeat acknowledgment did not match this owner.');
   }
   $('ownerHeartbeat').textContent='Owner heartbeat acknowledged. Instrument readiness is reported separately.';
  }catch(error){
   if(ownerHeartbeat!==context)return;
   if([401,403,404,409].includes(error.httpStatus))context.lost=true;
   iviVerified=false;
   $('ownerHeartbeat').textContent=context.lost?'Owner heartbeat lost; retain the lease and inspect explicit recovery.':'Heartbeat unavailable; control is unconfirmed. Refresh status before commands.';
  }finally{
   clearTimeout(timeout);context.pending=null;context.abort=null;
   if(ownerHeartbeat===context&&!context.lost)context.timer=setTimeout(()=>void renewOwnerHeartbeat(),context.interval);
  }
 })();
 return context.pending;
}
$('renewOwnerHeartbeat').onclick=guard(renewOwnerHeartbeat);
window.addEventListener('pagehide',stopOwnerHeartbeat);

let lifecycleGeneration=0;
async function refreshHostLifecycle(){
 const generation=++lifecycleGeneration;
 try{
  const state=await api('host-lifecycle');if(generation!==lifecycleGeneration)return;
  $('hostLifecycle').textContent=(state.acceptingWork?'Host accepts instrument work.':'Host is stopping; new instrument work is refused.')+
   '\nPending cleanup workers: '+state.pendingCleanup+'\n'+(state.shutdown?JSON.stringify(state.shutdown,null,2):'No completed shutdown decision.');
 }catch(error){if(generation===lifecycleGeneration)$('hostLifecycle').textContent='Host lifecycle inspection unavailable. Previous results are no longer current.';throw error;}
}
$('refreshHostLifecycle').onclick=guard(refreshHostLifecycle);
guard(async()=>{try{await reload();}finally{try{await restoreIvi();}finally{try{await refreshRecovery();}finally{await refreshHostLifecycle();}}}})();
