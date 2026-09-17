const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
const assert=require('node:assert/strict');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const page=await browser.newPage();
  await page.route('http://fixture/**',route=>route.fulfill({contentType:'text/html',body:'<html><body></body></html>'}));
  const mount=async()=>{await page.goto('http://fixture/');await page.addStyleTag({path:'wwwroot/css/site.css'});await page.addScriptTag({path:'wwwroot/js/file-operations.js'});await page.evaluate(()=>document.querySelector('.upload-tray').hidden=false);};
  await mount();
  const toggle=page.locator('.upload-tray-toggle');
  assert.equal(await toggle.getAttribute('aria-expanded'),'true');
  await toggle.click();assert.equal(await toggle.getAttribute('aria-expanded'),'false');
  assert.equal(await page.locator('.upload-tray-body').isVisible(),false);
  await mount();assert.equal(await toggle.getAttribute('aria-expanded'),'false');
  await toggle.focus();await page.keyboard.press('Enter');assert.equal(await toggle.getAttribute('aria-expanded'),'true');
  await mount();assert.equal(await toggle.getAttribute('aria-expanded'),'true');
  console.log('PASS collapse/expand, keyboard and remembered state across document reload');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
