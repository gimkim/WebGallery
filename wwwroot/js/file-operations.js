(() => {
  // Deliberately document-scoped: page module disposal must not abort uploads.
  const base=document.body.dataset.appBase||'', batches=[];
  let activeUploads=0, renderTimer;
  const tools=()=>document.querySelector('[data-write-path]');
  const selected=()=>[...document.querySelectorAll('.file-select:checked')].map(x=>x.value);
  const token=()=>document.querySelector('input[name="__RequestVerificationToken"]')?.value||'';
  const nameOk=name=>name && new TextEncoder().encode(name).length<=240 && name.length<=240 && name===name.trim() && !/[<>:"/\\|?*%\x00-\x1f\x7f]/.test(name) && !name.startsWith('.') && !name.endsWith('.') && !/^(CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|$)|^(thumbs?\.db|system volume information)$/i.test(name);
  const label=path=>path.replace(/^@root-\d+/, 'Root');
  const tray=document.createElement('aside');tray.className='upload-tray';tray.hidden=true;tray.setAttribute('aria-label','Upload progress');document.body.append(tray);
  const el=(tag,text,cls)=>{const e=document.createElement(tag);if(text!=null)e.textContent=text;if(cls)e.className=cls;return e;};
  const button=(text,action)=>{const b=el('button',text,'button compact');b.type='button';b.onclick=action;return b;};
  const collapseKey='webgallery-upload-tray-collapsed';
  let trayCollapsed=false;
  try { trayCollapsed=localStorage.getItem(collapseKey)==='true'; } catch {}
  const trayBody=el('div',null,'upload-tray-body');trayBody.id='upload-tray-body';
  const trayTitle=el('strong','Uploads');
  const trayChevron=el('span',null,'upload-tray-chevron');trayChevron.setAttribute('aria-hidden','true');
  const trayToggle=button('',()=>{
    trayCollapsed=!trayCollapsed;
    try { localStorage.setItem(collapseKey,String(trayCollapsed)); } catch {}
    updateTrayState();
  });
  trayToggle.className='upload-tray-toggle';trayToggle.setAttribute('aria-controls',trayBody.id);
  trayToggle.append(trayTitle,trayChevron);tray.append(trayToggle,trayBody);
  function updateTrayState() {
    trayBody.hidden=trayCollapsed;tray.classList.toggle('is-collapsed',trayCollapsed);
    trayToggle.setAttribute('aria-expanded',String(!trayCollapsed));
    trayToggle.title=trayCollapsed?'Expand uploads':'Collapse uploads';
    trayChevron.textContent=trayCollapsed?'⌃':'⌄';
  }
  updateTrayState();
  const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
  async function api(action,data,auth=token(),signal) {
    const response=await fetch(base+'/Files/'+action,{method:data===undefined?'GET':'POST',credentials:'same-origin',cache:'no-store',signal,
      headers:{'Content-Type':'application/json','RequestVerificationToken':auth},body:data===undefined?undefined:JSON.stringify(data)});
    let value;try{value=await response.json();}catch{throw new Error('Session expired or server unavailable. Sign in and try again.');}
    if(!response.ok) {const error=new Error(value.error||`HTTP ${response.status}`);error.status=response.status;throw error;}return value;
  }
  function changed() {window.GalleryRouter?.invalidate(); if(tools()&&!document.querySelector('dialog[open]')&&!selected().length)window.GalleryRouter?.navigate(location.href);}
  function announce(text) {const note=el('div',text,'file-notice');note.setAttribute('role','status');note.append(button('Dismiss',()=>note.remove()));document.body.append(note);setTimeout(()=>note.remove(),12000);}
  async function ask(title,message,entry=false) {
    return new Promise(resolve=>{
      const d=el('dialog',null,'file-dialog');d.append(el('h2',title),el('p',message));
      const input=el('input');if(entry){input.maxLength=240;input.setAttribute('aria-label','Folder name');d.append(input);}
      const done=value=>{d.close();d.remove();resolve(value);};
      d.append(button('Cancel',()=>done(null)),button(entry?'Create folder':'Confirm',()=>done(entry?input.value:true)));
      d.addEventListener('cancel',e=>{e.preventDefault();done(null);});document.body.append(d);d.showModal();if(entry)input.focus();
    });
  }
  async function move(paths,destination,source,folder=false) {
    if(!paths.length || paths.every(path=>path.slice(0,path.lastIndexOf('/'))===destination))return;
    if(folder&&paths.some(path=>destination===path||destination.startsWith(path+'/'))){announce('A folder cannot be moved into itself or its descendants.');return;}
    if(!await ask('Confirm move',`Move ${folder?'folder '+label(paths[0])+' and all its contents':paths.length+' file(s)'} from ${label(source)} to ${label(destination)}? Existing items will not be replaced. Links to the old location are not updated.`))return;
    try{const result=await api('Move',{paths,destination});announce(result.error||`Moved ${result.moved.length} item(s).`);document.querySelector('#clear-selection')?.click();changed();}catch(e){announce(e.message);}
  }
  async function chooseDestination() {
    const paths=selected(),current=tools()?.dataset.writePath;if(!paths.length||!current)return;
    const d=el('dialog',null,'file-dialog move-picker'),tree=el('div',null,'folder-tree'),status=el('p','Choose a destination.');
    let destination=current;const confirm=button('Move here',()=>{d.close();d.remove();move(paths,destination,current);});confirm.hidden=true;
    d.append(el('h2','Move files to…'),tree,status,button('Cancel',()=>d.close()),confirm);document.body.append(d);d.showModal();d.addEventListener('close',()=>d.remove());
    async function branch(path,name,parent,expand=false) {
      const wrap=el('div'),row=el('div',null,'folder-tree-row'),children=el('div',null,'folder-tree-children');children.hidden=true;let loaded=false;
      const pick=button(name,()=>{destination=path;status.textContent=label(path);confirm.hidden=path===current;tree.querySelectorAll('.chosen').forEach(x=>x.classList.remove('chosen'));pick.classList.add('chosen');});
      if(path===current)pick.classList.add('chosen');
      const toggle=button('▸',async()=>{try{await open();}catch(e){status.textContent=e.message;}});toggle.setAttribute('aria-label',`Expand ${name}`);toggle.setAttribute('aria-expanded','false');
      row.append(toggle,pick);wrap.append(row,children);parent.append(wrap);
      async function open(force=false) {
        children.hidden=force?false:!children.hidden;toggle.textContent=children.hidden?'▸':'▾';toggle.setAttribute('aria-expanded',String(!children.hidden));
        if(!loaded&&!children.hidden){loaded=true;try{const result=await api('Folders?path='+encodeURIComponent(path));for(const item of result.children)await branch(item.path,item.name,children,current===item.path||current.startsWith(item.path+'/'));}catch(e){loaded=false;throw e;}}
      }
      if(expand)await open(true);
    }
    try{const root=await api('Folders');await branch(root.root,root.rootName,tree,true);}catch(e){status.textContent=e.message;}
  }
  function render() { if(!renderTimer)renderTimer=setTimeout(()=>{renderTimer=null;draw();},80); }
  function draw() {
    trayBody.replaceChildren();tray.hidden=!batches.length;if(!batches.length)return;
    const allJobs=batches.flatMap(batch=>batch.jobs);
    trayTitle.textContent=`Uploads · ${allJobs.filter(job=>job.state==='Complete').length}/${allJobs.length}`;
    updateTrayState();
    for(const batch of batches){
      const group=el('section',null,'upload-batch'),head=el('div',null,'upload-batch-heading');
      const done=batch.jobs.filter(j=>j.state==='Complete').length,finished=batch.finished;
      head.append(el('strong',`${batch.name} · ${done}/${batch.jobs.length}`),button(finished?'Dismiss':'Cancel batch',()=>{if(finished){batches.splice(batches.indexOf(batch),1);render();}else{batch.cancelled=true;batch.jobs.forEach(cancel);render();}}));
      group.append(head,el('small',`To ${label(batch.path)}`));
      const visibleJobs=new Set(batch.jobs.slice(0,batch.visible||100));
      batch.jobs.filter(job=>!['Queued','Complete','Cancelled','Failed'].includes(job.state)).forEach(job=>visibleJobs.add(job));
      for(const job of visibleJobs){
        const row=el('div',null,'upload-job');row.append(el('span',job.relative));
        const p=el('progress');p.max=job.size||1;p.value=job.loaded||0;row.append(p);
        row.append(el('small',`${job.state} · ${format(job.loaded||0)} / ${format(job.size||0)}${job.error?' — '+job.error:''}`));
        if(!['Complete','Cancelled','Failed'].includes(job.state))row.append(button('Cancel',()=>cancel(job)));group.append(row);
      }
      if(batch.jobs.length>(batch.visible||100))group.append(button('Show more files',()=>{batch.visible=(batch.visible||100)+100;render();}));
      if(batch.error)group.append(el('p',batch.error));trayBody.append(group);
    }
  }
  const format=n=>n<1024?`${n} B`:n<1048576?`${(n/1024).toFixed(1)} KiB`:`${(n/1048576).toFixed(1)} MiB`;
  function cancel(job){if(job.state==='Complete')return;job.cancelled=true;job.xhr?.abort();job.controller?.abort();job.state='Cancelled';if(job.id)api('Cancel?id='+job.id,{},job.auth).catch(()=>{});render();}
  function chunk(job,offset,blob) {
    return new Promise((resolve,reject)=>{
      const xhr=job.xhr=new XMLHttpRequest();xhr.open('POST',base+`/Files/Chunk?id=${job.id}&offset=${offset}`);xhr.timeout=120000;
      xhr.setRequestHeader('RequestVerificationToken',job.auth);xhr.setRequestHeader('Content-Type','application/octet-stream');
      xhr.upload.onprogress=e=>{job.loaded=offset+e.loaded;render();};
      xhr.onload=()=>{try{const body=JSON.parse(xhr.responseText);if(xhr.status<200||xhr.status>=300){const e=new Error(body.error||`HTTP ${xhr.status}`);e.status=xhr.status;reject(e);}else resolve(body);}catch{reject(new Error('Invalid server response.'));}};
      xhr.onerror=()=>reject(new Error('Network disconnected.'));xhr.ontimeout=()=>reject(new Error('Network timeout.'));xhr.onabort=()=>reject(new Error('Cancelled.'));xhr.send(blob);
    });
  }
  async function upload(job,batch) {
    while(activeUploads>=2&&!job.cancelled&&!batch.cancelled)await wait(100);
    if(job.cancelled||batch.cancelled)return;
    activeUploads++;
    job.auth=batch.auth;job.controller=new AbortController();
    try {
      if(job.cancelled||batch.cancelled)return;
      job.state='Starting';render();
      const started=await api('Start',{path:batch.path,relativePath:job.relative,size:job.file.size},job.auth,job.controller.signal);
      job.id=started.id;if(job.cancelled){cancel(job);return;}let offset=started.offset,retries=0;
      while(offset<job.file.size){
        if(job.cancelled||batch.cancelled)return;
        try{job.state='Uploading';const result=await chunk(job,offset,job.file.slice(offset,offset+started.chunkSize));offset=result.offset;job.loaded=offset;retries=0;}
        catch(e){
          if(job.cancelled)return;if(e.status&&e.status<500&&e.status!==409)throw e;if(++retries>5)throw e;
          job.state=`Retrying (${retries}/5)`;render();await wait(Math.min(16000,1000*2**(retries-1)));
          if(job.cancelled)return;
          try{const state=await api('Status?id='+job.id,undefined,job.auth,job.controller.signal);if(state.cancelled)throw new Error('Upload cancelled.');offset=state.offset;job.loaded=offset;}catch(e){if(e.status&&e.status<500)throw e;}
        }
      }
      job.state='Saving / creating thumbnail';render();let result;
      for(let retry=0;;retry++){
        if(job.cancelled)return;
        try{result=await api('Complete?id='+job.id,{},job.auth,job.controller.signal);break;}
        catch(e){if(job.cancelled)return;if(retry>=5||(e.status&&e.status<500))throw e;job.state='Retrying completion';render();await wait(Math.min(16000,1000*2**retry));}
      }
      job.loaded=job.file.size;job.state='Complete';job.error=result.warning;job.file=null;
    }catch(e){if(!job.cancelled){job.state='Failed';job.error=e.message;if(job.id)api('Cancel?id='+job.id,{},job.auth).catch(()=>{});}}
    finally{activeUploads--;render();}
  }
  async function queue(files,folders,path,name) {
    if(!tools())return;
    const batch={name,path,auth:token(),jobs:files.map(x=>({...x,size:x.file.size,state:'Queued',loaded:0})),cancelled:false};batches.push(batch);render();
    try{
      for(const item of [...files.map(x=>x.relative),...folders])if(!item.split('/').every(nameOk))throw new Error(`Invalid or reserved name: ${item}`);
      // Preserve empty directories. Existing parents are safe to merge; files never overwrite.
      for(const folder of [...new Set(folders)].sort((a,b)=>a.split('/').length-b.split('/').length)){
        if(batch.cancelled)break;const i=folder.lastIndexOf('/'),parent=i<0?path:path+'/'+folder.slice(0,i),name=folder.slice(i+1);
        await api('EnsureUploadFolder',{path:parent,name},batch.auth);
      }
      let cursor=0;await Promise.all(Array.from({length:2},async()=>{while(cursor<batch.jobs.length&&!batch.cancelled)await upload(batch.jobs[cursor++],batch);}));
    }catch(e){batch.error=e.message;batch.jobs.filter(x=>x.state==='Queued').forEach(x=>{x.state='Failed';x.error=e.message;});}
    batch.finished=true;render();announce(batch.cancelled?'Upload batch cancelled. Completed files were kept.':batch.error||batch.jobs.some(x=>x.state==='Failed')?'Upload batch finished with errors. See upload details.':'All uploads completed.');changed();
  }
  async function dropped(entries,fallback,path) {
    const files=[],folders=[];
    async function walk(entry,prefix=''){
      if(!nameOk(entry.name))throw new Error(`Invalid or reserved name: ${entry.name}`);
      const relative=prefix+entry.name;
      if(entry.isFile){const file=await new Promise((yes,no)=>entry.file(yes,no));files.push({file,relative});}
      else if(entry.isDirectory){folders.push(relative);const reader=entry.createReader();for(;;){const list=await new Promise((yes,no)=>reader.readEntries(yes,no));if(!list.length)break;for(const child of list)await walk(child,relative+'/');}}
      if(files.length+folders.length>10000)throw new Error('A batch can contain at most 10,000 entries.');
    }
    try{if(entries.length)for(const entry of entries)await walk(entry);else for(const file of fallback)files.push({file,relative:file.webkitRelativePath||file.name});await queue(files,folders,path,entries.length===1&&entries[0].isDirectory?entries[0].name:`${files.length} files`);}catch(e){announce(e.message);}
  }
  document.addEventListener('click',async event=>{
    const action=event.target.closest('[data-file-action]')?.dataset.fileAction;if(!action||!tools())return;event.preventDefault();
    const path=tools().dataset.writePath;
    if(action==='move'){await chooseDestination();return;}
    if(action==='create'){const name=await ask('Create folder',`Create a folder in ${label(path)}`,true);if(name===null)return;
      try{if(!nameOk(name))throw new Error('Invalid or reserved folder name.');await api('CreateFolder',{path,name});announce('Folder created.');changed();}catch(e){announce(e.message);}return;}
    if(action==='upload'){const input=el('input');input.type='file';input.multiple=true;input.onchange=()=>queue([...input.files].map(file=>({file,relative:file.name})),[],path,`${input.files.length} files`);input.click();}
  });
  let dragging=[],source='',highlight,draggingFolder=false,internalDrag=false;
  const dragPreview=el('div',null,'file-drag-preview');dragPreview.hidden=true;document.body.append(dragPreview);
  // Chromium native drag hit-testing walks large image grids even when dragover is cheap.
  // Hit-test one viewport surface instead; resolve semantic targets from a geometry snapshot.
  const dragSurface=el('div',null,'file-drag-surface');dragSurface.hidden=true;document.body.append(dragSurface);
  let dragTargets=[],dragGeometryDirty=false;
  const targetSelector='.directory-card, .parent-card, .breadcrumbs a, .breadcrumbs [data-drop-path], #gallery-items, .empty-state, .file-write-tools';
  function snapshotDragTargets(){
    dragTargets=[...document.querySelectorAll(targetSelector)].map(node=>({node,rect:node.getBoundingClientRect()}));
    // Specific folders/breadcrumbs precede the large current-folder fallback.
    dragTargets.sort((a,b)=>Number(a.node.matches('#gallery-items, .empty-state, .file-write-tools'))-Number(b.node.matches('#gallery-items, .empty-state, .file-write-tools')));
    dragGeometryDirty=false;
  }
  function eventDropTarget(event){
    if(dragSurface.hidden)return dropTarget(event.target);
    if(dragGeometryDirty)snapshotDragTargets();
    const match=dragTargets.find(({rect:r})=>event.clientX>=r.left&&event.clientX<r.right&&event.clientY>=r.top&&event.clientY<r.bottom);
    return match?dropTarget(match.node):null;
  }
  function endDragSurface(){dragSurface.hidden=true;dragTargets=[];}
  document.addEventListener('scroll',()=>{dragGeometryDirty=true;},true);
  window.addEventListener('resize',()=>{dragGeometryDirty=true;});
  document.addEventListener('gallery:navigation-start',endDragSurface);
  window.addEventListener('pagehide',endDragSurface);
  const setHighlight=node=>{
    if(highlight===node)return;
    highlight?.classList.remove('file-drop-target');highlight=node;
    highlight?.classList.add('file-drop-target');
  };
  const clearHighlight=()=>setHighlight(null);
  function dropTarget(node){
    const t=tools();if(!t)return null;
    const target=node.closest('.directory-card, .parent-card, .breadcrumbs a, .breadcrumbs [data-drop-path], #gallery-items, .empty-state, .file-write-tools');if(!target)return null;
    const href=target.matches('.parent-card, .breadcrumbs a')?target.href:null;
    const path=target.dataset.relativePath??target.dataset.dropPath??(href?new URL(href).searchParams.get('path'):null)??t.dataset.writePath;
    return {node:target,path:path||t.dataset.writePath};
  }
  const prepare=()=>{
    const writable=!!tools();
    document.querySelectorAll('.gallery-card img, .gallery-card a').forEach(node=>{node.draggable=false;});
    document.querySelectorAll('.file-card, .directory-card').forEach(card=>{card.draggable=writable;});
  };prepare();document.addEventListener('gallery:navigation-end',prepare);document.addEventListener('gallery:items-added',prepare);
  document.addEventListener('dragstart',e=>{
    dragging=[];draggingFolder=false;internalDrag=true;
    const card=e.target.closest('.file-card, .directory-card');
    if(!card||!tools()){if(card||e.target.closest('img')){e.preventDefault();internalDrag=false;}return;}
    draggingFolder=card.matches('.directory-card');
    if(draggingFolder)dragging=[card.dataset.relativePath];
    else{dragging=selected();if(!card.querySelector('.file-select:checked')){e.preventDefault();dragging=[];internalDrag=false;return;}}
    source=tools().dataset.writePath;
    snapshotDragTargets();
    e.dataTransfer.clearData(); // Never let native image/URI payload become an upload.
    e.dataTransfer.setData('application/x-webgallery-files','move');e.dataTransfer.effectAllowed='move';
    // Keep the native drag ghost independent of a live, image-heavy grid card.
    dragPreview.textContent=draggingFolder?`Move folder: ${card.dataset.name||dragging[0].split('/').pop()}`:`Move ${dragging.length} file(s)`;
    dragPreview.hidden=false;e.dataTransfer.setDragImage(dragPreview,16,16);
    setTimeout(()=>{dragPreview.hidden=true;if(internalDrag&&dragging.length)dragSurface.hidden=false;},0);
  },true);
  document.addEventListener('dragover',e=>{if(!dragging.length&&!e.dataTransfer.types.includes('Files'))return;e.preventDefault();const target=eventDropTarget(e);setHighlight(target?.node||null);e.dataTransfer.dropEffect=target?(dragging.length?'move':'copy'):'none';});
  document.addEventListener('dragleave',e=>{if(!e.relatedTarget)clearHighlight();});document.addEventListener('dragend',()=>{dragging=[];internalDrag=false;clearHighlight();endDragSurface();});
  document.addEventListener('drop',e=>{
    if(!dragging.length&&!e.dataTransfer.types.includes('Files'))return;e.preventDefault();const target=eventDropTarget(e);clearHighlight();endDragSurface();if(!target)return;
    if(dragging.length){const paths=dragging;dragging=[];move(paths,target.path,source,draggingFolder);}
    else if(!internalDrag&&!e.dataTransfer.types.includes('application/x-webgallery-files')){const entries=[...e.dataTransfer.items].map(item=>item.webkitGetAsEntry?.()).filter(Boolean),fallback=[...e.dataTransfer.files];dropped(entries,fallback,target.path);}
  });
  document.addEventListener('submit',e=>{if(/\/Account\/Logout/i.test(e.target.action)){for(const batch of batches){batch.cancelled=true;batch.jobs.forEach(cancel);}batches.length=0;render();}},true);
  window.addEventListener('beforeunload',e=>{if(batches.some(b=>!b.finished)){e.preventDefault();e.returnValue='';}});
})();
