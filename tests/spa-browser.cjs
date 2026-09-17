const fs = require('node:fs'), assert = require('node:assert/strict');
const {chromium} = require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage(); const errors=[]; page.on('pageerror',e=>errors.push(e.message));
  await page.goto('http://127.0.0.1:5294/Account/Login');
  const api=await page.request.get('http://127.0.0.1:5294/Account/Login',{headers:{'X-Gallery-Page':'1'}});
  assert(api.headers()['content-type'].includes('application/vnd.webgallery.page+json'));
  assert((await api.json()).html.includes('Sign in'));
  const password=fs.readFileSync('App_Data/spa-smoke/bootstrap-admin.txt','utf8').match(/Password: (.+)/)[1].trim();
  await page.locator('[name="UserName"]').fill('admin'); await page.locator('[name="Password"]').fill(password);
  await page.evaluate(()=>window.testDocumentIdentity='original');
  await page.getByRole('button',{name:'Sign in',exact:true}).click();
  await page.waitForSelector('.directory-card-link');
  assert.equal(await page.evaluate(()=>window.testDocumentIdentity),'original','Login should retain document');
  await page.waitForFunction(()=>document.querySelector('.folder-navigation-loading').hidden);
  // Root discovery may be behind its first refresh; navigate again after the index settles.
  await page.waitForTimeout(1000);
  if(!await page.locator('.directory-card-link').count()) {await page.evaluate(()=>GalleryRouter.navigate(location.href));await page.waitForFunction(()=>document.querySelector('.folder-navigation-loading').hidden);}
  const parentUrl=page.url();
  await page.locator('.directory-card-link').first().click(); await page.waitForFunction(()=>document.querySelector('.file-card'));
  const childUrl=page.url(); assert.notEqual(parentUrl,childUrl);
  await page.goBack(); await page.waitForFunction(()=>document.querySelector('.folder-navigation-loading').hidden); assert.equal(page.url(),parentUrl);
  await page.goForward(); await page.waitForFunction(()=>document.querySelector('.folder-navigation-loading').hidden); assert.equal(page.url(),childUrl);
  assert.equal(await page.evaluate(()=>window.testDocumentIdentity),'original');
  await page.locator('.parent-card').click();await page.waitForFunction(()=>document.querySelector('.folder-navigation-loading').hidden);
  const [tab]=await Promise.all([page.context().waitForEvent('page'),page.locator('.directory-card-link').first().click({button:'middle'})]);
  await tab.waitForLoadState(); assert(tab.url().includes('path='));await tab.close();
  await page.getByRole('link',{name:'Collections',exact:false}).first().click();await page.waitForURL(/Collections/i);await page.waitForFunction(()=>document.querySelector('.folder-navigation-loading').hidden);
  await page.getByRole('link',{name:'Management',exact:false}).first().click();await page.waitForURL(/Admin/i);await page.waitForFunction(()=>document.querySelector('.folder-navigation-loading').hidden);
  assert.equal(await page.evaluate(()=>window.testDocumentIdentity),'original');
  let posts=0; const countPost=request=>{if(request.method()==='POST')posts++;}; page.on('request',countPost);
  page.once('dialog',dialog=>dialog.dismiss());
  await page.locator('button[data-confirm]').first().click(); await page.waitForTimeout(150);
  assert.equal(posts,0,'Dismissed destructive confirmation must not submit');page.off('request',countPost);
  assert.equal(await page.evaluate(()=>GalleryPages.active.length),2,'Only two page scopes after all navigations');
  await page.route('**/*',async route=>{if(route.request().headers()['x-gallery-page'])await new Promise(resolve=>setTimeout(resolve,1000));await route.continue().catch(()=>{});});
  await page.evaluate(()=>{GalleryRouter.invalidate();GalleryRouter.navigate('/');});
  await page.waitForFunction(()=>!document.querySelector('.folder-navigation-loading').hidden);
  await page.screenshot({path:'App_Data/spa-smoke/loading.png'});
  await page.keyboard.press('Escape');await page.waitForTimeout(1200);
  assert(page.url().includes('Admin'));assert(await page.locator('.folder-navigation-loading').isHidden());
  await page.unroute('**/*');
  await page.evaluate(()=>GalleryRouter.navigate('/'));await page.waitForFunction(()=>document.querySelector('.folder-navigation-loading').hidden);
  for (const density of [5, 10]) {
    await page.evaluate(()=>GalleryRouter.invalidate());
    await page.locator('#columns').evaluate((input,value)=>{input.value=value;input.dispatchEvent(new Event('input',{bubbles:true}));},density);
    const before=await page.locator('#gallery-items .media').first().boundingBox();
    const tracks=await page.locator('#gallery-items').evaluate(node=>getComputedStyle(node).gridTemplateColumns);
    await page.route('**/*',async route=>{if(route.request().headers()['x-gallery-page'])await new Promise(resolve=>setTimeout(resolve,800));await route.continue().catch(()=>{});});
    await page.locator('.directory-card-link').first().click();await page.waitForFunction(()=>!document.querySelector('.folder-navigation-loading').hidden);
    const after=await page.locator('#gallery-items .media').first().boundingBox();
    assert.equal(after.width,before.width);assert.equal(after.height,before.height);
    assert.equal(await page.locator('#gallery-items').evaluate(node=>getComputedStyle(node).gridTemplateColumns),tracks);
    assert.equal(await page.locator('.page-shell').evaluate(node=>node.inert),false);
    assert(await page.locator('#gallery-items .spa-element-placeholder').count()>0);
    assert((await page.locator('.spa-navigation-status').boundingBox()).width < 500);
    await page.screenshot({path:`App_Data/spa-smoke/placeholders-${density}.png`});
    await page.keyboard.press('Escape');await page.waitForTimeout(850);await page.unroute('**/*');
    assert.equal(await page.locator('.spa-element-placeholder').count(),0);
  }
  assert.deepEqual(errors,[]);
  console.log('PASS: real local MVC page JSON, SPA login POST, folder navigation, history Back/Forward, middle-click new tab, Collections/Management, stable document and no browser errors.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
