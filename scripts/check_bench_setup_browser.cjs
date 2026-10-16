/* End-to-end setup verification against a disposable local host and configuration file.
 * Build the host first. Requires Playwright; BENCH_SETUP_DOTNET optionally selects dotnet.
 * PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH optionally selects an installed Chromium build.
 */
const { chromium } = require('playwright');
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
(async () => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'bench-setup-browser-'));
  const hostRoot = path.join(root, 'src/BenchDouble.Host.Server');
  const child = spawn(process.env.BENCH_SETUP_DOTNET || 'dotnet',
    [path.join(hostRoot, 'bin/Release/net10.0/BenchDouble.Host.Server.dll')], {
      cwd: hostRoot, env: {...process.env, BenchSetup__Path: path.join(temporary, 'configuration.json')}, stdio: ['ignore', 'pipe', 'pipe']
    });
  let hostOutput = '', browser;
  child.stdout.on('data', x => hostOutput += x); child.stderr.on('data', x => hostOutput += x);
  child.on('error', error => hostOutput += error.message);
  try {
    let ready = false;
    for (let attempt = 0; attempt < 100; attempt++) {
      if (child.exitCode !== null) throw Error('Host exited: ' + hostOutput);
      try { ready = (await fetch('http://127.0.0.1:5278/health')).ok; } catch {}
      if (ready) break;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.ok(ready, 'Host did not start: ' + hostOutput);
    browser = await chromium.launch({headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || undefined});
    const page = await browser.newPage({baseURL: 'http://127.0.0.1:5278', viewport: {width: 1365, height: 1000}});
    const pageErrors = []; page.on('pageerror', error => pageErrors.push(error.message));
    await page.goto('http://127.0.0.1:5278/setup/');
    await page.getByText('No saved configuration. Start with discovery or manual entry.').waitFor();
    await page.getByRole('button', {name:'Discover local host'}).click();
    await page.getByText('3 endpoints · 2 controllers').waitFor();
    for (let index=0; index<3; index++) await page.getByRole('button', {name:'Add connection', exact:true}).nth(index).click();
    await page.locator('#controllers input[type=checkbox]').nth(0).check();
    await page.locator('#controllers input[type=checkbox]').nth(1).check();
    await page.getByLabel('I confirm these connections belong to the same physical instrument').check();
    await page.locator('#roleName').fill('voltmeter');
    await page.locator('#roleInstrument').selectOption('DMM1');
    await page.getByRole('button', {name:'Assign role',exact:true}).click();
    await page.getByRole('button', {name:'Validate connections',exact:true}).click();
    await page.locator('#validation .pass').waitFor();
    await page.getByRole('button', {name:'Review changes'}).click();
    await page.getByLabel('I reviewed these instrument and controller mappings.').check();
    await page.getByRole('button', {name:'Save reviewed configuration'}).click();
    await page.getByText('Validated configuration saved. Existing leases retain their original route.').waitFor();
    const saved = await (await page.request.get('/api/v1/setup/configuration')).json();
    assert.equal(saved.draft, false); assert.equal(saved.configuration.instruments.length,2);
    assert.ok(saved.configuration.instruments.every(x=>x.expectedIdentity===null));
    const leaseResponse = await page.request.post('/api/v1/setup/roles/voltmeter/reserve');
    assert.equal(leaseResponse.status(),200);const lease=await leaseResponse.json();
    assert.equal((await page.request.post('/api/v1/reservations',{data:{resourceId:'DMM1'}})).status(),409);
    await page.getByRole('button', {name:'Validate connections',exact:true}).click();
    await page.getByText('DMM1: Instrument is reserved or unavailable. Wait for its owner; validation has not queried it.').waitFor();
    await page.request.delete('/api/v1/reservations/'+lease.leaseId);
    // Human-readable instrument names are rendered as text, never interpreted as markup.
    await page.locator('#instruments').getByLabel('Friendly name').first().fill('<img src=x onerror=alert(1)>');
    await page.locator('#instruments').getByLabel('Friendly name').first().press('Tab');
    await page.getByRole('button',{name:'Review changes'}).click();
    assert.equal(await page.locator('#after img').count(),0);
    await page.getByRole('button',{name:'Cancel edits'}).click();
    assert.equal((await (await page.request.get('/api/v1/setup/configuration')).json()).revision,saved.revision);
    // Native provider enumeration has an explicit transport/host scope and reports missing runtimes.
    await page.locator('#discoverySource').selectOption('USB');
    await page.getByRole('button',{name:'Discover local host'}).click();
    await page.locator('#diagnostics .notice').first().waitFor();
    assert.equal(pageErrors.length,0,pageErrors.join('\n'));
    await page.locator('#discoverySource').selectOption('simulation');
    await page.getByRole('button',{name:'Discover local host'}).click();
    await page.getByText('3 endpoints · 2 controllers').waitFor();
    fs.mkdirSync(path.join(root,'build'),{recursive:true});
    await page.screenshot({path:path.join(root,'build/bench-setup-desktop.png'),fullPage:true});
    await page.setViewportSize({width:390,height:844});
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth <= innerWidth+1),'Mobile horizontal overflow');
    await page.screenshot({path:path.join(root,'build/bench-setup-mobile.png'),fullPage:true});
    console.log('PASS: discovery, two controllers, no-serial setup, association, validation, save, lease contention, cancellation of edits, safe text rendering, native discovery UI, mobile layout.');
  } finally {
    if(browser) await browser.close();
    child.kill('SIGTERM');
    await new Promise(resolve=>{if(child.exitCode!==null)resolve();else child.once('exit',resolve);});
    fs.rmSync(temporary,{recursive:true,force:true});
  }
})().catch(error=>{console.error(error);process.exitCode=1;});
