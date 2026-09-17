const fs=require('node:fs'),assert=require('node:assert/strict');
const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const page=await browser.newPage({viewport:{width:1800,height:1000}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto('http://127.0.0.1:5294/Account/Login');
  const images=await page.evaluate(()=>[480,1500,2400].map(w=>{const c=document.createElement('canvas');c.width=w;c.height=w*2/3;c.getContext('2d').fillRect(0,0,c.width,c.height);return c.toDataURL('image/webp').split(',')[1];}));
  let mediumDelay=650,fullDelay=1800,mediumRequests=0;
  await page.route(/\/Thumbnail\?/,async route=>{
   const medium=new URL(route.request().url()).searchParams.get('medium')==='true';
   if(medium){mediumRequests++;await new Promise(r=>setTimeout(r,mediumDelay));}
   await route.fulfill({status:200,contentType:'image/webp',body:Buffer.from(images[medium?1:0],'base64')}).catch(()=>{});
  });
  await page.route(/\/ViewFile\?/,async route=>{await new Promise(r=>setTimeout(r,fullDelay));await route.fulfill({status:200,contentType:'image/webp',headers:{'X-Image-Width':'2400','X-Image-Height':'1600'},body:Buffer.from(images[2],'base64')}).catch(()=>{});});
  await page.evaluate(()=>localStorage.setItem('gim-gallery-columns','10'));
  await page.locator('[name="UserName"]').fill('admin');
  await page.locator('[name="Password"]').fill(fs.readFileSync('App_Data/spa-smoke/bootstrap-admin.txt','utf8').match(/Password: (.+)/)[1].trim());
  await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForSelector('.directory-card-link');
  for(let depth=0;depth<3 && !await page.locator('.image-button').count();depth++) {const before=page.url();await page.locator('.directory-card-link').first().click();await page.waitForURL(url=>url.href!==before);await page.waitForFunction(()=>document.querySelector('.folder-navigation-loading').hidden);}
  await page.waitForFunction(()=>document.querySelector('.image-button img')?.naturalWidth===480);
  await page.locator('.image-button').first().dblclick();
  await page.waitForFunction(()=>document.querySelector('#viewer-image').naturalWidth===480);
  await page.waitForFunction(()=>document.querySelector('#viewer-image').naturalWidth===1500);
  await page.waitForFunction(()=>document.querySelector('#viewer-image').naturalWidth===2400);
  console.log('PASS small -> medium -> full viewer progression');
  await page.keyboard.press('Escape');
  mediumDelay=1800;fullDelay=100;
  await page.reload();await page.waitForFunction(()=>document.querySelector('.image-button img')?.naturalWidth===480);
  await page.locator('.image-button').first().dblclick();await page.waitForFunction(()=>document.querySelector('#viewer-image').naturalWidth===2400);
  await page.waitForTimeout(2000);assert.equal(await page.locator('#viewer-image').evaluate(n=>n.naturalWidth),2400);
  console.log('PASS late medium never replaces completed full image');
  await page.keyboard.press('Escape');mediumDelay=150;
  await page.locator('#columns').evaluate(n=>{n.value='2';n.dispatchEvent(new Event('input',{bubbles:true}));});
  await page.waitForFunction(()=>document.querySelector('.image-button img')?.naturalWidth===1500);
  assert(mediumRequests>=3);assert.deepEqual(errors,[]);
  console.log('PASS density resize upgrades oversized visible thumbnail, no browser errors');
  await page.unrouteAll({behavior:'wait'});
  const realUrl=await page.locator('.image-button').first().getAttribute('data-viewer-thumbnail');
  const mediumUrl=new URL(realUrl,page.url());mediumUrl.searchParams.set('medium','true');
  const actual=await page.request.get(mediumUrl.href);assert.equal(actual.status(),200);
  assert(actual.headers()['content-type'].includes('image/webp'));
  assert(actual.headers()['cache-control'].includes('immutable'));
  const dimensions=await page.evaluate(async data=>{const i=new Image();i.src='data:image/webp;base64,'+data;await i.decode();return [i.naturalWidth,i.naturalHeight];},(await actual.body()).toString('base64'));
  assert.equal(Math.max(...dimensions),1500);
  const anonymous=await browser.newContext();const denied=await anonymous.request.get(mediumUrl.href);assert.equal(denied.status(),404);await anonymous.close();
  console.log('PASS real authorized medium endpoint via live remote service, 1500px WebP, immutable cache, anonymous denial');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
