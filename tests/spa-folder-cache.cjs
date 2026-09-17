const fs=require('node:fs'), assert=require('node:assert/strict');
const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const page=await browser.newPage(), errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto('http://127.0.0.1:5294/Account/Login');
  await page.locator('[name="UserName"]').fill('admin');
  await page.locator('[name="Password"]').fill(fs.readFileSync('App_Data/spa-smoke/bootstrap-admin.txt','utf8').match(/Password: (.+)/)[1].trim());
  await page.getByRole('button',{name:'Sign in',exact:true}).click();
  await page.waitForSelector('.directory-card-link');
  await page.waitForFunction(()=>document.querySelector('.spa-navigation-status').hidden);
  const parent=page.url();
  await page.locator('.directory-card-link').first().click();await page.waitForFunction(()=>!!document.querySelector('.file-card'));
  await page.waitForFunction(()=>document.querySelector('.spa-navigation-status').hidden);
  const child=page.url(); let change=false;
  await page.route('**/*',async route=>{
   if(!route.request().headers()['x-gallery-page'])return route.continue();
   const response=await route.fetch();let data=await response.json();
   if(change && route.request().url()===child)data.html=data.html.replaceAll('readme.txt','updated.txt');
   await new Promise(r=>setTimeout(r,900));await route.fulfill({response,contentType:'application/vnd.webgallery.page+json',body:JSON.stringify(data)}).catch(()=>{});
  });
  await page.goBack();await page.waitForTimeout(100);
  assert.equal(page.url(),parent);assert(await page.locator('.directory-card-link').count());
  assert.equal(await page.locator('.spa-element-placeholder').count(),0);
  assert(!await page.locator('.spa-navigation-status').isHidden());
  await page.waitForFunction(()=>document.querySelector('.spa-navigation-status').hidden, null, {timeout:5000}).catch(async e=>{console.log(await page.locator('.spa-navigation-status').innerText());throw e;});
  await page.goForward();await page.waitForTimeout(100);
  assert.equal(page.url(),child);assert(await page.locator('.file-card').count());
  await page.evaluate(()=>window.savedCard=document.querySelector('.file-card'));
  await page.waitForFunction(()=>document.querySelector('.spa-navigation-status').hidden);
  assert(await page.evaluate(()=>savedCard===document.querySelector('.file-card')),'Unchanged data must retain DOM');
  change=true;await page.evaluate(()=>GalleryRouter.navigate(location.href));
  await page.waitForFunction(()=>document.querySelector('.spa-navigation-status').hidden);
  assert((await page.locator('.page-shell').innerText()).includes('updated.txt'),'Background response updates changed listing');
  await page.evaluate(parent=>{GalleryRouter.navigate(location.href);GalleryRouter.navigate(parent);},parent);
  await page.waitForFunction(()=>document.querySelector('.spa-navigation-status').hidden);
  assert.equal(page.url(),parent);assert(await page.locator('.directory-card-link').count());
  assert.deepEqual(errors,[]);console.log('PASS: cached Back/Forward immediately render without placeholders; unchanged DOM retained; background changes applied; stale navigation ignored.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
