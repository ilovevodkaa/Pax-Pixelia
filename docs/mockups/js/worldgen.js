'use strict';
/* World generation: terrain, rivers, provinces, nations. Must not touch the DOM (testable in node).
   Per-pixel passes are row-band kernels (wgA/wgH/wgBio/wgCol/wgP, dispatched by wgJob) that only read their arguments,
   so the same code runs inline (one band = whole world) or in Web Workers (many bands) with identical results. */
function labelBy(key){const id=new Int32Array(N).fill(-1),size=[],first=[],st=new Int32Array(N);let c=0;
  for(let s=0;s<N;s++){if(id[s]>=0)continue;const kv=key[s];let sp=0,n=0;st[sp++]=s;id[s]=c;
    while(sp){const j=st[--sp];n++;const x=j%WW,row=j-x;let k=row+(x+1===WW?0:x+1);if(id[k]<0&&key[k]===kv){id[k]=c;st[sp++]=k}
      k=row+(x===0?WW-1:x-1);if(id[k]<0&&key[k]===kv){id[k]=c;st[sp++]=k}
      if(row>0){k=j-WW;if(id[k]<0&&key[k]===kv){id[k]=c;st[sp++]=k}}
      if(row<N-WW){k=j+WW;if(id[k]<0&&key[k]===kv){id[k]=c;st[sp++]=k}}}
    size.push(n);first.push(s);c++}
  return{id,size,first,count:c}}

/* ---------- band kernels: rows [y0,y1); inputs indexed g-o, outputs g-q (g = y*WW+x) ---------- */
function wgA(y0,y1,q,B,R){let mn=1e9,mx=-1e9;const wk=NK.warp,wa=NK.warpA,eo=NK.eOct,rk=NK.ridge;
  for(let y=y0;y<y1;y++){const pl=Math.abs(y/WH-.5)*2,pp=.2*pl*pl*pl;for(let x=0;x<WW;x++){const i=y*WW+x-q;
    const qx=x+wa*(fbm(x,y,wk,3,S+7)*2-1),qy=y+wa*(fbm(x,y,wk,3,S+13)*2-1),e=fbm(qx,qy,4,eo,S)-pp;B[i]=e;if(e<mn)mn=e;if(e>mx)mx=e;
    const mk=fbm(x,y,3,3,S+21);if(mk<=.47){R[i]=0;continue}
    let r=1-Math.abs(fbm(qx,qy,rk,4,S+3)-.5)*7;r=r<0?0:r;R[i]=r*r*sm(.47,.6,mk)}}
  return[mn,mx]}
function wgH(y0,y1,q,B,R,L,H,sea,mn){const hk=NK.hill,mk=NK.micro;
  for(let y=y0;y<y1;y++)for(let x=0;x<WW;x++){const i=y*WW+x-q,d=B[i]-sea;
    if(L[i]){const dd=Math.max(d,.001);H[i]=Math.min(1,dd/.2)*.28+R[i]*.82*sm(0,.05,dd)+.1*Math.max(0,fbm(x,y,hk,3,S+33)-.5)*2+.05*fbm(x,y,mk,2,S+34)}
    else H[i]=Math.min(-.001,d)/(sea-mn)}}
function wgBio(y0,y1,q,L,H,BI){const mk=NK.moist,tk=NK.temp;
  for(let y=y0;y<y1;y++){const lat=Math.abs(y/WH-.5)*2;for(let x=0;x<WW;x++){const i=y*WW+x-q;if(!L[i])continue;const h=H[i];
    const m=(fbm(x,y,5,4,S+50)-.5)*1.9+.5+(fbm(x,y,mk,3,S+51)-.5)*.4+(h2(x,y,S+52)-.5)*.07,t=1-lat*1.1+(fbm(x,y,4,3,S+70)-.5)*.3+(fbm(x,y,tk,2,S+71)-.5)*.12+(h2(x,y,S+53)-.5)*.04-h*.5;let b;
    if(t<.08)b=1;else if(h>.82)b=2;else if(h>.6)b=3;else if(t<.2)b=4;else if(t<.35)b=m>.5?5:6;
    else if(t<.7)b=(m>.7&&h<.12)?7:m>.55?8:m>.44?9:m>.32?10:11;else b=m>.6?12:m>.42?13:14;BI[i]=b}}}
function wgCol(y0,y1,o,q,L,H,BI,C){const tk=NK.tone,dk=NK.dune;
  for(let y=y0;y<y1;y++)for(let x=0;x<WW;x++){const g=y*WW+x,i=g-o,xl=i-x+(x+WW-1)%WW,xr=i-x+(x+1)%WW;
    if(L[i]){const b=BI[g-q],h=H[i];let[r,gr,bl]=BC[b];
      if(h>.34&&h<=.6&&b>3){r=r*.72+114*.28;gr=gr*.72+104*.28;bl=bl*.72+82*.28}
      const hn=h2(x,y,S+999),tone=fbm(x,y,tk,2,S+60);let f=(b===5||b===8||b===12)?(hn<.3?.78:hn>.84?1.13:1):(.965+hn*.07);
      f*=.92+tone*.16;
      if(b===14||b===11)f*=1+.04*Math.sin(x*.5+y*.2+fbm(x,y,dk,1,S+5)*6);
      const gx=H[xr]-H[xl],gy=(y<WH-1?H[i+WW]:h)-(y>0?H[i-WW]:h);
      let s=1+(gx+gy)*10;s=Math.round(s/.06)*.06;s=s<.55?.55:s>1.4?1.4:s;f*=s;
      C[g-q]=pack(r*f,gr*f,bl*f)}
    else{const d=-H[i];let c=d<.035?[64,99,120]:d<.1?[47,77,100]:d<.25?[37,59,82]:[28,44,64];
      if(L[xl]||L[xr]||(y>0&&L[i-WW])||(y<WH-1&&L[i+WW]))c=[80,116,134];
      const f=h2(x,y,S+77)<.012?1.16:1;C[g-q]=pack(c[0]*f,c[1]*f,c[2]*f)}}}
/* province seeds: jittered grid 16px on land, 64px on sea; pixel -> nearest seed of the same component, distance
   penalised by height difference so borders follow relief */
const PCS=16,PCS2=64;
function wgP(y0,y1,q,L,H,CI,PR,sd){const{SX,SY,SC,SH,g1,g2,cO,cL}=sd,GX=WW/PCS,GY=Math.ceil(WH/PCS),GX2=WW/PCS2,GY2=Math.ceil(WH/PCS2),jk=NK.jit,ja=NK.jitA,hw=WW/2;
  for(let y=y0;y<y1;y++)for(let x=0;x<WW;x++){const i=y*WW+x-q,c=CI[i],Ld=L[i];
    const jx=x+ja*(fbm(x,y,jk,2,S+90)*2-1),jy=y+ja*(fbm(x,y,jk,2,S+91)*2-1);
    const cs=Ld?PCS:PCS2,gw=Ld?GX:GX2,gh=Ld?GY:GY2,gr=Ld?g1:g2,gx0=Math.floor(jx/cs),gy0=Math.floor(jy/cs),hi=H[i],pw=Ld?48:0;
    let best=-1,bd=1e18;
    for(let gy=gy0-2;gy<=gy0+2;gy++){if(gy<0||gy>=gh)continue;const rb=gy*gw;for(let gx=gx0-2;gx<=gx0+2;gx++){const s=gr[rb+(gx<0?gx+gw:gx>=gw?gx-gw:gx)];if(s<0||SC[s]!==c)continue;
      let ddx=SX[s]-jx;if(ddx>hw)ddx-=WW;else if(ddx<-hw)ddx+=WW;const ddy=SY[s]-jy,pe=(hi-SH[s])*pw,d=ddx*ddx+ddy*ddy+pe*pe;if(d<bd){bd=d;best=s}}}
    if(best<0)for(let k=cO[c],e=cO[c+1];k<e;k++){const s=cL[k];let ddx=SX[s]-jx;if(ddx>hw)ddx-=WW;else if(ddx<-hw)ddx+=WW;const ddy=SY[s]-jy,d=ddx*ddx+ddy*ddy;if(d<bd){bd=d;best=s}}
    PR[i]=best}}
/* job dispatcher, shared by the inline path and the workers. m.ya = first row of the input slices (halo) */
function wgJob(m){S=m.S;const{y0,y1}=m,q=y0*WW,o=m.ya*WW,n=(y1-y0)*WW;
  if(m.c==='A'){const B=new Float32Array(n),R=new Float32Array(n),r=wgA(y0,y1,q,B,R);return[{B,R,r},[B.buffer,R.buffer]]}
  if(m.c==='H'){const H=new Float32Array(n),BI=new Uint8Array(n);wgH(y0,y1,q,m.B,m.R,m.L,H,m.sea,m.mn);wgBio(y0,y1,q,m.L,H,BI);return[{H,BI},[H.buffer,BI.buffer]]}
  if(m.c==='C'){const C=new Uint32Array(n);wgCol(y0,y1,o,q,m.L,m.H,m.BI,C);return[{C},[C.buffer]]}
  const PR=new Int32Array(n);wgP(y0,y1,q,m.L,m.H,m.CI,PR,m.sd);return[{PR},[PR.buffer]]}

/* ---------- worker pool (Blob URL, built from the kernels' own source) ---------- */
let wgPool=null,wgWarm=null,wgStat=null;
function wkPool(){if(wgPool!==null)return wgPool;wgPool=false;
  try{if(typeof Worker==='undefined'||typeof Blob==='undefined'||typeof URL==='undefined')return wgPool;
    const nw=Math.min(8,(typeof navigator!=='undefined'&&navigator.hardwareConcurrency)||4);if(nw<2)return wgPool;
    const src=`'use strict';const WW=${WW},WH=${WH},N=${N},KS=${KS},NK=${JSON.stringify(NK)},BC=${JSON.stringify(BC)},PCS=${PCS},PCS2=${PCS2};let S=0;
const sm=${sm},cl=${cl},pack=${pack};
${[h2,vn,fbm,wgA,wgH,wgBio,wgCol,wgP,wgJob].join('\n')}
onmessage=e=>{const[r,t]=wgJob(e.data);postMessage(r,t)};`;
    const url=URL.createObjectURL(new Blob([src],{type:'text/javascript'}));wgPool=[];for(let k=0;k<nw;k++)wgPool.push(new Worker(url));
    // JIT warm-up: a few rows of the relief pass per worker while the page is still loading
    wgWarm=Promise.all(wgPool.map((w,k)=>new Promise(r=>{const t=setTimeout(()=>r(false),10000);w.onmessage=()=>{clearTimeout(t);r(true)};
      w.onerror=e=>{e.preventDefault&&e.preventDefault();clearTimeout(t);r(false)};w.postMessage({c:'A',S:k,y0:k*4,y1:k*4+4,ya:k*4})}))).then(a=>a.every(Boolean))}
  catch(e){wkKill()}return wgPool}
function wkKill(){if(wgPool)for(const w of wgPool)w.terminate();wgPool=false}
/* run jobs on the pool (work queue, a free worker takes the next band) */
function wkQueue(ws,jobs){return new Promise((res,rej)=>{let nx=0,left=jobs.length;const tm=setTimeout(()=>rej(new Error('worker timeout')),30000);
  const go=w=>{if(nx>=jobs.length)return;const j=jobs[nx++];w.onmessage=e=>{j.done(e.data);if(--left===0){clearTimeout(tm);res()}else go(w)};
    w.postMessage(j.m,Object.values(j.m).filter(v=>ArrayBuffer.isView(v)).map(v=>v.buffer))};
  for(const w of ws){w.onerror=e=>{e.preventDefault&&e.preventDefault();clearTimeout(tm);rej(new Error('worker error: '+(e.message||'')))};go(w)}})}
/* one pass over all rows. mk(y0,y1,ya,yb,sl) builds the message (sl(a) = input rows ya..yb, a copy that gets transferred);
   done(r,y0) consumes the result. Without workers: runs now as a single whole-world band, no copies. Returns the jobs. */
function wgPass(ws,c,halo,mk,done){
  if(!ws){const m=mk(0,WH,0,WH,a=>a);m.c=c;m.S=S;m.y0=0;m.y1=WH;m.ya=0;done(wgJob(m)[0],0);return[]}
  const nb=ws.length*3,jobs=[];
  for(let k=0;k<nb;k++){const y0=Math.round(WH*k/nb),y1=Math.round(WH*(k+1)/nb),ya=Math.max(0,y0-halo),yb=Math.min(WH,y1+halo);
    const m=mk(y0,y1,ya,yb,a=>a.slice(ya*WW,yb*WW));m.c=c;m.S=S;m.y0=y0;m.y1=y1;m.ya=ya;jobs.push({m,done:r=>done(r,y0)})}
  return jobs}
const wgQ=(ws,jobs)=>ws?wkQueue(ws,jobs):Promise.resolve();

/* ---------- terrain ---------- */
function seaLevel(base,mn,mx){const bins=new Uint32Array(4096),rng=mx-mn;for(let i=0;i<N;i++)bins[Math.min(4095,((base[i]-mn)/rng*4096)|0)]++;
  let acc=0,sea=mn;const tgt=N*(1-.37);for(let b=0;b<4096;b++){acc+=bins[b];if(acc>=tgt){sea=mn+(b+1)/4096*rng;break}}
  land=new Uint8Array(N);for(let i=0;i<N;i++)land[i]=base[i]>sea?1:0;
  const lab=labelBy(land);for(let i=0;i<N;i++){const s=lab.size[lab.id[i]];if(land[i]?s<14:s<70)land[i]^=1}
  return sea}
/* rivers: greedy descent from random springs in the hills (attempts and cap scale with the map area); the key adds a little raw elevation so that the flat
   interiors of big continents still drain towards the coast instead of trapping rivers */
function genRivers(base,sea,DW=.5){river=new Uint8Array(N);riverPaths=[];const stamp=new Uint32Array(N),A=Math.round(N/50),MX=Math.round(N/9200);let made=0;
  const key=j=>land[j]?hgt[j]+DW*(base[j]-sea)+h2(j,3,S)*.003:-1;
  // a tracer that circles a flat pit leaves a spiral: drop paths that come back within 10px of where they were 40 steps earlier
  const curl=pa=>{for(let k=0;k+40<pa.length;k++){const a=pa[k],b=pa[k+40];let dx=Math.abs(a%WW-b%WW);if(dx>WW/2)dx=WW-dx;const dy=((a/WW)|0)-((b/WW)|0);if(dx*dx+dy*dy<100)return true}return false};
  for(let k=0;k<A&&made<MX;k++){let i=((h2(k,9,S)*WH)|0)*WW+((h2(k,7,S)*WW)|0);if(!land[i]||hgt[i]<.3||hgt[i]>.78||biome[i]<=2)continue;
    const path=[];let ok=false;
    for(let st=0;st<1200;st++){path.push(i);stamp[i]=k+1;const x=i%WW,y=(i-x)/WW;let best=-1,bh=1e9;const cur=key(i);
      const nb=[y*WW+(x+1)%WW,y*WW+(x+WW-1)%WW,y>0?i-WW:-1,y<WH-1?i+WW:-1];
      for(const j of nb){if(j<0||stamp[j]===k+1)continue;const hj=key(j);if(hj<bh){bh=hj;best=j}}
      if(best<0||bh>cur+.03)break;
      if(!land[best]||river[best]){ok=true;break}
      i=best}
    if(ok&&path.length>28&&!curl(path)){for(const j of path)river[j]=1;made++;
      const pts=[];let px0=path[0]%WW;for(const j of path){let x=j%WW+.5;while(x-px0>WW/2)x-=WW;while(px0-x>WW/2)x+=WW;px0=x;pts.push([x,Math.floor(j/WW)+.5])}
      const smp=[];for(let k=0;k<pts.length;k+=2){let ax=0,ay=0,n=0;for(let q=Math.max(0,k-4);q<=Math.min(pts.length-1,k+4);q++){ax+=pts[q][0];ay+=pts[q][1];n++}smp.push(k===0?pts[0]:[ax/n,ay/n])}
      smp.push(pts[pts.length-1]);let y0=1e9,y1=-1e9;for(const q of smp){y0=Math.min(y0,q[1]);y1=Math.max(y1,q[1])}riverPaths.push({pts:smp,y0,y1})}}}

/* ---------- provinces ---------- */
function provSeeds(comp){const GX=WW/PCS,GY=Math.ceil(WH/PCS),GX2=WW/PCS2,GY2=Math.ceil(WH/PCS2);
  const sx=[],sy=[],sc=[],sh=[];const g1=new Int32Array(GX*GY).fill(-1),g2=new Int32Array(GX2*GY2).fill(-1);
  const add=(x,y)=>{const i=(y|0)*WW+(x|0);sx.push(x);sy.push(y);sc.push(comp.id[i]);sh.push(hgt[i]);return sx.length-1};
  for(let gy=0;gy<GY;gy++)for(let gx=0;gx<GX;gx++){const x=gx*PCS+(.15+.7*h2(gx,gy,S+501))*PCS,y=Math.min(WH-1,gy*PCS+(.15+.7*h2(gx,gy,S+502))*PCS);if(land[(y|0)*WW+(x|0)])g1[gy*GX+gx]=add(x,y)}
  for(let gy=0;gy<GY2;gy++)for(let gx=0;gx<GX2;gx++){const x=gx*PCS2+(.2+.6*h2(gx,gy,S+503))*PCS2,y=Math.min(WH-1,gy*PCS2+(.2+.6*h2(gx,gy,S+504))*PCS2);if(!land[(y|0)*WW+(x|0)])g2[gy*GX2+gx]=add(x,y)}
  const cnt=new Int32Array(comp.count);for(let s=0;s<sc.length;s++)cnt[sc[s]]++;
  for(let c=0;c<comp.count;c++)if(!cnt[c]){const f=comp.first[c];add(f%WW+.5,Math.floor(f/WW)+.5);cnt[c]++}
  const nS=sx.length,cO=new Int32Array(comp.count+1);for(let c=0;c<comp.count;c++)cO[c+1]=cO[c]+cnt[c];
  const cL=new Int32Array(nS),w=cO.slice(0,comp.count);for(let s=0;s<nS;s++)cL[w[sc[s]]++]=s;
  return{SX:Float32Array.from(sx),SY:Float32Array.from(sy),SC:Int32Array.from(sc),SH:Float32Array.from(sh),g1,g2,cO,cL,nS}}
function genProvinces(comp,nS){
  // fragments (non-largest pieces of a seed's area) join the neighbouring piece they touch most, preferring main pieces
  const pc=labelBy(prov),nc=pc.count,mainC=new Int32Array(nS).fill(-1),mainS=new Int32Array(nS),isM=new Uint8Array(nc);
  for(let k=0;k<nc;k++){const p=prov[pc.first[k]];if(pc.size[k]>mainS[p]){mainS[p]=pc.size[k];mainC[p]=k}}
  let nm=0;for(let p=0;p<nS;p++)if(mainC[p]>=0){isM[mainC[p]]=1;nm++}
  const uf=new Int32Array(nc),fnd=k=>{while(uf[k]!==k){uf[k]=uf[uf[k]];k=uf[k]}return k};
  if(nc>nm){const fragN=new Map();
    for(let i=0;i<N;i++){const k=pc.id[i];if(isM[k])continue;const x=i%WW,row=i-x;
      for(const j of [row+(x+1)%WW,row+(x+WW-1)%WW,row>0?i-WW:-1,row<N-WW?i+WW:-1]){if(j<0)continue;const q=pc.id[j];if(q===k||land[j]!==land[i])continue;let m=fragN.get(k);if(!m){m=new Map();fragN.set(k,m)}m.set(q,(m.get(q)||0)+(isM[q]?1e6:1))}}
    for(let k=0;k<nc;k++)uf[k]=k;
    for(const[k,m]of fragN){let bq=-1,bc=0;for(const[q,c]of m)if(c>bc){bc=c;bq=q}if(bq>=0)uf[fnd(k)]=fnd(bq)}
    const rp=new Int32Array(nc).fill(-1);for(let k=0;k<nc;k++)if(isM[k])rp[fnd(k)]=prov[pc.first[k]];
    let extra=nS;const fp=new Int32Array(nc);for(let k=0;k<nc;k++)if(!isM[k]){const r=fnd(k);if(rp[r]<0)rp[r]=extra++;fp[k]=rp[r]}
    for(let i=0;i<N;i++){const k=pc.id[i];if(!isM[k])prov[i]=fp[k]}nS=extra}
  // merge tiny land provinces into the larger neighbour they touch most
  const cnt=new Int32Array(nS);for(let i=0;i<N;i++)cnt[prov[i]]++;
  const smallN=new Map();
  for(let i=0;i<N;i++){const p=prov[i];if(cnt[p]>=22||!land[i])continue;const x=i%WW,row=i-x;
    for(const j of [row+(x+1)%WW,row+(x+WW-1)%WW,row>0?i-WW:-1,row<N-WW?i+WW:-1]){if(j<0)continue;const q=prov[j];if(q===p||land[j]!==land[i])continue;let m=smallN.get(p);if(!m){m=new Map();smallN.set(p,m)}m.set(q,(m.get(q)||0)+1)}}
  const u2=new Int32Array(nS);for(let p=0;p<nS;p++)u2[p]=p;const f2=k=>{while(u2[k]!==k){u2[k]=u2[u2[k]];k=u2[k]}return k};
  for(const[p,m]of smallN){let bq=-1,bc=0;for(const[q,c]of m)if(c>bc&&cnt[q]>=cnt[p]){bc=c;bq=q}if(bq>=0){const a=f2(p),b=f2(bq);if(a!==b)u2[a]=b}}
  const rt=new Int32Array(nS),nid=new Int32Array(nS).fill(-1);for(let p=0;p<nS;p++)rt[p]=f2(p);P=0;
  for(let i=0;i<N;i++){const p=rt[prov[i]];let v=nid[p];if(v<0)v=nid[p]=P++;prov[i]=v}
  // stats
  pSize=new Int32Array(P);pLand=new Uint8Array(P);pRiver=new Uint8Array(P);pCoast=new Uint8Array(P);
  const sC=new Float64Array(P),sS=new Float64Array(P),sY=new Float64Array(P),hs=new Float64Array(P),bh=new Uint32Array(P*15),cX=new Float64Array(WW),sX=new Float64Array(WW);
  for(let x=0;x<WW;x++){const a=x/WW*6.283185307;cX[x]=Math.cos(a);sX[x]=Math.sin(a)}
  for(let y=0;y<WH;y++)for(let x=0;x<WW;x++){const i=y*WW+x,p=prov[i];pSize[p]++;pLand[p]=land[i];sC[p]+=cX[x];sS[p]+=sX[x];sY[p]+=y;hs[p]+=hgt[i];bh[p*15+biome[i]]++;if(river[i])pRiver[p]=1}
  const cx=new Float32Array(P),cy=new Float32Array(P);
  for(let p=0;p<P;p++){cx[p]=((Math.atan2(sS[p],sC[p])/6.283185307*WW)+WW)%WW;cy[p]=sY[p]/pSize[p]}
  pCX=new Int32Array(P);pCY=new Int32Array(P);const bd=new Float32Array(P).fill(1e9);
  for(let y=0;y<WH;y++)for(let x=0;x<WW;x++){const p=prov[y*WW+x];let dx=Math.abs(x-cx[p]);if(dx>WW/2)dx=WW-dx;const d=dx*dx+(y-cy[p])**2;if(d<bd[p]){bd[p]=d;pCX[p]=x;pCY[p]=y}}
  pH=new Float32Array(P);pBiome=new Uint8Array(P);
  for(let p=0;p<P;p++){pH[p]=hs[p]/pSize[p];let bb=0,bn=0;for(let b=1;b<15;b++)if(bh[p*15+b]>bn){bn=bh[p*15+b];bb=b}pBiome[p]=bb}
  const pairs=new Set();
  for(let i=0;i<N;i++){const x=i%WW,a=prov[i];const r=prov[i-x+(x+1)%WW];if(r!==a)pairs.add(a<r?a*65536+r:r*65536+a);if(i+WW<N){const d=prov[i+WW];if(d!==a)pairs.add(a<d?a*65536+d:d*65536+a)}}
  adj=Array.from({length:P},()=>[]);
  for(const k of pairs){const a=Math.floor(k/65536),b=k%65536;adj[a].push(b);adj[b].push(a);if(pLand[a]!==pLand[b]){pCoast[pLand[a]?a:b]=1}}
  // water body names: the largest water body and big enclosed seas are seas/oceans, small ones lakes
  const wcomp=comp;let ocean=-1,osz=0;for(let c=0;c<wcomp.count;c++){const f=wcomp.first[c];if(!land[f]&&wcomp.size[c]>osz){osz=wcomp.size[c];ocean=c}}
  pName=[];pFert=new Float32Array(P);
  for(let p=0;p<P;p++){const nm=mkName(p);
    if(pLand[p]){pName.push(nm);let f=FERT[pBiome[p]];if(pH[p]>.34)f*=.6;if(pRiver[p])f=Math.min(1,f+.2);pFert[p]=f}
    else{const c=wcomp.id[pCY[p]*WW+pCX[p]];const coastal=adj[p].some(q=>pLand[q]);
      pName.push(c!==ocean&&wcomp.size[c]<5000*KS*KS?'Озеро '+nm:!coastal&&c===ocean?'Океан '+nm:pSize[p]<1400?'Залив '+nm:'Море '+nm)}}
  return comp;
}

function hexd(a,b){let dx=Math.abs(pCX[a]-pCX[b]);if(dx>WW/2)dx=WW-dx;return Math.hypot(dx,pCY[a]-pCY[b])}

function genNations(comp){
  own=new Int16Array(P).fill(-1);pCap=new Int16Array(P).fill(-1);pTown=new Uint8Array(P);
  const csz=p=>comp.size[comp.id[pCY[p]*WW+pCX[p]]];
  const cand=[];for(let p=0;p<P;p++)if(pLand[p]&&pSize[p]>=40&&pFert[p]>=.5&&csz(p)>=670*KS*KS&&Math.abs(pCY[p]/WH-.5)<.36)cand.push(p);
  let first=-1,fd=1e9;for(const p of cand){let dx=Math.abs(pCX[p]-WW*.5);const d=dx*dx+(pCY[p]-WH*.44)**2;if(d<fd){fd=d;first=p}}
  const caps=[first];
  while(caps.length<16&&caps.length<cand.length){let bp=-1,bdist=-1;for(const p of cand){let m=1e9;for(const c of caps)m=Math.min(m,hexd(p,c));if(m>bdist){bdist=m;bp=p}}caps.push(bp)}
  const rest=caps.slice(1).sort((a,b)=>hexd(a,first)-hexd(b,first));natCap=[first,...rest];
  natCap.forEach((p,n)=>{own[p]=n;pCap[p]=n});
  // ancient era: player ~14 provinces, others 8..18
  const target=natCap.map((_,n)=>n===0?14:8+((h2(n,5,S)*11)|0)),count=natCap.map(()=>1);
  for(let round=0;round<20;round++)for(let n=0;n<natCap.length;n++){if(count[n]>=target[n])continue;let bp=-1,bs=-1;
    for(let p=0;p<P;p++){if(own[p]!==n)continue;for(const q of adj[p]){if(own[q]>=0||!pLand[q])continue;let nb=0;for(const r of adj[q])if(own[r]===n)nb++;const s=pFert[q]*.6+h2(q,n,S)*.2+nb*.35-hexd(q,natCap[n])/55+(pSize[q]>30?.1:0);if(s>bs){bs=s;bp=q}}}
    if(bp>=0){own[bp]=n;count[n]++}}
  natCol=NAT.map(n=>pack(...n[2]));natBrd=NAT.map(n=>pack(n[2][0]*1.2+40,n[2][1]*1.2+40,n[2][2]*1.2+40));
  pPop=new Float32Array(P);pRel=new Int8Array(P).fill(-1);pBld=Array.from({length:P},()=>[]);pRes=new Array(P).fill(null);pSlots=new Uint8Array(P);pMood=new Uint8Array(P);
  for(let p=0;p<P;p++){if(!pLand[p])continue;const o=own[p],r=.7+.6*h2(p,77,S);
    pPop[p]=pSize[p]*Math.max(.05,pFert[p])*(o>=0?55:9)*r*(pCap[p]>=0?2.6:1);
    pRel[p]=o>=0?NREL[o]:(h2(p,88,S)<.6?2:-1);pMood[p]=52+((h2(p,66,S)*36)|0);
    pSlots[p]=2+(pSize[p]>60?1:0)+(pSize[p]>120?1:0)+(pCoast[p]?1:0);
    if(pH[p]>.34&&h2(p,99,S)<.4)pRes[p]={k:ORES[(h2(p,98,S)*ORES.length)|0],found:o===0&&h2(p,97,S)<.5};
    if(o>=0){const opts=bOpts(p);const nb=Math.min(pSlots[p]-1,1+((pFert[p]*3)|0));for(let j=0;j<nb&&j<opts.length;j++)pBld[p].push(opts[(j+((h2(p,55,S)*3)|0))%opts.length])
      if(pCap[p]>=0&&!pBld[p].includes('shrn'))pBld[p].push('shrn')}}
  for(let n=0;n<natCap.length;n++){const ps=[];for(let p=0;p<P;p++)if(own[p]===n&&pCap[p]<0)ps.push(p);ps.sort((a,b)=>pPop[b]-pPop[a]);ps.slice(0,n===0?2:1).forEach(p=>pTown[p]=1)}
  // trade routes
  routes=[];const pairsR=[[0,1],[0,2],[1,3],[2,4]];
  for(const[a,b]of pairsR){if(a<natCap.length&&b<natCap.length){const path=bfsPath(natCap[a],natCap[b]);if(path)routes.push(path)}}
  computeFog();
}
function bOpts(p){const b=pBiome[p],o=[];if([9,10,11,13,6].includes(b)&&pH[p]<=.6)o.push('farm');if([5,8,12].includes(b))o.push('lumb');if(pH[p]>.34)o.push('quar');if(pCoast[p])o.push('fish');if([4,6,11,13].includes(b))o.push('past');o.push('gran','mark');return o}
function bfsPath(a,b){const prev=new Int32Array(P).fill(-2);prev[a]=-1;const q=[a];for(let h=0;h<q.length;h++){const p=q[h];if(p===b)break;for(const n of adj[p])if(prev[n]===-2){prev[n]=p;q.push(n)}}if(prev[b]===-2)return null;const path=[];for(let p=b;p!==-1;p=prev[p])path.push(p);return path.reverse()}

/* ---------- entry point (ui.js boot) ---------- */
async function wgRun(ws,onProgress){const now=()=>(typeof performance!=='undefined'?performance:Date).now(),T={};let t=now();const lap=k=>{const n=now();T[k]=Math.round(n-t);t=n};
  const say=s=>onProgress&&onProgress(s);if(ws&&wgWarm&&!await wgWarm)throw new Error('workers did not start');
  say('Рельеф…');const base=new Float32Array(N),ridge=new Float32Array(N);let mn=1e9,mx=-1e9;
  await wgQ(ws,wgPass(ws,'A',0,()=>({}),(r,y0)=>{base.set(r.B,y0*WW);ridge.set(r.R,y0*WW);mn=Math.min(mn,r.r[0]);mx=Math.max(mx,r.r[1])}));lap('relief');
  const sea=seaLevel(base,mn,mx);lap('sea');
  say('Климат…');hgt=new Float32Array(N);biome=new Uint8Array(N);let comp=null;
  const pH_=wgQ(ws,wgPass(ws,'H',0,(y0,y1,ya,yb,sl)=>({B:sl(base),R:sl(ridge),L:sl(land),sea,mn}),(r,y0)=>{hgt.set(r.H,y0*WW);biome.set(r.BI,y0*WW)}));
  if(ws)comp=labelBy(land);await pH_;if(!ws)comp=labelBy(land);lap('climate');
  say('Реки и провинции…');baseCol=new Uint32Array(N);prov=new Int32Array(N);const sd=provSeeds(comp);
  const pCP=wgQ(ws,[...wgPass(ws,'P',0,(y0,y1,ya,yb,sl)=>({L:sl(land),H:sl(hgt),CI:sl(comp.id),sd}),(r,y0)=>prov.set(r.PR,y0*WW)),
    ...wgPass(ws,'C',1,(y0,y1,ya,yb,sl)=>({L:sl(land),H:sl(hgt),BI:ws?biome.slice(y0*WW,y1*WW):biome}),(r,y0)=>baseCol.set(r.C,y0*WW))]);
  genRivers(base,sea);await pCP;lap('colour+rivers+assign');
  genProvinces(comp,sd.nS);lap('provinces');
  say('Державы…');genNations(comp);lap('nations');
  return T}
async function generateWorld(onProgress){
  const now=()=>(typeof performance!=='undefined'?performance:Date).now(),t0=now();let ws=wkPool(),T;
  if(ws)try{T=await wgRun(ws,onProgress)}catch(e){console.warn('worldgen: workers failed, single thread',e);wkKill();ws=false}
  if(!ws)T=await wgRun(null,onProgress);
  const ms=Math.round(now()-t0);wgStat={ms,workers:ws?ws.length:0,t:T,P};
  return {ms,provinces:P,workers:wgStat.workers,t:T};
}
/* start the workers while the page is still loading (browser only; node has no Worker) */
if(typeof Worker!=='undefined')wkPool();
