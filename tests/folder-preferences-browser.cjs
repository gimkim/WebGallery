const fs=require('node:fs'),assert=require('node:assert/strict');
const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));const base='http://127.0.0.1:5294';
 await page.goto(base+'/Account/Login');await page.locator('[name=UserName]').fill('admin');await page.locator('[name=Password]').fill(fs.readFileSync('App_Data/spa-smoke/bootstrap-admin.txt','utf8').match(/Password: (.+)/)[1].trim());await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(base+'/');
 const folder=base+'/?path=%40root-1%2Fpaging-fixture';
 await page.goto(folder+'&sort=size&dir=desc');await page.goto(base+'/?path=%40root-1&sort=date&dir=asc');
 assert(!new URL(await page.locator('.directory-card-link').filter({has:page.locator('text=paging-fixture')}).getAttribute('href'),base).searchParams.has('sort'));
 await page.goto(folder);assert.equal(await page.locator('#gallery-items').getAttribute('data-sort'),'size');assert.match(await page.locator('#gallery-sort-select').inputValue(),/dir=desc/);
 const fresh=await browser.newContext({storageState:await page.context().storageState()});const p2=await fresh.newPage();await p2.goto(base+'/?path=%40root-1');assert.equal(await p2.locator('#gallery-items').getAttribute('data-sort'),'date');await fresh.close();
 await page.goto(folder+'&sort=name&dir=asc&page=2#gallery-item-500');await page.waitForTimeout(300);
 assert(!(await page.locator('[data-stream-prev] button').isVisible()));
 await page.evaluate(()=>window.scrollTo(0,document.body.scrollHeight));await page.waitForTimeout(100);await page.evaluate(()=>window.scrollTo(0,0));
 await page.waitForFunction(()=>document.querySelectorAll('.file-card').length===501);assert(await page.locator('#gallery-item-500').isVisible());
 assert.deepEqual(errors,[]);console.log('PASS folder-specific persisted sort, fresh browser context, navigation links omit inherited sort, automatic prepend');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
