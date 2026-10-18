// Focused page renderer checks with a detached DOM substitute; no browser or device I/O.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync('src/BenchDouble.Host.Server/wwwroot/setup/setup.js','utf8');
const start=source.indexOf('function renderReadingHealth('),end=source.indexOf('\nfunction parseIviLease',start);
assert.ok(start>=0&&end>start);
const elements=new Map();const $=id=>{if(!elements.has(id))elements.set(id,{textContent:'',className:''});return elements.get(id);};
const context={$};vm.createContext(context);vm.runInContext(source.slice(start,end),context);
let count=0;
function render(snapshot){context.renderReadingHealth(snapshot);count++;return $('readingHealth');}
let output=render({state:'Ready',heartbeat:{state:'Healthy'},readingHealth:{state:'Stale',ageMilliseconds:6000,maximumAgeMilliseconds:5000}});
assert.match(output.textContent,/Owner contact: Healthy/);assert.match(output.textContent,/Device: Ready/);assert.match(output.textContent,/Stale.*6000 ms/);assert.equal(output.className,'notice error');
output=render({state:'RecoveryRequired',heartbeat:{state:'Healthy'},readingHealth:{state:'Fresh',ageMilliseconds:0,maximumAgeMilliseconds:5000}});
assert.match(output.textContent,/Device: RecoveryRequired/);assert.equal(output.className,'notice error');
output=render({state:'Ready',readingHealth:{state:'Missing',ageMilliseconds:null,maximumAgeMilliseconds:5000}});assert.match(output.textContent,/No successful reading/);assert.equal(output.className,'notice error');
output=render({state:'Ready',readingHealth:{state:'Fresh',ageMilliseconds:0,maximumAgeMilliseconds:1}});assert.match(output.textContent,/0 ms/);assert.equal(output.className,'notice');assert.match(output.textContent,/Snapshot at refresh/);
output=render({state:'<img onerror=evil>',lastDeviceSuccessUtc:'<script>evil</script>'});assert.match(output.textContent,/<script>evil<\/script>/);assert.equal(output.innerHTML,undefined);
const html=fs.readFileSync('src/BenchDouble.Host.Server/wwwroot/setup/index.html','utf8');assert.match(html,/id="readingHealth"[^>]*aria-live="polite"/);assert.match(html,/id="readingFreshness"[^>]*min="1"[^>]*max="86400000"/);
assert.match(source,/readingFreshnessMilliseconds:freshness/);assert.match(source,/Previously displayed device and reading health are unconfirmed/);
console.log('PASS: '+count+' cached reading renderer scenarios, accessible controls and request routing');
