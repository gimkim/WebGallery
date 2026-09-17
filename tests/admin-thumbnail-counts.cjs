const fs=require('node:fs'),assert=require('node:assert/strict'),{DatabaseSync}=require('node:sqlite');
const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});const db=new DatabaseSync('App_Data/spa-smoke/gallery.db',{readOnly:true});try{
 const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto('http://127.0.0.1:5294/Account/Login');await page.locator('[name=UserName]').fill('admin');await page.locator('[name=Password]').fill(fs.readFileSync('App_Data/spa-smoke/bootstrap-admin.txt','utf8').match(/Password: (.+)/)[1].trim());await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForSelector('.directory-card');
 await page.clock.install();await page.goto('http://127.0.0.1:5294/Admin');await page.waitForSelector('.user-thumbnail-summary');
 const config=JSON.parse(fs.readFileSync('appsettings.json','utf8')).Gallery;
 const mode=await page.locator('[name=thumbnailDecodeMode]').inputValue();const signature=`${mode==='jpeg-idct'?'contain-idct-v1':'contain-v1'}|${config.ThumbnailWidth}x${config.ThumbnailHeight}|${config.ThumbnailQuality}`;
 const rows=await page.locator('.user-card').evaluateAll(cards=>cards.map(c=>({id:c.id.slice(5),total:Number(c.querySelector('[data-thumbnail-total]').dataset.thumbnailTotal),ready:Number(c.querySelector('[data-thumbnail-ready]').dataset.thumbnailReady),pending:Number(c.querySelector('[data-thumbnail-pending]').dataset.thumbnailPending)})));
 for(const row of rows){const counts=db.prepare('SELECT COUNT(*) total, COALESCE(SUM(e.ThumbnailSignature=?),0) ready FROM GalleryIndexEntries e JOIN UserRoots r ON e.RootId=r.Id WHERE e.OwnerId=? AND e.OwnerId=r.OwnerUserId AND e.IsImage=1 AND e.IsDirectory=0').get(signature,row.id);assert.equal(row.total,counts.total);assert.equal(row.ready,counts.ready);assert.equal(row.pending,row.total-row.ready);}
 const snapshot=await page.locator('.user-thumbnail-summary').allTextContents();await page.waitForTimeout(3500);assert.deepEqual(await page.locator('.user-thumbnail-summary').allTextContents(),snapshot);assert.deepEqual(errors,[]);
 const response=await page.request.get('http://127.0.0.1:5294/Admin/ThumbnailProgress');assert.equal(response.status(),200);const real=await response.json();
 for(const row of rows){assert.equal(real[row.id]?.smallReady||0,row.ready);const expected=db.prepare('SELECT COUNT(*) n FROM GalleryIndexEntries WHERE OwnerId=? AND IsImage=1 AND MediumThumbnailSignature=?').get(row.id,signature.replace(/\|\d+x\d+\|/,'|1500x1500|'));assert.equal(real[row.id]?.mediumReady||0,expected.n);}
 const anon=await browser.newContext();const denied=await anon.request.get('http://127.0.0.1:5294/Admin/ThumbnailProgress',{maxRedirects:0});assert.equal(denied.status(),302);await anon.close();
 let polls=0;await page.route('**/Admin/ThumbnailProgress',r=>{polls++;return r.fulfill({contentType:'application/json',body:JSON.stringify({[rows[0].id]:{total:100,smallReady:75,mediumReady:25}})});});
 await page.clock.runFor(15010);await page.waitForFunction(()=>document.querySelector('[data-medium-ready]').textContent==='25 created');assert.equal(polls,1);
 assert.equal(await page.locator('[data-medium-pending]').first().textContent(),'75 remaining');
 await page.evaluate(()=>Object.defineProperty(document,'visibilityState',{configurable:true,get:()=> 'hidden'}));await page.clock.runFor(30010);assert.equal(polls,1,'Hidden tabs do not poll');
 await page.locator('.user-card').first().screenshot({path:'App_Data/spa-smoke/admin-thumbnail-counts.png'});console.log(`PASS: ${rows.length} small/medium index summaries, admin-only endpoint, one poll per page not user, live updates, hidden-tab pause.`);
}finally{db.close();await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
