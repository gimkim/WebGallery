const fs=require('node:fs'),assert=require('node:assert/strict');
const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{const b=await chromium.launch({channel:'msedge',headless:true});try{
 const p=await b.newPage(),base='http://127.0.0.1:5294';await p.goto(base+'/Account/Login');await p.locator('[name=UserName]').fill('admin');await p.locator('[name=Password]').fill(fs.readFileSync('App_Data/spa-smoke/bootstrap-admin.txt','utf8').match(/Password: (.+)/)[1].trim());await p.getByRole('button',{name:'Sign in',exact:true}).click();await p.waitForSelector('[data-write-path]');const root=await p.locator('[data-write-path]').getAttribute('data-write-path');const folder=root+'/paging-fixture',url=base+'/?path='+encodeURIComponent(folder)+'&sort=name&dir=asc';
 await p.goto(url);assert.equal(await p.locator('.file-card').count(),500);await p.locator('[aria-label="Folder pages"] a').filter({hasText:'Next'}).click();await p.waitForFunction(()=>document.querySelectorAll('.file-card').length===1);assert.match(p.url(),/page=2/);assert.match(await p.locator('.file-card').getAttribute('data-name'),/0500/);
 await p.goBack();await p.waitForFunction(()=>document.querySelectorAll('.file-card').length===500);
 const response=await p.request.get(url,{headers:{'X-Gallery-Page':'1'}});const body=await response.body();assert(body.length<8*1024*1024);assert.equal((await response.json()).html.match(/class="gallery-card file-card/g)?.length,500);
 await p.goto(url+'&focus='+encodeURIComponent(folder+'/file-0500.txt'));assert.equal(await p.locator('.file-card').count(),1);
 console.log('PASS 501 entries bounded to500, SPA next/back, focus selects correct page; API bytes '+body.length);
}finally{await b.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
