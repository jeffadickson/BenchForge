/* DOM/HTTP integration checks against a disposable host. Does not test browser rendering.
 * Requires happy-dom (HAPPY_DOM_MODULE may select its module), and a built Release host.
 */
const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),assert=require('node:assert/strict');
const {spawn}=require('node:child_process');
const root=path.resolve(__dirname,'..');
(async()=>{
 const {Window}=await import(process.env.HAPPY_DOM_MODULE||'happy-dom');
 const temporary=fs.mkdtempSync(path.join(os.tmpdir(),'bench-setup-dom-'));
 const hostRoot=path.join(root,'src/BenchDouble.Host.Server');
 const child=spawn(process.env.BENCH_SETUP_DOTNET||'dotnet',[path.join(hostRoot,'bin/Release/net10.0/BenchDouble.Host.Server.dll')],{cwd:hostRoot,env:{...process.env,BenchSetup__Path:path.join(temporary,'config.json')},stdio:['ignore','pipe','pipe']});
 let log='',window;child.stdout.on('data',x=>log+=x);child.stderr.on('data',x=>log+=x);
 const base='http://127.0.0.1:5278';
 const request=(route,body,extra={})=>fetch(base+route,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json',...extra},body:body===undefined?undefined:JSON.stringify(body)});
 const wait=async(predicate)=>{for(let i=0;i<100;i++){if(await predicate())return;await new Promise(resolve=>setTimeout(resolve,100));}throw Error('Condition timed out. '+log);};
 try{
  await wait(async()=>{try{return(await fetch(base+'/health')).ok;}catch{return false;}});
  assert.equal(JSON.parse(fs.readFileSync(path.join(temporary,'config.json.ownership','ownership.json'),'utf8')).version,1);
  window=new Window({url:base+'/setup/',settings:{disableCSSFileLoading:true,disableJavaScriptFileLoading:true}});
  window.AbortController=global.AbortController;
  window.fetch=(url,options)=>fetch(new URL(url,base),options);
  window.document.write(await(await fetch(base+'/setup/')).text());
  window.eval(await(await fetch(base+'/setup/setup.js')).text());
  const $=id=>window.document.getElementById(id);
  await wait(()=>$('status').textContent.includes('No saved configuration'));
  await $('discover').onclick();
  assert.match($('discoveryLabel').textContent,/3 endpoints/,$('status').textContent);
  const add=()=>[...$('inventory').querySelectorAll('button')];
  for(let i=0;i<3;i++)await add()[i].onclick();
  for(const checkbox of $('controllers').querySelectorAll('input[type=checkbox]')){checkbox.checked=true;checkbox.onchange();}
  const association=$('instruments').querySelector('input[type=checkbox]');association.checked=true;association.onchange();
  $('roleName').value='voltmeter';$('roleInstrument').value='DMM1';await $('addRole').onclick();
  await $('validate').onclick();assert.ok($('validation').querySelector('.pass'),$('status').textContent+' '+$('validation').textContent);
  $('review').onclick();$('reviewConfirmed').checked=true;await $('save').onclick();
  assert.match($('status').textContent,/Validated configuration saved/);
  const saved=await(await request('/api/v1/setup/configuration')).json();assert.equal(saved.draft,false);assert.equal(saved.configuration.instruments.length,2);
  const leaseResponse=await request('/api/v1/setup/roles/voltmeter/reserve',{});assert.equal(leaseResponse.status,200);const lease=await leaseResponse.json();
  assert.equal((await request('/api/v1/reservations',{resourceId:'DMM1'})).status,409);
  await $('validate').onclick();assert.match($('validation').textContent,/reserved or unavailable/);
  assert.equal((await fetch(base+'/api/v1/reservations/'+lease.leaseId,{method:'DELETE'})).status,204);
  const name=$('instruments').querySelector('input');name.value='<img src=x onerror=alert(1)>';name.onchange();$('review').onclick();assert.equal($('after').querySelectorAll('img').length,0);
  $('cancelEdit').onclick();assert.equal((await(await request('/api/v1/setup/configuration')).json()).revision,saved.revision);
  // JSON import is checked by the actual strict host parser before applying it to the draft.
  $('json').value='{"schemaVersion":1,"schemaVersion":2}';await $('import').onclick();assert.match($('status').textContent,/Duplicate setup field/);
  assert.deepEqual(await(await request('/api/v1/setup/ivi/recovery')).json(),[]);
  assert.equal((await request('/api/v1/setup/ivi/recovery/METER/reconcile',{})).status,400);
  assert.equal((await request('/api/v1/setup/ivi/recovery/METER/reconcile',{recordId:'00000000-0000-0000-0000-000000000001',confirmed:false})).status,409);
  assert.equal((await request('/api/v1/setup/ivi/recovery/METER/reconcile',{recordId:'00000000-0000-0000-0000-000000000001',confirmed:true},{Origin:'https://elsewhere.example'})).status,403);
  // Read-only IVI upload: exercise real malformed-input handling and platform diagnostics.
  const beforeIvi=JSON.stringify(await(await request('/api/v1/setup/configuration')).json());
  const inspectXml=body=>fetch(base+'/api/v1/setup/ivi-catalog',{method:'POST',headers:{'Content-Type':'application/xml'},body});
  assert.equal((await inspectXml('<bad/>')).status,400);
  assert.equal((await inspectXml('<!DOCTYPE x [<!ENTITY e SYSTEM "file:///etc/passwd">]><IviConfigStore>&e;</IviConfigStore>')).status,400);
  assert.equal((await inspectXml('x'.repeat(2*1024*1024+1))).status,400);
  if(process.platform!=='win32'){
   Object.defineProperty($('iviFile'),'files',{configurable:true,value:[{name:'store.xml',size:18,text:async()=>'<IviConfigStore />'}]});
   await $('iviInspect').onclick();assert.match($('status').textContent,/Windows registry/);
  }
  // Render a supplied catalog independently of the platform-specific loader.
  const realFetch=window.fetch;
  window.fetch=async(url,options)=>String(url).endsWith('/ivi-catalog')?{ok:true,json:async()=>({revision:'test-revision',diagnostics:['Preview only'],logicalNames:[{name:'<img src=x>',session:'session'}],sessions:[{name:'session',hardwareAsset:'A',softwareModule:'driver'}],hardwareAssets:[{name:'A',resourceDescriptor:'GPIB1::22::INSTR'}],softwareModules:[]})}:realFetch(url,options);
  Object.defineProperty($('iviFile'),'files',{configurable:true,value:[{name:'store.xml',size:18,text:async()=>'<IviConfigStore />'}]});
  await $('iviInspect').onclick();assert.match($('iviCatalog').textContent,/GPIB1::22::INSTR/);assert.equal($('iviCatalog').querySelectorAll('img').length,0);
  const previewFetch=window.fetch;
  window.fetch=async(url,options)=>String(url).endsWith('/ivi-bindings/apply')?{ok:true,json:async()=>{
   const r=JSON.parse(options.body),draft=r.configuration;draft.schemaVersion=2;
   draft.instruments.push({id:r.instrumentId,name:r.instrumentId,selectedConnection:'ivi-route1',associationConfirmed:false,connections:[{id:'ivi-route1',provider:'visa',transport:'VISA',address:'TCPIP0::meter.lab::inst0::INSTR',driverBinding:{sourceRevision:'a'.repeat(64),logicalName:r.logicalName,session:'session',module:{name:'driver',kind:'IviNetSoftwareModule',assemblyQualifiedClassName:'Vendor.Driver, Vendor'},resourceDescriptor:'TCPIP0::meter.lab::inst0::INSTR',settings:{DriverSetup:'PRIVATE-DRIVER-OPTION'},virtualNames:{}}}]});return draft;
  }}:previewFetch(url,options);
  $('iviInstrumentId').value='IMPORTED';
  await $('iviCatalog').querySelector('button').onclick();assert.match($('status').textContent,/Review and confirm/);
  $('iviConfirmed').checked=true;await $('iviCatalog').querySelector('button').onclick();assert.match($('status').textContent,/added to offline draft/);
  assert.match($('instruments').textContent,/IVI: driver/);assert.equal(JSON.parse($('json').value).schemaVersion,2);
  window.fetch=realFetch;
  assert.equal(JSON.stringify(await(await request('/api/v1/setup/configuration')).json()),beforeIvi);
  // A stale editor cannot overwrite the saved configuration.
  assert.equal((await request('/api/v1/setup/configuration',{configuration:saved.configuration,expectedRevision:'stale',draft:true})).status,409);
  assert.equal((await request('/api/v1/setup/configuration',{configuration:saved.configuration,expectedRevision:saved.revision,draft:true},{Origin:'https://elsewhere.example'})).status,403);
  $('discoverySource').value='USB';await $('discover').onclick();assert.match($('diagnostics').textContent,/VISA/);
  $('manualId').value='PHYSICAL1';$('manualName').value='Offline meter';$('manualProvider').value='visa';$('manualTransport').value='Ethernet';$('manualAddress').value='192.0.2.10';await $('manualAdd').onclick();
  $('review').onclick();$('reviewConfirmed').checked=true;$('draft').checked=true;await $('save').onclick();assert.match($('status').textContent,/Untested draft saved/);
  assert.equal((await request('/api/v1/setup/roles/voltmeter/reserve',{})).status,409);
  await $('reload').onclick();assert.match($('instruments').textContent,/PHYSICAL1/);
  $('activateRole').value='voltmeter';$('activateConfirmed').checked=true;await $('activateIvi').onclick();
  assert.match($('status').textContent,/no reviewed IVI binding/);
  assert.equal((await request('/api/v1/setup/ivi/METER/status',{leaseId:'00000000-0000-0000-0000-000000000001'})).status,403);
  assert.equal((await request('/api/v1/setup/ivi/METER/configure',{leaseId:'00000000-0000-0000-0000-000000000001'})).status,400);
  const lifecycleFetch=window.fetch;let failActivation=false,activationRequests=0;
  window.fetch=async(url,options)=>{
   const name=String(url);
   if(name.endsWith('/activate')){activationRequests++;return {ok:true,json:async()=>({active:!failActivation,errorCode:failActivation?'recovery_required':null,lease:{resourceId:'METER',leaseId:'00000000-0000-0000-0000-000000000001',revision:'preview',connection:{}}})};}
   if(name.endsWith('/status'))return {ok:true,json:async()=>({resourceId:'meter',revision:'preview',state:failActivation?'RecoveryRequired':'Ready',function:0})};
   if(name.endsWith('/capabilities'))return {ok:true,json:async()=>({functions:[0,4]})};
   if(name.endsWith('/configure'))return {ok:true,json:async()=>({configured:true})};
   if(name.endsWith('/measure'))return {ok:true,json:async()=>({value:4.5,function:4,unit:'ohm',sequence:2,correlationId:'diagnostic-session',completedUtc:'2026-10-17T00:00:00Z',revision:'preview'})};
   if(name.endsWith('/recover'))return {ok:true,json:async()=>({recovered:true})};
   if(options?.method==='DELETE')return {ok:true};
   return lifecycleFetch(url,options);
  };
  $('activateConfirmed').checked=true;await $('activateIvi').onclick();assert.match($('status').textContent,/activated under/);
  const retained=window.sessionStorage.getItem('benchforge.ivi.lease.v1');assert.ok(retained);assert.ok(!retained.includes('connection'));
  const restored=new Window({url:base+'/setup/',settings:{disableCSSFileLoading:true,disableJavaScriptFileLoading:true}});
  try{
   restored.fetch=window.fetch;restored.sessionStorage.setItem('benchforge.ivi.lease.v1',retained);
   restored.document.write(await(await fetch(base+'/setup/')).text());restored.eval(await(await fetch(base+'/setup/setup.js')).text());
   await wait(()=>restored.document.getElementById('status').textContent.includes('No driver was reopened'));
   assert.match(restored.document.getElementById('iviRuntime').textContent,/Ready/);
  }finally{await restored.happyDOM.close();}
  assert.equal($('iviFunction').options.length,2);$('iviFunction').value='4';$('iviRange').value='1000';$('iviResolution').value='0.01';
  const normalLifecycleFetch=window.fetch;let configureRequests=0,finishConfigure;
  window.fetch=async(url,options)=>String(url).endsWith('/configure')?(configureRequests++,await new Promise(resolve=>finishConfigure=()=>resolve({ok:true,json:async()=>({configured:true})}))):normalLifecycleFetch(url,options);
  const pending=$('configureIvi').onclick();assert.equal($('measureIvi').disabled,true);
  await $('configureIvi').onclick();assert.equal(configureRequests,1);assert.match($('status').textContent,/pending IVI operation/);
  finishConfigure();await pending;window.fetch=normalLifecycleFetch;
  assert.equal($('measureIvi').disabled,false);
  await $('configureIvi').onclick();await $('measureIvi').onclick();assert.match($('iviRuntime').textContent,/4.5 ohm/);assert.match($('iviRuntime').textContent,/Measurement #2/);assert.match($('iviRuntime').textContent,/Resistance/);assert.match($('iviRuntime').textContent,/diagnostic-session/);
  let finishClose,finishStatus;const beforeCloseFetch=window.fetch;
  window.fetch=async(url,options)=>{
   if(options?.method==='DELETE')return await new Promise(resolve=>finishClose=()=>resolve({ok:true}));
   if(String(url).endsWith('/status'))return await new Promise(resolve=>finishStatus=()=>resolve({ok:true,json:async()=>({resourceId:'METER',revision:'preview',state:'Ready'})}));
   return beforeCloseFetch(url,options);
  };
  const closing=$('closeIvi').onclick(),refreshing=$('refreshIvi').onclick();
  finishClose();await closing;finishStatus();await refreshing;window.fetch=beforeCloseFetch;
  assert.match($('iviRuntime').textContent,/lease released/);assert.equal(window.sessionStorage.getItem('benchforge.ivi.lease.v1'),null);
  failActivation=true;$('activateConfirmed').checked=true;await $('activateIvi').onclick();assert.match($('status').textContent,/ownership retained/);
  await $('recoverIvi').onclick();assert.match($('status').textContent,/Confirm provider recovery/);
  $('recoverConfirmed').checked=true;await $('recoverIvi').onclick();assert.match($('status').textContent,/Recovery succeeded/);
  assert.equal($('recoverConfirmed').checked,false);
  // A failed status request retains the capability and blocks commands until inspection succeeds.
  failActivation=false;$('activateConfirmed').checked=true;await $('activateIvi').onclick();
  const healthyFetch=window.fetch;
  window.fetch=async(url,options)=>String(url).endsWith('/status')?{ok:false,status:503,json:async()=>({error:'Host unavailable'})}:healthyFetch(url,options);
  await $('refreshIvi').onclick();assert.ok(window.sessionStorage.getItem('benchforge.ivi.lease.v1'));
  await $('measureIvi').onclick();assert.match($('status').textContent,/Refresh retained session/);
  window.fetch=async(url,options)=>String(url).endsWith('/status')?{ok:false,status:403,json:async()=>({error:'Lease invalid.'})}:healthyFetch(url,options);
  await $('refreshIvi').onclick();assert.equal(window.sessionStorage.getItem('benchforge.ivi.lease.v1'),null);
  assert.match($('iviRuntime').textContent,/Physical state has not been verified/);
  window.fetch=healthyFetch;
  const activationCount=activationRequests;
  $('iviLeaseDetails').value=retained;await $('resumeIvi').onclick();assert.match($('status').textContent,/Confirm inspection/);
  $('resumeConfirmed').checked=true;await $('resumeIvi').onclick();assert.match($('status').textContent,/No driver was reopened/);
  assert.equal(activationRequests,activationCount);assert.equal($('resumeConfirmed').checked,false);
  await $('showIviLease').onclick();assert.deepEqual(JSON.parse($('iviLeaseDetails').value),JSON.parse(retained));
  await $('closeIvi').onclick();assert.equal(window.sessionStorage.getItem('benchforge.ivi.lease.v1'),null);
  $('iviLeaseDetails').value='{"leaseId":"invalid"}';$('resumeConfirmed').checked=true;await $('resumeIvi').onclick();
  assert.match($('status').textContent,/must contain/);assert.equal(window.sessionStorage.getItem('benchforge.ivi.lease.v1'),null);
  window.fetch=lifecycleFetch;
  let pendingRestart=true,restartFails=true,restartCalls=0,inspectionFails=false;const beforeRestartFetch=window.fetch;
  window.fetch=async(url,options)=>{
   if(String(url).endsWith('/ivi/recovery')&&inspectionFails)return {ok:false,status:503,json:async()=>({error:'Inspection unavailable'})};
   if(String(url).endsWith('/ivi/recovery'))return {ok:true,json:async()=>pendingRestart?[{resourceId:'METER',recordId:'review-1',canReconcile:true,code:'ready',routesDigest:'digest',revision:'preview',recordedUtc:'2026-10-17T00:00:00Z'}]:[]};
   if(String(url).endsWith('/reconcile')){restartCalls++;if(restartFails)return {ok:false,status:409,json:async()=>({error:'Provider reconciliation failed; ownership retained.'})};pendingRestart=false;return {ok:true,json:async()=>({reconciled:true})};}
   return beforeRestartFetch(url,options);
  };
  await $('refreshRecovery').onclick();assert.match($('restartRecords').textContent,/METER/);
  inspectionFails=true;$('restartConfirmed').checked=true;await $('refreshRecovery').onclick();assert.equal($('restartConfirmed').checked,false);
  $('restartConfirmed').checked=true;await $('reconcileRestart').onclick();assert.equal(restartCalls,0);assert.match($('status').textContent,/Refresh pending recovery/);
  inspectionFails=false;await $('refreshRecovery').onclick();
  await $('reconcileRestart').onclick();assert.equal(restartCalls,0);
  $('restartConfirmed').checked=true;await $('reconcileRestart').onclick();assert.equal(restartCalls,1);assert.equal($('restartConfirmed').checked,false);assert.match($('restartRecords').textContent,/METER/);
  restartFails=false;$('restartConfirmed').checked=true;await $('reconcileRestart').onclick();assert.equal(restartCalls,2);assert.match($('restartRecords').textContent,/No unresolved/);
  window.fetch=beforeRestartFetch;
  assert.ok(!log.includes('PRIVATE-DRIVER-OPTION'));assert.match(log,/Setup saved/);
  console.log('PASS: DOM/HTTP workflow: discover, two adapters, optional serials, association, role, validation, save, contention, edit cancellation, safe rendering, strict import, stale save, origin rejection, native diagnostics, offline draft/reload, IVI upload bounds/platform diagnostics, reviewed bindings, retained-session reload/status, command serialization, stale capabilities, declared functions, measurement units/provenance, manual lease resumption and payload-free logging.');
 }finally{
  if(window)await window.happyDOM.close();child.kill('SIGTERM');await new Promise(resolve=>child.exitCode!==null?resolve():child.once('exit',resolve));fs.rmSync(temporary,{recursive:true,force:true});
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
