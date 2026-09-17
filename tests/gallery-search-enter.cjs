const fs=require('node:fs'),assert=require('node:assert/strict');
const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto('http://127.0.0.1:5294/Account/Login');
  await page.locator('[name="UserName"]').fill('admin');await page.locator('[name="Password"]').fill(fs.readFileSync('App_Data/spa-smoke/bootstrap-admin.txt','utf8').match(/Password: (.+)/)[1].trim());
  await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForSelector('[data-write-path]');
  await page.locator('.directory-card[data-name="child"] .directory-card-link').click();
  await page.waitForFunction(()=>document.querySelector('.spa-navigation-status').hidden&&document.querySelector('.file-card'));
  const url=page.url();let submits=0;page.on('request',r=>{if(r.url().includes('/DownloadSelected'))submits++;});
  for(const query of ['readme','not-a-match','']){
   await page.locator('#gallery-search').fill(query);await page.locator('#gallery-search').press('Enter');await page.waitForTimeout(100);
   assert.equal(page.url(),url);assert.equal(submits,0,'Search Enter must not download');
  }
  await page.locator('#gallery-search').fill('readme');
  await page.locator('.file-card[data-name="readme.txt"] .select-toggle').click();
  const downloadPromise=page.waitForEvent('download');await page.locator('#download-selected').click();const download=await downloadPromise;
  assert.equal(download.suggestedFilename(),'readme.txt');assert.equal(submits,1,'Explicit download still works');assert.deepEqual(errors,[]);
  console.log('PASS: real Edge SPA search Enter (matching, no matches, empty) keeps URL and sends no DownloadSelected request; explicit selected download still succeeds.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
