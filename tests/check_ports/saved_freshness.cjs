// Execute actual setup policy functions with detached DOM and transport substitutes.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync('src/BenchDouble.Host.Server/wwwroot/setup/setup.js','utf8');
const start=source.indexOf('async function refreshIvi('),end=source.indexOf('\nfunction parseIviLease',start);
const elements=new Map(),errors=[],requests=[];let changed=0,count=0,pinned=1000;
const $=id=>{if(!elements.has(id))elements.set(id,{textContent:'',className:'',value:'',checked:false,children:[]});return elements.get(id);};
const node=(tag,text,parent)=>{const item={tag,textContent:text,value:'',children:[]};if(parent)parent.children.push(item);return item;};
const context={$,node,guard:action=>async()=>{try{await action();}catch(error){errors.push(error);}},changed:()=>changed++,iviVerified:false,
 activeIvi:{lease:{resourceId:'METER',leaseId:'lease',revision:'revision'}},syncOwnerHeartbeat:()=>{},loadIviFunctions:async()=>{},status:()=>{},
 api:async(path,body)=>{assert.ok(path.endsWith('/status'));requests.push({path,body});const limit=body.readingFreshnessMilliseconds??pinned;return {resourceId:'METER',revision:'revision',state:'Ready',readingFreshnessMilliseconds:pinned,readingHealth:{state:limit==null?'Unspecified':1500>=limit?'Stale':'Fresh',ageMilliseconds:1500,maximumAgeMilliseconds:limit}};}};
vm.createContext(context);vm.runInContext(source.slice(start,end),context);
for(const text of ['1','1.25','5000','86400000']){assert.equal(context.parseReadingFreshness(text,false),Number(text));count++;}
assert.equal(context.parseReadingFreshness(' ',true),null);count++;
for(const text of ['',' ','NaN','Infinity','0','-1','86400001']){assert.throws(()=>context.parseReadingFreshness(text,false),/freshness limit/i);count++;}
assert.throws(()=>context.parseReadingFreshness(null,true),/numeric text/);count++;
(async()=>{
 const role={role:'meter',instrumentId:'METER',readingFreshnessMilliseconds:1000},parent={children:[]};context.renderRoleFreshness(role,parent);const field=parent.children[0].children[0];
 assert.equal(field.min,'1');assert.equal(field.max,'86400000');field.value='2500.5';await field.onchange();assert.equal(role.readingFreshnessMilliseconds,2500.5);assert.equal(changed,1);count++;
 field.value='-1';await field.onchange();assert.equal(role.readingFreshnessMilliseconds,2500.5);assert.equal(Number(field.value),2500.5);assert.equal(changed,1);assert.equal(errors.length,1);count++;
 field.value='';await field.onchange();assert.equal(role.readingFreshnessMilliseconds,null);assert.equal(changed,2);count++;
 const other={role:'other',instrumentId:'OTHER',readingFreshnessMilliseconds:9000};context.renderRoleFreshness(other,{children:[]});assert.equal(role.readingFreshnessMilliseconds,null);assert.equal(other.readingFreshnessMilliseconds,9000);count++;
 $('useSavedReadingFreshness').checked=true;$('readingFreshness').value='invalid';await context.refreshIvi();assert.equal(requests.at(-1).body.readingFreshnessMilliseconds,null);assert.match($('readingHealth').textContent,/Saved role limit at activation: 1000/);assert.match($('readingHealth').textContent,/Stale/);count++;
 $('useSavedReadingFreshness').checked=false;$('readingFreshness').value='3000';await context.refreshIvi();assert.equal(requests.at(-1).body.readingFreshnessMilliseconds,3000);assert.match($('readingHealth').textContent,/Applied freshness limit: 3000/);assert.match($('readingHealth').textContent,/Saved role limit at activation: 1000/);count++;
 const before=requests.length;$('readingFreshness').value='';await assert.rejects(context.refreshIvi(),/freshness limit/i);assert.equal(requests.length,before);assert.match($('readingHealth').textContent,/unconfirmed/);count++;
 pinned=null;$('useSavedReadingFreshness').checked=true;await context.refreshIvi();assert.match($('readingHealth').textContent,/Unspecified/);count++;
 vm.runInContext(source.split('\n').find(line=>line.startsWith("$('useSavedReadingFreshness').onchange=")),context);const calls=requests.length;$('useSavedReadingFreshness').checked=false;$('useSavedReadingFreshness').onchange();assert.equal($('readingFreshness').disabled,false);assert.equal(requests.length,calls);assert.match($('readingHealth').textContent,/Refresh session status/);count++;
 const html=fs.readFileSync('src/BenchDouble.Host.Server/wwwroot/setup/index.html','utf8');assert.match(html,/id="useSavedReadingFreshness"[^>]*checked/);assert.match(html,/id="readingFreshness"[^>]*disabled/);
 console.log('PASS: '+count+' saved-policy editor, validation and status-routing scenarios; no device dispatch');
})().catch(error=>{console.error(error);process.exitCode=1;});
