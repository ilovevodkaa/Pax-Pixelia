'use strict';
/* Input, tooltips, province panel, time/events, notifications, boot. */
/* ---------- interaction ---------- */
function worldAt(sx,sy){const z=cam.z,WZ=WW*z;let a=(cam.px+sx)%WZ;if(a<0)a+=WZ;const wx=(a/z)|0,b=cam.py+sy;if(b<0||b>=WH*z)return -1;return prov[((b/z)|0)*WW+wx]}
function zoomAt(dir,sx,sy){const i=ZL.indexOf(cam.z),ni=Math.max(0,Math.min(ZL.length-1,i+dir));if(ni===i)return;const oz=cam.z,fx=(cam.px+sx)/oz,fy=(cam.py+sy)/oz;cam.z=ZL[ni];cam.px=Math.round(fx*cam.z-sx);cam.py=Math.round(fy*cam.z-sy);clampCam();$('zl').textContent='×'+cam.z;dirtyMap=true;drawMiniView()}
let drag=null;
cv.addEventListener('mousedown',e=>{if(e.button!==0)return;drag={x:e.clientX,y:e.clientY,px:cam.px,py:cam.py,moved:false}});
addEventListener('mousemove',e=>{
  if(drag){const dx=e.clientX-drag.x,dy=e.clientY-drag.y;if(!drag.moved&&Math.abs(dx)+Math.abs(dy)>4){drag.moved=true;cv.classList.add('drag')}
    if(drag.moved){cam.px=drag.px-dx;cam.py=drag.py-dy;clampCam();dirtyMap=true;hideTip();drawMiniView();return}}
  const tipEl=e.target.closest&&e.target.closest('[data-tip]');
  if(tipEl){if(hov!==-1){hov=-1;dirtyMap=true}showTip(tipEl.dataset.tip,e.clientX,e.clientY);return}
  if(e.target===cv){const p=worldAt(e.clientX,e.clientY),hp=p>=0&&fogOn&&!fog[p]?-1:p;if(hp!==hov){hov=hp;dirtyMap=true}if(p>=0)showTip(tipFor(p),e.clientX,e.clientY);else hideTip()}else hideTip()});
addEventListener('mouseup',e=>{if(drag&&!drag.moved&&e.target===cv){const p=worldAt(e.clientX,e.clientY);if(p>=0){if(scoutPick)pickScout(p);else{sel=fogOn&&!fog[p]?-1:p;openPanel(p);dirtyMap=true}}}drag=null;cv.classList.remove('drag')});
cv.addEventListener('mouseleave',()=>{hov=-1;dirtyMap=true;hideTip()});
cv.addEventListener('wheel',e=>{e.preventDefault();zoomAt(e.deltaY<0?1:-1,e.clientX,e.clientY)},{passive:false});
addEventListener('keydown',e=>{const k=e.key.toLowerCase();const pan=90;
  if(k===' '){e.preventDefault();togglePause()}else if('12345'.includes(k)&&k){speed=+k;renderSpeed()}
  else if(k==='arrowleft'||k==='a'||k==='ф'){cam.px-=pan}else if(k==='arrowright'||k==='d'||k==='в'){cam.px+=pan}else if(k==='arrowup'||k==='w'||k==='ц'){cam.py-=pan}else if(k==='arrowdown'||k==='s'||k==='ы'){cam.py+=pan}
  else if(k==='+'||k==='='){zoomAt(1,cv.width/2,cv.height/2);return}else if(k==='-'){zoomAt(-1,cv.width/2,cv.height/2);return}else if(k==='escape'){if(scoutPick)cancelPick(true);else closePanel();return}else return;
  clampCam();dirtyMap=true;drawMiniView()});
$('zin').onclick=()=>zoomAt(1,cv.width/2,cv.height/2);$('zout').onclick=()=>zoomAt(-1,cv.width/2,cv.height/2);
mcv.addEventListener('click',e=>{const r=mcv.getBoundingClientRect(),wx=(e.clientX-r.left)/r.width*WW,wy=(e.clientY-r.top)/r.height*WH;cam.px=Math.round(wx*cam.z-cv.width/2);cam.py=Math.round(wy*cam.z-cv.height/2);clampCam();dirtyMap=true;drawMiniView()});
document.querySelectorAll('#modes [data-m]').forEach(b=>b.onclick=()=>{mode=b.dataset.m;document.querySelectorAll('#modes [data-m]').forEach(x=>x.classList.toggle('on',x===b));computeMode()});
function syncFogBtn(){const b=$('fogbtn');b.classList.toggle('on',!fogOn);b.innerHTML=`<i class="ti ti-${fogOn?'eye-off':'eye'}"></i>`;
  b.dataset.tip=fogOn?"<b>Туман войны</b><br>Видны только разведанные земли<br><span class='mu'>Нажмите — режим наблюдателя</span>":"<b>Режим наблюдателя</b><br>Туман войны выключен, видна вся карта<br><span class='mu'>Нажмите, чтобы вернуть туман</span>"}
$('fogbtn').onclick=e=>{fogOn=!fogOn;syncFogBtn();if(fogOn&&sel>=0&&!fog[sel])closePanel();computeMode();renderLead();dirtyOv=true;if(!panel.hidden&&panP>=0)renderPanel(panP);
  showTip($('fogbtn').dataset.tip,e.clientX,e.clientY);toast(fogOn?'Туман войны включён':'Режим наблюдателя: туман войны выключен')};
document.querySelectorAll('[data-screen]').forEach(b=>b.onclick=()=>toast(`Экран «${b.dataset.screen}» — нарисуем следующим макетом`));
function placeLead(){const l=$('lead');if(l.hidden)return;const r=$('leadbtn').getBoundingClientRect(),lx=Math.max(8,Math.min(innerWidth-310,r.left-140));l.style.left=lx+'px';l.style.setProperty('--ax',(r.left+r.width/2-lx)+'px')}
$('leadbtn').onclick=e=>{const l=$('lead');l.hidden=!l.hidden;$('leadbtn').classList.toggle('on',!l.hidden);placeLead();renderLead();dirtyOv=true};
$('regen').onclick=()=>{S=(Math.random()*1e6)|0;boot()};

/* ---------- tooltips / panel ---------- */
const tip=$('tip');
function showTip(h,x,y){tip.innerHTML=h;tip.hidden=false;const w=tip.offsetWidth,hh=tip.offsetHeight;let lx=x+16,ly=y+18;if(lx+w>innerWidth-8)lx=x-w-12;if(ly+hh>innerHeight-8)ly=y-hh-12;tip.style.left=lx+'px';tip.style.top=ly+'px'}
function hideTip(){tip.hidden=true}
function terrName(p){return pH[p]>.6?'Горы':pH[p]>.34&&pBiome[p]>3?'Холмы':BN[pBiome[p]]}
const STALE='<div class="stale"><i class="ti ti-history"></i> Сведения устарели</div>';
function tipFor(p){const f=fogOn?fog[p]:2,pk=scoutPick?`<div class="pk${pLand[p]?'':' bad'}"><i class="ti ti-${pLand[p]?'map-pin':'ban'}"></i>${pLand[p]?'Отправить разведчиков сюда':'Разведчики ходят только по суше'}</div>`:'';
  if(!f)return `<b>Неизведанные земли</b><div class="mu">${pk?'Что там — узнают разведчики':'Отправьте туда разведчиков из столицы'}</div>`+pk;
  const st=f===1?STALE:'';
  if(!pLand[p])return `<b>${pName[p]}</b><div class="mu">Морская зона</div>`+st+pk;
  const o=own[p];return `<b>${pName[p]}</b><div class="mu">${terrName(p)} · климат ${CLIM(pBiome[p])}</div><div class="tr"><span>${o>=0?`<i class="sw" style="background:rgb(${NAT[o][2]})"></i>${NAT[o][0]}`:'<i class="sw" style="background:#9aa1a9"></i>Ничья земля'}</span><span>${f===1?'~?':'~'+fmt(pPop[p])} <span class="mu" style="font-weight:400">чел.</span></span></div>`+st+pk}
const panel=$('panel');
let buildOpen=false,panP=-1,panF=-1;
function closePanel(){panel.hidden=true;sel=-1;panP=-1;dirtyMap=true}
function chip(n){return `<span class="chip" style="--c:rgb(${NAT[n][2]})">${NAT[n][0]}</span>`}
function stat(l,v){return `<div class="stat"><div class="l">${l}</div><div class="v">${v}</div></div>`}
function fertBar(f){const n=Math.round(f*5);return `<span class="pips5">${'<i class="on"></i>'.repeat(n)}${'<i></i>'.repeat(5-n)}</span><span class="mu">${n}/5</span>`}
function openPanel(p){buildOpen=false;renderPanel(p);panel.hidden=false}
function renderPanel(p){let h='';const L=pLand[p],o=own[p],old=fogOn&&fog[p]===1,st=old?STALE:'',pop=v=>old?'~?':'~'+fmt(v);panP=p;panF=fog[p];
  const head=(t,s,ic,c)=>`<div class="ph"${c?` style="--own:${c}"`:''}><div><div class="pt">${t}${ic?`<i class="ti ${ic}"></i>`:''}</div><div class="ps">${s}</div></div><button class="x" onclick="closePanel()" data-tip="Закрыть · Esc"><i class="ti ti-x"></i></button></div>`;
  if(fogOn&&fog[p]===0){panel.innerHTML=head('Неизведанные земли','Туман войны','ti-cloud-fog')+`<p class="mu">Здесь могут быть племена, ресурсы и чужие державы. Туман рассеивается там, где проходят ваши разведчики, границы и торговые пути.</p>`+
    (L?`<div class="acts"><button class="pri" ${scouts.length>=SC_MAX?'disabled':''} onclick="sendTo(${p})"><i class="ti ti-map-search"></i> Отправить разведчиков сюда</button></div><p class="mu fine">Разведчиков в пути: ${scouts.length} / ${SC_MAX}</p>`:`<p class="mu">Похоже на море. Разведчики ходят только по суше — корабли появятся позже.</p>`);return}
  if(!L){const r=routes.filter(rt=>rt.includes(p)).length;panel.innerHTML=head(pName[p],'Морская зона','ti-anchor')+st+`<div class="grid2">${stat('Рыбные угодья',pSize[p]<1400?'Богатые':'Обычные')}${stat('Торговые пути',r)}</div><p class="mu" style="margin-top:12px">Морские зоны дают рыбу прибрежным провинциям и связывают торговые пути. Флот появится во втором этапе.</p>`;return}
  const sub=`${terrName(p)} · климат ${CLIM(pBiome[p])}${pRiver[p]?' · река':''}${pCoast[p]?' · побережье':''}`;
  if(o<0){const near=adj[p].some(q=>own[q]===0);
    h=head(pName[p],sub,'','#9aa1a9')+st+`<div class="own"><span class="chip" style="--c:#9aa1a9">Ничья земля</span><span class="tag">Кочевники</span></div><div class="grid2">${stat('Кочевые племена',pop(pPop[p]))}${stat('Плодородие',fertBar(pFert[p]))}</div>`+
      `<h4>Присоединение</h4>`+(near?`<p class="mu" style="margin:0 0 8px">Граничит с Арданией. Племена можно убедить войти в державу.</p><button class="pri" onclick="claim(${p})"><i class="ti ti-flag"></i> Присоединить · 120 золота</button>`:`<p class="mu" style="margin:0">Слишком далеко от ваших границ.</p>`);
    panel.innerHTML=h;return}
  if(o!==0){h=head(pName[p],sub,pCap[p]>=0?'ti-crown':'',`rgb(${NAT[o][2]})`)+st+`<div class="own">${chip(o)}<span class="tag">${NAT[o][1]}</span>${pCap[p]>=0?'<span class="tag">Столица</span>':''}</div>`+
    `<div class="grid2">${stat('Население',pop(pPop[p]))}${stat('Отношения',o===1?'<span class="warn">Настороженные</span>':'<span class="mu" style="font-size:16px;font-weight:600">Нейтральные</span>')}${stat('Вера',`<i class="sw" style="background:rgb(${REL[pRel[p]][1]})"></i>${REL[pRel[p]][0]}`)}${stat('Культура',NAT[o][3])}</div>`+
    `<div class="acts"><button onclick="toast('Окно сделки — отдельный макет')"><i class="ti ti-scale"></i> Предложить сделку</button><button onclick="toast('Торговый путь будет проложен по суше и морю')"><i class="ti ti-route"></i> Торговый путь</button></div>`;
    panel.innerHTML=h;return}
  const tot=pPop[p],cl=[62,18,12,8].map((v,k)=>v+((h2(p,k,S)*6)|0)-3);const cs=cl.reduce((a,b)=>a+b,0);
  h=head(pName[p],sub,pCap[p]>=0?'ti-crown':'',`rgb(${NAT[0][2]})`)+`<div class="own">${chip(0)}<span class="tag">${pCap[p]>=0?'Столица':'Провинция'}</span></div>`+
    `<div class="grid2">${stat('Население',fmt(tot))}${stat('Довольство',`<span class="${pMood[p]>60?'ok':''}">${pMood[p]}%</span>`)}${stat('Плодородие',fertBar(pFert[p]))}${stat('Налоги','+'+(tot/1600).toFixed(1)+' <span class="mu" style="font-size:11px">в год</span>')}</div>`+
    (pCap[p]===0?`<div id="scbox">${scoutBox()}</div>`:'')+
    `<h4>Население <span>${CLS.length} класса</span></h4><div class="bar">${cl.map((v,k)=>`<div style="width:${v/cs*100}%;background:${CLS[k][1]}"></div>`).join('')}</div>`+
    `<div class="leg">${cl.map((v,k)=>`<span><i style="background:${CLS[k][1]}"></i>${CLS[k][0]} <span class="mu">${Math.round(v/cs*100)}%</span></span>`).join('')}</div>`+
    `<div class="kv"><span>Культура <b>${NAT[0][3]}</b></span><span>Вера <b><i class="sw" style="background:rgb(${REL[pRel[p]][1]})"></i>${REL[pRel[p]][0]}</b></span></div>`+
    `<h4>Постройки <span>${pBld[p].length} / ${pSlots[p]}</span></h4>`+pBld[p].map(b=>`<div class="bl"><i class="ti ${BLD[b][1]}"></i>${BLD[b][0]}<span class="lv">ур. 1 · работников ${80+((h2(p,b.length,S)*140)|0)}</span></div>`).join('')+
    (pBld[p].length<pSlots[p]?`<div class="bl empty" onclick="buildOpen=!buildOpen;renderPanel(${p})"><i class="ti ti-hammer"></i>Свободный участок — построить</div>`+(buildOpen?`<div class="menu">${bOpts(p).filter(b=>!pBld[p].includes(b)).map(b=>`<button onclick="build(${p},'${b}')"><i class="ti ${BLD[b][1]}"></i> ${BLD[b][0]}</button>`).join('')}</div>`:''):'')+
    `<h4>Недра</h4>`+(pRes[p]?(pRes[p].found?`<div class="bl"><i class="ti ti-diamond"></i>${pRes[p].k}<span class="lv">разведано</span></div>`:`<div class="bl"><i class="ti ti-help"></i><span class="mu">Не разведаны</span><button style="height:26px;font-size:12px" onclick="survey(${p})"><i class="ti ti-shovel"></i>Отправить геологов</button></div>`):`<p class="mu">Холмов и гор нет — залежей не ожидается</p>`)+
    (pCap[p]===0?`<h4>Строится <span id="qpct">${queue.pct}%</span></h4><div class="bl"><i class="ti ti-hammer"></i>${queue.name}</div><div class="prog"><div id="qbar" style="width:${queue.pct}%"></div></div>`:'');
  panel.innerHTML=h}
function claim(p){gold-=120;own[p]=0;pRel[p]=0;pPop[p]*=2.2;pBld[p]=[];updateFog();computeMode();renderPanel(p);updTop();note('ti-flag',`${pName[p]} вошла в состав Ардании`);renderLead()}

/* ---------- scouts ---------- */
const plural=(n,a,b,c)=>{const m=n%100,k=n%10;return m>10&&m<20?c:k===1?a:k>1&&k<5?b:c};
function scoutBox(){const n=scouts.length,full=n>=SC_MAX;
  return `<h4>Разведчики <span>в пути ${n} / ${SC_MAX}</span></h4>`+scouts.map(s=>{const tg=s.path[s.path.length-1],left=s.path.length-1-s.k;
    return `<div class="bl"><i class="ti ti-walk"></i>${s.auto?'Свободный поиск':'→ '+(fog[tg]||!fogOn?pName[tg]:'неизведанные земли')}<span class="lv">${s.auto?'разведано '+s.found:'ещё '+left+' '+plural(left,'провинция','провинции','провинций')}</span></div>`}).join('')+
    `<div class="acts"><button class="${scoutPick?'on':''}" ${full?'disabled':''} onclick="startPick()"${scoutPick?' data-tip="Отменить выбор цели · Esc"':''}><i class="ti ti-${scoutPick?'crosshair':'map-search'}"></i> ${scoutPick?'Выберите цель на карте':'Отправить разведчиков'}</button><button ${full?'disabled':''} onclick="sendAuto()" data-tip="Разведчики сами пойдут к ближайшим неизведанным землям"><i class="ti ti-compass"></i> Авто</button></div>`}
function refreshScouts(force){const b=$('scbox');if(b)b.innerHTML=scoutBox();else if(!panel.hidden&&panP>=0&&(force||fog[panP]!==panF))renderPanel(panP)}
function startPick(){if(scoutPick){cancelPick(true);return}if(scouts.length>=SC_MAX){toast(`Все разведчики уже в пути (${SC_MAX} из ${SC_MAX})`,0,'err');return}
  scoutPick=true;cv.classList.add('pick');toast('Выберите цель для разведчиков · Esc — отмена',864e5,'pick');refreshScouts()}
function cancelPick(msg){scoutPick=false;cv.classList.remove('pick');if(msg)toast('Отправка разведчиков отменена');else $('toast').hidden=true;refreshScouts()}
const SCERR={sea:'Разведчики ходят только по суше — выберите сухопутную провинцию',far:'Туда не добраться по суше',here:'Разведчики уже в столице — выберите цель подальше',max:`Все разведчики уже в пути (${SC_MAX} из ${SC_MAX})`,none:'Поблизости не осталось неизведанных земель'};
function pickScout(p){const r=sendScout(p);if(r&&r!=='max'){toast(SCERR[r],864e5,'err');return}cancelPick();
  if(r)toast(SCERR[r],0,'err');else{toast(fog[p]?`Разведчики выступили к провинции ${pName[p]}`:'Разведчики выступили в неизведанные земли');refreshScouts(true)}}
function sendTo(p){const r=sendScout(p);if(r){toast(SCERR[r],0,'err');return}toast('Разведчики выступили в неизведанные земли');renderPanel(p)}
function sendAuto(){if(scoutPick)cancelPick();const r=sendScout(-1);if(r){toast(SCERR[r],0,'err');return}toast('Разведчики отправились к ближайшим неизведанным землям');refreshScouts(true)}
/* called from fog.js */
function fogEvent(k,x){
  if(k==='done'){const tg=x.path[x.path.length-1],f=x.found;
    note('ti-map-2',x.auto?(f?`Разведчики исследовали ${f} ${plural(f,'провинцию','провинции','провинций')} и вернулись с картами`:'Разведчики вернулись: поблизости нет неизведанных земель')
      :`Разведчики достигли провинции ${pName[tg]} и вернулись с картами`+(f?` (+${f} ${plural(f,'провинция','провинции','провинций')} на карте)`:''))}
  else if(k==='met')for(const n of x)if(n!==0)note('ti-affiliate',`Встречена новая держава: ${NAT[n][0]} (${NAT[n][1].toLowerCase()})`);
  refreshScouts(k==='done');if(k!=='step'||!$('lead').hidden)renderLead()}
function build(p,b){pBld[p].push(b);buildOpen=false;renderPanel(p);dirtyOv=true;note('ti-hammer',`${pName[p]}: заложена постройка «${BLD[b][0]}»`)}
function survey(p){pRes[p].found=true;renderPanel(p);note('ti-shovel',`Геологи нашли ${pRes[p].k.toLowerCase()} в провинции ${pName[p]}`)}

/* ---------- time, notes, toasts ---------- */
let speed=2,paused=false,year=-1250,gold=1240,acc=0,mins=134,queue={name:'Амбар',pct:64},evk=0;
function renderSpeed(){$('pips').innerHTML=[1,2,3,4,5].map(s=>`<span class="${s<=speed?'on':''}" data-s="${s}"></span>`).join('');$('pause').innerHTML=`<i class="ti ti-player-${paused?'play':'pause'}"></i>`;$('date').classList.toggle('paused',paused)}
$('pips').onclick=e=>{const s=e.target.dataset.s;if(s){speed=+s;renderSpeed()}};
function togglePause(){paused=!paused;renderSpeed();if(paused)toast('Пауза. В сетевой игре все видят, кто её поставил.')}
$('pause').onclick=togglePause;
const JOKES=[m=>`Правитель, ты на троне уже ${m}. Народ просит тебя попить чаю.`,m=>`${m} без перерыва. Даже жрецы Солнца спят по ночам.`,m=>`${m}. «Ещё один год» — так говорил каждый павший император.`];
$('session').onclick=()=>{const t=`${Math.floor(mins/60)} ч ${mins%60} мин`;toast(JOKES[(mins/7|0)%JOKES.length](t))};
function updTop(){$('r-gold').textContent=fmt(gold);let s=0;for(let p=0;p<P;p++)if(own[p]===0)s+=pPop[p];$('r-pop').textContent=s>=1e3?(s/1e3).toFixed(1).replace('.',',')+' тыс':fmt(s)}
const EVENTS=[
  ()=>['ti-bulb','Эврика! Три фермы ускорили исследование «Ирригация» на 20%'],
  ()=>['ti-scale',`${NAT[1][0]} предлагает обмен: камень на вино`],
  ()=>{const p=rndOwn();return['ti-feather',`В провинции ${pName[p]} эму объявили войну урожаю. Армия в замешательстве`]},
  ()=>{const p=rndOwn();return['ti-music',`В ${pName[p]} началась танцевальная чума: производство −10% на год`]},
  ()=>['ti-sun','Жрецы Культа Солнца верно предсказали затмение. Стабильность +5%'],
  ()=>{const p=rndOwn();return['ti-users',`Общинники провинции ${pName[p]} требуют новый колодец`]},
  ()=>['ti-crown','Правитель назначил своего коня советником. Знать возмущена'],
];
function rndOwn(){const ps=[];for(let p=0;p<P;p++)if(own[p]===0)ps.push(p);return ps[(evk*7)%ps.length]}
function tick(){year++;if(year===0)year=1;gold+=12;$('date').textContent=year<0?`${-year} до н. э.`:`${year} н. э.`;
  queue.pct+=3;if(queue.pct>=100){note('ti-hammer',`В столице построен ${queue.name.toLowerCase()}`);queue={name:queue.name==='Амбар'?'Каменные стены':'Амбар',pct:0}}
  const qp=$('qpct'),qb=$('qbar');if(qp){qp.textContent=queue.pct+'%';qb.style.width=queue.pct+'%'}
  if(year%11===0){const e=EVENTS[evk++%EVENTS.length]();note(e[0],e[1])}updTop()}
setInterval(()=>{if(paused||!P)return;acc+=100;const need=[0,2000,1000,500,250,100][speed];while(acc>=need){acc-=need;tick()}},100);
setInterval(()=>{mins++;$('sess').textContent=Math.floor(mins/60)+':'+String(mins%60).padStart(2,'0')},60000);
const notes=$('notes');
function note(ic,t){const d=document.createElement('div');d.className='note pn';d.innerHTML=`<i class="ti ${ic}"></i><div><div>${t}</div><div class="t">${year<0?-year+' до н. э.':year+' н. э.'}</div></div>`;d.onclick=()=>{d.remove();dirtyOv=true};notes.prepend(d);while(notes.children.length>3)notes.lastChild.remove();dirtyOv=true}
/* k: '' info · 'pick' scout targeting · 'err' refused action (the icon block is drawn by CSS) */
let tt;function toast(t,ms,k){const e=$('toast');e.textContent=t;e.dataset.k=k||'';e.hidden=false;clearTimeout(tt);tt=setTimeout(()=>e.hidden=true,ms||3800)}
function renderLead(){const rows=[];let unk=0;for(let n=0;n<natCap.length;n++){let pop=0,pr=0;for(let p=0;p<P;p++)if(own[p]===n){pop+=pPop[p];pr++}if(!met(n)){unk++;continue}rows.push([n,Math.round(pop/800+pr*9)])}
  rows.sort((a,b)=>b[1]-a[1]);const top=rows.length?rows[0][1]||1:1;$('lead').innerHTML=`<div class="lh">Таблица лидеров<small>встречено ${rows.length}</small></div><div class="lb">`+rows.map(([n,s],k)=>`<div class="lr${n===0?' me':''}"><span class="rk">${k+1}</span><span class="c" style="background:rgb(${NAT[n][2]})"></span><span>${NAT[n][0]}${n===0?' <span class="mu" style="font-weight:400">· вы</span>':''}</span><span class="s">${s}</span><span class="lbar"><i style="width:${Math.round(s/top*100)}%"></i></span></div>`).join('')+(unk?`<div class="lr unk"><i class="ti ti-help-hexagon"></i> Ещё ${unk} ${plural(unk,'держава не встречена','державы не встречены','держав не встречены')}</div>`:'')+`</div>`}
function drawFlag(){const f=$('flag').getContext('2d'),c=NAT[0][2];document.querySelector('.nation').style.setProperty('--own',`rgb(${c})`);f.fillStyle=`rgb(${c})`;f.fillRect(0,0,18,12);f.fillStyle='#f0c850';f.fillRect(8,3,2,6);f.fillRect(6,5,6,2);f.fillRect(7,4,4,4);f.fillStyle=`rgb(${c.map(v=>v*.6|0)})`;f.fillRect(0,10,18,2)}


/* ---------- boot ---------- */
async function boot(){$('loading').hidden=false;$('loadtxt').textContent=`Генерация мира · зерно ${S}`;panel.hidden=true;sel=-1;hov=-1;panP=-1;scouts=[];if(scoutPick)cancelPick();
  await new Promise(r=>setTimeout(r,40));
  const info=await generateWorld(t=>{$('loadtxt').textContent=`${t} · зерно ${S}`});
  initFog();fdCache=FD();fuCache=FU();computeMode();resize();
  const c=natCap[0];cam.z=3;$('zl').textContent='×3';cam.px=Math.round(pCX[c]*cam.z-cv.width/2+170);cam.py=Math.round(pCY[c]*cam.z-cv.height/2);clampCam();
  updTop();renderLead();drawFlag();drawMiniView();$('loading').hidden=true;
  notes.innerHTML='';note('ti-scale',`${NAT[1][0]} предлагает обмен: камень на вино`);note('ti-shovel',`Геологи нашли медь в холмах у ${pName[natCap[0]]}`);note('ti-bulb','Эврика! Три фермы ускорили «Ирригацию» на 20%');
  sel=c;openPanel(c);dirtyMap=true;console.log('world',S,'provinces',info.provinces,'ms',info.ms,'fog',JSON.stringify(fogMs))}
addEventListener('resize',()=>{resize();drawMiniView();placeLead()});
document.fonts&&document.fonts.ready.then(()=>{fdCache=FD();fuCache=FU();dirtyOv=true});
renderSpeed();syncFogBtn();boot();requestAnimationFrame(frame);
