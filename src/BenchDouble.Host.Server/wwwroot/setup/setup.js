'use strict';
const $ = id => document.getElementById(id);
let config = empty(), saved = null, inventory = null, discoveryAbort = null, reviewed = null;
function empty() { return {schemaVersion:1, controllers:[], instruments:[], roles:[]}; }
function clone(value) { return JSON.parse(JSON.stringify(value)); }
function status(message, error=false) { $('status').textContent=message; $('status').className='notice'+(error?' error':''); }
async function api(path, body, signal) {
  const response=await fetch('/api/v1/setup/'+path, {method:body===undefined?'GET':'POST', headers:body===undefined?{}:{'Content-Type':'application/json'}, body:body===undefined?undefined:JSON.stringify(body),signal});
  const value=await response.json(); if(!response.ok) throw Error(value.error||JSON.stringify(value)); return value;
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
$('discover').onclick=guard(async()=>{discoveryAbort=new AbortController();$('cancel').disabled=false;$('discover').disabled=true;try{const source=$('discoverySource').value;const discoveryPath=source==='simulation'?'discover':'discover-visa?scope='+encodeURIComponent(source)+'&host='+encodeURIComponent($('discoveryHost').value);inventory=await api(discoveryPath,{},discoveryAbort.signal);$('discoveryLabel').textContent=inventory.endpoints.length+' endpoints · '+inventory.controllers.length+' controllers';$('diagnostics').replaceChildren();for(const message of inventory.diagnostics)node('p',message,$('diagnostics')).className='notice';$('inventory').replaceChildren();const table=node('table',undefined,$('inventory'));table.className='inventory';const header=node('tr',undefined,table);for(const text of ['Instrument evidence','Connection','Availability',''])node('th',text,header);for(const endpoint of inventory.endpoints){const row=node('tr',undefined,table);node('td',(endpoint.identity?.model||'Unknown model')+' / '+(endpoint.resourceId||'Unregistered')+(endpoint.identity?.serial?' / Serial: '+endpoint.identity.serial:''),row);node('td',endpoint.transport+' · '+(endpoint.controllerAddress?endpoint.controllerAddress+' / ':'')+endpoint.address,row);node('td',endpoint.busy?'Reserved — cached metadata':endpoint.resourceId?'Available':'Availability untested',row);button('Add connection',()=>addEndpoint(endpoint),node('td',undefined,row));}changed();render();status('Discovery complete. Identify adapters and select the connections you want.');}finally{$('cancel').disabled=true;$('discover').disabled=false;}});
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
guard(reload)();

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
 status('IVI configuration inspected. Setup and saved configuration are unchanged; driver activation is not implemented.');
 }finally{$('iviInspect').disabled=false;}
});

let activeIvi=null;
function requireIvi(){if(!activeIvi)throw Error('Activate a registered saved role first.');return activeIvi.lease;}
$('activateIvi').onclick=guard(async()=>{
 if(activeIvi)throw Error('Close or recover the retained IVI lease before another activation.');
 if(!saved)throw Error('Save and review the setup first.');
 if(!$('activateConfirmed').checked)throw Error('Confirm driver activation first.');
 $('activateIvi').disabled=true;
 try{
 activeIvi=await api('ivi/roles/'+encodeURIComponent($('activateRole').value.trim())+'/activate',{expectedRevision:saved.revision,confirmed:true});
 $('iviRuntime').textContent=JSON.stringify(activeIvi,null,2);$('activateConfirmed').checked=false;
 status(activeIvi.active?'IVI driver activated under an exclusive host lease.':'Initialization failed; ownership retained. Review logs and use provider recovery.',!activeIvi.active);
 }finally{$('activateIvi').disabled=false;}
});
$('configureIvi').onclick=guard(async()=>{const lease=requireIvi();await api('ivi/'+encodeURIComponent(lease.resourceId)+'/configure',{leaseId:lease.leaseId,function:0});status('DMM configured for DC voltage.');});
$('measureIvi').onclick=guard(async()=>{const lease=requireIvi();const result=await api('ivi/'+encodeURIComponent(lease.resourceId)+'/measure',{leaseId:lease.leaseId});$('iviRuntime').textContent='Measurement: '+result.value+' (base SI unit of the configured function)';status('DMM measurement completed.');});
$('closeIvi').onclick=guard(async()=>{const lease=requireIvi();const response=await fetch('/api/v1/reservations/'+lease.leaseId,{method:'DELETE'});if(!response.ok){const value=await response.json();throw Error(value.message||value.error||'Cleanup requires recovery; ownership retained.');}activeIvi=null;$('iviRuntime').textContent='Driver closed; lease released.';status('IVI cleanup completed before releasing ownership.');});
$('recoverIvi').onclick=guard(async()=>{const lease=requireIvi();if(!$('recoverConfirmed').checked)throw Error('Confirm provider recovery first.');await api('ivi/'+encodeURIComponent(lease.resourceId)+'/recover',{leaseId:lease.leaseId,confirmed:true});activeIvi=null;$('recoverConfirmed').checked=false;$('iviRuntime').textContent='Provider recovery completed; lease released.';status('Recovery succeeded. A new activation requires review.');});
