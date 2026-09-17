const fs=require('node:fs'),assert=require('node:assert/strict');
const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{const b=await chromium.launch({channel:'msedge',headless:true});try{
 const p=await b.newPage(),base='http://127.0.0.1:5294';await p.goto(base+'/Account/Login');
 await p.locator('[name=UserName]').fill('admin');await p.locator('[name=Password]').fill(fs.readFileSync('App_Data/spa-smoke/bootstrap-admin.txt','utf8').match(/Password: (.+)/)[1].trim());
 await p.getByRole('button',{name:'Sign in',exact:true}).click();await p.waitForSelector('#gallery-sort-select');
 assert.equal(await p.locator('#gallery-sort-select option').count(),8);
 for(const label of ['Date Modified ↑','Date Modified ↓','Date Taken ↑','Date Taken ↓']) {
   await p.locator('#gallery-sort-select').selectOption({label});
   const field=label.startsWith('Date Taken')?'taken':'date',dir=label.endsWith('↑')?'asc':'desc';
   await p.waitForFunction(({field,dir})=>new URL(location.href).searchParams.get('sort')===field&&new URL(location.href).searchParams.get('dir')===dir,{field,dir});
   await p.waitForFunction(label=>document.querySelector('#gallery-sort-select option:checked')?.textContent.trim()===label,label);
 }
 await p.goto(base+'/');assert.equal((await p.locator('#gallery-sort-select option:checked').textContent()).trim(),'Date Taken ↓');
 console.log('PASS eight options, all four date choices through SPA, saved sort cookie on fresh navigation');
}finally{await b.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
