const fs=require('node:fs'),assert=require('node:assert/strict');
const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage(),base='http://127.0.0.1:5294';
 await page.goto(base+'/Account/Login');await page.locator('[name=UserName]').fill('admin');await page.locator('[name=Password]').fill(fs.readFileSync('App_Data/spa-smoke/bootstrap-admin.txt','utf8').match(/Password: (.+)/)[1].trim());await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForSelector('[data-write-path]');
 const root=await page.locator('[data-write-path]').getAttribute('data-write-path');await page.goto(base+'/?path='+encodeURIComponent(root+'/child'));
 const card=page.locator('.file-card[data-name="raw-fixture.RAF"]');await card.waitFor();assert.equal(await card.getAttribute('data-item-kind'),'image');
 const media=card.locator('[data-viewer-src]'),url=new URL(await media.getAttribute('data-viewer-src'),base).href,thumbnail=new URL(await media.getAttribute('data-viewer-thumbnail'),base).href;
 const thumb=await page.request.get(thumbnail);assert.equal(thumb.status(),200);assert.match(thumb.headers()['content-type'],/image\/webp/);
 const preview=await page.request.get(url);assert.equal(preview.status(),200);assert.equal(preview.headers()['x-image-source'],'embedded-raw-preview');assert.match(preview.headers()['content-type'],/image\/jpeg/);
 const original=await page.request.get(new URL(await media.getAttribute('data-download'),base).href);assert.equal(original.status(),200);assert.equal((await original.body()).length,fs.statSync('App_Data/spa-smoke/content/child/raw-fixture.RAF').size);assert.equal((await original.body()).subarray(0,8).toString(),'FUJIFILM');
 const guest=await browser.newContext();assert.equal((await guest.request.get(thumbnail)).status(),404);await guest.close();
 console.log('PASS RAW image card, authorized WebP thumbnail/JPEG preview, original RAF download, guest denied');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
