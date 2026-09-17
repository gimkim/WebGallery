const fs=require('node:fs'),assert=require('node:assert/strict');
const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{const b=await chromium.launch({channel:'msedge',headless:true});try{
 const p=await b.newPage(),base='http://127.0.0.1:5294';await p.goto(base+'/Account/Login');
 await p.locator('[name=UserName]').fill('admin');await p.locator('[name=Password]').fill(fs.readFileSync('App_Data/spa-smoke/bootstrap-admin.txt','utf8').match(/Password: (.+)/)[1].trim());
 await p.getByRole('button',{name:'Sign in',exact:true}).click();await p.waitForSelector('#gallery-sort-select');
 await p.goto(base+'/?path=%40root-1&sort=name&dir=asc');
 const button=p.locator('[data-video-name="video-thumb-fixture.mp4"]'),img=button.locator('img');await button.scrollIntoViewIfNeeded();
 await img.evaluate(el=>new Promise((resolve,reject)=>{const deadline=setTimeout(()=>reject(Error('thumbnail timeout')),20000);const timer=setInterval(()=>{if(el.naturalWidth>0){clearTimeout(deadline);clearInterval(timer);resolve();}},50);}));
 const url=new URL(await img.getAttribute('data-thumbnail-src'),base).href,response=await p.request.get(url);assert.equal(response.status(),200);assert.match(response.headers()['content-type'],/image\/webp/);
 assert.equal(await img.evaluate(el=>getComputedStyle(el).objectFit),'contain');assert(await button.locator('.video-play-icon').isVisible());
 await p.getByRole('button',{name:'List',exact:true}).click();assert(await img.isVisible());
 const guest=await b.newContext();const denied=await guest.request.get(url,{maxRedirects:0});assert.notEqual(denied.status(),200);await guest.close();
 console.log('PASS video WebP endpoint, mounted Grid/List thumbnail and play overlay, contain aspect ratio, guest denied');
}finally{await b.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
