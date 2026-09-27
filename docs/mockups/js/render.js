'use strict';
/* Map modes, per-pixel map renderer, overlay (rivers, sprites, labels), minimap. */
/* ---------- map modes ---------- */
let mode='pol',mb=new Uint32Array(N),mR,mG,mB,mA,mD,mF,fdBuf=new Uint16Array(0),exBuf=new Uint8Array(0),fogD=null;
/* fog tones: 4 cloud levels x (flat, lit top edge, shaded bottom edge) */
const FOGP=[[14,15,18],[14,15,18],[11,12,14],[20,21,25],[29,31,36],[15,16,19],[27,29,34],[40,42,49],[20,21,25],[36,38,45],[56,59,68],[27,29,34]].map(c=>pack(...c));
const BAY=[0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5].map(v=>(v+.5)/16);
function fertCol(f){return f<.5?[200,90+f*2*120,70]:[200-(f-.5)*2*120,210,70+(f-.5)*2*20]}
function modeParams(){if(!mR||mR.length!==P){mR=new Float32Array(P);mG=new Float32Array(P);mB=new Float32Array(P);mA=new Float32Array(P);mD=new Float32Array(P);mF=new Float32Array(P)}
  for(let p=0;p<P;p++){const L=pLand[p],o=own[p];let c=null,a=0,d=0,f=1;
    if(mode==='pol'){if(L&&o>=0){c=NAT[o][2];a=.56}else if(L){d=.18;f=.94}}
    else if(mode==='rel'){if(L&&pRel[p]>=0){c=REL[pRel[p]][1];a=.56}else if(L)d=.6}
    else if(mode==='trd'){d=.62;f=.85;if(L&&o>=0){c=NAT[o][2];a=.18}}
    else if(mode==='fer'){if(L){c=fertCol(pFert[p]);a=.62}else d=.5}
    if(fogOn&&fog[p]===1){d=Math.max(d,L?.6:.3);f*=L?.6:.8}
    mD[p]=d;mF[p]=f;mA[p]=a;if(c){mR[p]=c[0];mG[p]=c[1];mB[p]=c[2]}}}
function pxNorm(i,p){const c=baseCol[i];let r=c&255,g=(c>>8)&255,b=(c>>16)&255;const d=mD[p];
  if(d){const m=(r+g+b)/3;r+=(m-r)*d;g+=(m-g)*d;b+=(m-b)*d}
  const a=mA[p];if(a){r+=(mR[p]-r)*a;g+=(mG[p]-g)*a;b+=(mB[p]-b)*a}
  const f=mF[p];return pack(r*f,g*f,b*f)}
/* fog look of pixel i (province p) at distance dv (chamfer units, 3/px) from the explored/unexplored boundary:
   signed px distance + cloud noise -> fogginess t, Bayer 4x4 ordered dither frays the edge on both sides */
function pxFog(i,p,dv,x,y){const ex=fog[p]>0,bi=(y>>1)*(WW>>1)+(x>>1);if(dv>=FDC*3)return ex?pxNorm(i,p):FOGP[fogT[bi]];
  const t=((ex?-.5-dv/3:.5+dv/3)+FA*(fogN[bi]/127.5-1)+FB/2)/FB;
  if(t>=1)return FOGP[fogT[bi]];if(t>BAY[((y&3)<<2)|(x&3)])return FOGP[fogT[bi]-fogT[bi]%3+1];if(ex)return pxNorm(i,p);
  const c=baseCol[i];let r=c&255,g=(c>>8)&255,b=(c>>16)&255;const m=(r+g+b)/3,f=.5-.16*t;r+=(m-r)*.7;g+=(m-g)*.7;b+=(m-b)*.7;return pack(r*f,g*f,b*f)}
function paintProv(p){const F=fogOn&&!!fogN&&!!fogD,C=FDC*3;for(let k=pxOff[p],e=pxOff[p+1];k<e;k++){const i=pxList[k];
  if(F&&fogD[i]<C){const y=(i/WW)|0;mb[i]=pxFog(i,p,fogD[i],i-y*WW,y)}else mb[i]=pxNorm(i,p)}}
/* bake mb for [x0,x1)x[y0,y1) (x unwrapped, may be <0 or >WW). With fog: explored mask -> boundary pixels -> two-pass chamfer
   distance (3/4 metric) over the rect + margin; result kept in fogD so paintProv can recolour single provinces later. */
function paintRect(x0,y0,x1,y1){y0=Math.max(0,y0);y1=Math.min(WH,y1);if(y1<=y0||x1<=x0)return;
  const F=fogOn&&!!fogN,M=FDC+1,X0=x0-M,Y0=Math.max(0,y0-M),Y1=Math.min(WH,y1+M),w=x1-x0+2*M,h=Y1-Y0;const d=F&&fdBuf.length<w*h?(fdBuf=new Uint16Array(w*h)):fdBuf;
  if(F){if(exBuf.length<w*h)exBuf=new Uint8Array(w*h);if(!fogD||fogD.length!==N)fogD=new Uint8Array(N);const e=exBuf;
    for(let y=0,o=0;y<h;y++){const row=(Y0+y)*WW;let wx=((X0%WW)+WW)%WW;for(let x=0;x<w;x++,o++){e[o]=fog[prov[row+wx]]?1:0;if(++wx===WW)wx=0}}
    for(let y=0,k=0;y<h;y++)for(let x=0;x<w;x++,k++){const c=e[k];if((x&&e[k-1]!==c)||(x<w-1&&e[k+1]!==c)||(y&&e[k-w]!==c)||(y<h-1&&e[k+w]!==c)){d[k]=0;continue}
      let v=999,q;if(x){q=d[k-1]+3;if(q<v)v=q}if(y){const u=k-w;q=d[u]+3;if(q<v)v=q;if(x){q=d[u-1]+4;if(q<v)v=q}if(x<w-1){q=d[u+1]+4;if(q<v)v=q}}d[k]=v}
    for(let y=h-1,k=w*h-1;y>=0;y--)for(let x=w-1;x>=0;x--,k--){let v=d[k];if(!v)continue;let q;if(x<w-1){q=d[k+1]+3;if(q<v)v=q}
      if(y<h-1){const u=k+w;q=d[u]+3;if(q<v)v=q;if(x<w-1){q=d[u+1]+4;if(q<v)v=q}if(x){q=d[u-1]+4;if(q<v)v=q}}d[k]=v}}
  for(let y=y0;y<y1;y++){const row=y*WW,dr=(y-Y0)*w+M;let wx=((x0%WW)+WW)%WW;
    for(let x=0,n=x1-x0;x<n;x++){const i=row+wx,p=prov[i];
      if(F){const dv=d[dr+x];fogD[i]=dv>255?255:dv;mb[i]=pxFog(i,p,dv,wx,y)}else mb[i]=pxNorm(i,p);
      if(++wx===WW)wx=0}}}
/* full bake: with fog, far pixels are either plain explored colour or cloud texture; only rects around frontier provinces run the distance field */
function computeMode(){const t0=performance.now();modeParams();
  if(fogOn&&fogN&&pxOff){if(!fogD||fogD.length!==N)fogD=new Uint8Array(N);fogD.fill(255);const bw=WW>>1,fr=[];
    for(let y=0,i=0;y<WH;y++){const br=(y>>1)*bw;for(let x=0;x<WW;x++,i++){const p=prov[i];mb[i]=fog[p]?pxNorm(i,p):FOGP[fogT[br+(x>>1)]]}}
    for(let p=0;p<P;p++)if(fog[p]&&adj[p].some(q=>!fog[q]))fr.push(p);if(fr.length)paintAround(fr)}
  else paintRect(0,0,WW,WH);
  drawMini();drawMiniView();dirtyMap=true;fogMs.mode=performance.now()-t0}

/* ---------- rendering ---------- */
const cv=$('map'),ctx=cv.getContext('2d'),ov=$('ov'),octx=ov.getContext('2d');
const ZL=[1,2,3,4,5,6,8];
const cam={z:3,px:0,py:0};
let img,u32,colW,colS,colL,colR,dirtyMap=true,dirtyOv=true,hov=-1,sel=-1;
const BG=pack(62,67,74),SEL=pack(255,255,255);
function resize(){cv.width=ov.width=Math.max(320,innerWidth||document.documentElement.clientWidth||1280);cv.height=ov.height=Math.max(240,innerHeight||document.documentElement.clientHeight||720);img=ctx.createImageData(cv.width,cv.height);u32=new Uint32Array(img.data.buffer);
  colW=new Int32Array(cv.width);colS=new Int32Array(cv.width);colL=new Int32Array(cv.width);colR=new Int32Array(cv.width);clampCam();dirtyMap=true}
function clampCam(){const z=cam.z,WZ=WW*z,H_=cv.height;cam.px=((cam.px%WZ)+WZ)%WZ;const top=54;if(WH*z<H_-top)cam.py=-top-((H_-top)-WH*z)/2|0;else cam.py=Math.max(-top-40,Math.min(WH*z-H_+40,cam.py))}
function edge(p,c,l,u,r,d,z){let kind=0,nc=0;const qs=[l,u,r,d];
  for(let k=0;k<4;k++){const q=qs[k];if(q<0||q===p)continue;
    if(p===sel||q===sel)return SEL;
    if(fogOn&&(!fog[p]||!fog[q]))continue;
    const op=own[p],oq=own[q];
    if(op!==oq&&(op>=0||oq>=0)&&pLand[p]&&pLand[q]){kind=3;nc=natBrd[op>=0?op:oq]}
    else if(k<2&&pLand[p]!==pLand[q]){if(kind<2)kind=2}
    else if(k<2&&kind<1)kind=1}
  if(kind===3)return nc;if(kind===2)return dk(c,.42);if(kind===1)return pLand[p]?dk(c,z>=2?.72:.84):(z>=2?dk(c,.9):c);return c}
function render(){
  const W_=cv.width,H_=cv.height,z=cam.z,WZ=WW*z;
  for(let sx=0;sx<W_;sx++){let a=(cam.px+sx)%WZ;if(a<0)a+=WZ;const wx=(a/z)|0;colW[sx]=wx;colS[sx]=a-wx*z;colL[sx]=wx===0?WW-1:wx-1;colR[sx]=wx===WW-1?0:wx+1}
  let o=0;
  for(let sy=0;sy<H_;sy++){const b=cam.py+sy;if(b<0||b>=WH*z){u32.fill(BG,o,o+W_);o+=W_;continue}
    const wy=(b/z)|0,ss=b-wy*z,row=wy*WW,up=wy>0?row-WW:row,dn=wy<WH-1?row+WW:row,eT=ss===0,eB=z>1&&ss===z-1;
    for(let sx=0;sx<W_;sx++){const wx=colW[sx],i=row+wx,p=prov[i];let c=mb[i];const cs=colS[sx];
      if(cs===0||eT||eB||(z>1&&cs===z-1)){const l=cs===0?prov[row+colL[sx]]:p,u=eT?prov[up+wx]:p,r=z>1&&cs===z-1?prov[row+colR[sx]]:p,d=eB?prov[dn+wx]:p;
        if(l!==p||u!==p||r!==p||d!==p)c=edge(p,c,l,u,r,d,z)}
      if(p===hov&&c!==SEL)c=lt(c,.2);else if(p===sel&&c!==SEL)c=lt(c,.1);
      u32[o++]=c}}
  ctx.putImageData(img,0,0)}

const SPR={
  cap:[['...F...','...FF..','...F...','R.RRR.R','RRRRRRR','.WWWWW.','.WDWDW.','.WWDWW.'],'cap'],
  town:[['..R..','.RRR.','RRRRR','.WDW.','.WDW.'],'town'],
  farm:[['Y.Y.Y','.Y.Y.','Y.Y.Y'],{Y:'#e3c85e'}],lumb:[['.T.T.','TTTTT','.B.B.'],{T:'#2d5a33',B:'#7a5634'}],quar:[['.G.','GGG','GgG'],{G:'#9a968e',g:'#5d5a55'}],
  fish:[['A.A','.A.','AAA'],{A:'#e8eef2'}],past:[['.W.W','WWWW','W..W'],{W:'#eee8d8'}],shrn:[['.S.','SSS','.S.'],{S:'#f2cf6a'}],mark:[['MMM','M.M'],{M:'#d49a5a'}],gran:[['.O.','OOO','OOO'],{O:'#b88a52'}],
  sc0:[['.HH.F','.HH.O','CCCCT','.CC.T','.CC..','.L.L.'],'nat'],sc1:[['.HH.O','.HH.F','CCCCT','.CC.T','.CC..','L...L'],'nat'],
  flag:[['WFFF','WFFF','WF..','W...','W...'],'nat']};
function spr(sx,sy,key,n,ps){const[rows,pal0]=SPR[key];let pal=pal0;
  if(pal0==='cap'||pal0==='town'){const c=NAT[n][2];pal={F:`rgb(${c})`,R:`rgb(${c.map(v=>v*.7|0)})`,W:'#e8dcc4',D:'#3a2e24'}}
  else if(pal0==='nat'){const c=NAT[n][2];pal={C:`rgb(${c})`,F:key==='flag'?`rgb(${c.map(v=>Math.min(255,v*1.15+20)|0)})`:'#ffe27a',O:'#f0863a',H:'#e2b38a',T:'#7a5634',L:'#2e2620',W:'#e8dcc4'}}
  const w=rows[0].length,h=rows.length,ox=sx-(w*ps>>1),oy=sy-(h*ps>>1);
  octx.fillStyle='rgba(0,0,0,.45)';octx.fillRect(ox+ps,oy+ps,w*ps,h*ps);
  for(let y=0;y<h;y++)for(let x=0;x<w;x++){const ch=rows[y][x];if(ch==='.')continue;octx.fillStyle=pal[ch];octx.fillRect(ox+x*ps,oy+y*ps,ps,ps)}}
function label(t,x,y,font,fill,sw,ls){octx.font=font;octx.letterSpacing=ls||'0px';octx.textAlign='center';octx.textBaseline='middle';octx.lineJoin='round';octx.strokeStyle='rgba(22,26,31,.84)';octx.lineWidth=sw;octx.strokeText(t,x,y);octx.fillStyle=fill;octx.fillText(t,x,y)}
const FD=()=>getComputedStyle(document.documentElement).getPropertyValue('--fd');
const FU=()=>getComputedStyle(document.documentElement).getPropertyValue('--fu');
let fdCache='',fuCache='';
/* sea labels. Candidates are ranked once per world: open water first (own size + a share of the neighbouring sea
   zones; zones touching land are penalised); lakes and bays never take part, at x1 only the most open ocean zones.
   Each frame a greedy pass keeps a generous spacing between labels, so a water region carries one name at most; labels
   shown last frame go first (no reshuffling while panning); candidates near screen edges, under UI cards, over nation
   names, over land or over unexplored fog are skipped. */
let seaOrd=[],seaFor=null,seaPrev=[],seaPrevZ=0;const seaTw=new Map();let seaTwK='';
function seaRank(){seaFor=prov;seaPrev=[];const c=[];
  for(let p=0;p<P;p++){if(pLand[p]||pSize[p]<2500)continue;let sc=pSize[p],co=0;for(const q of adj[p])if(pLand[q])co++;else sc+=pSize[q]*.35;c.push([p,co?sc*.6:sc,!co])}
  c.sort((a,b)=>b[1]-a[1]);seaOrd=c}
function seaLabels(z,W_,H_,boxes){if(seaFor!==prov)seaRank();
  const fs=z>=3?14:13,ls=z===1?'.22em':'.16em',f=`500 ${fs}px ${fdCache}`,WZ=WW*z,ui=uiRects(),top=ui.top+26,sp=z===1?560:z===2?460:520,sp2=sp*sp;
  const lim=z===1&&seaOrd.length?seaOrd[Math.min(seaOrd.length-1,seaOrd.length>>3)][1]:0;
  octx.font=f;octx.letterSpacing=ls;if(seaTwK!==f+ls){seaTw.clear();seaTwK=f+ls}
  const water=(X,Y)=>{const wy=Math.floor((Y+cam.py)/z);if(wy<0||wy>=WH)return false;let wx=Math.floor((X+cam.px)/z)%WW;if(wx<0)wx+=WW;const q=prov[wy*WW+wx];return !pLand[q]&&(!fogOn||fog[q]>0)};
  const prev=seaPrevZ===z?seaPrev:[],cand=prev.concat(seaOrd.filter(c=>!prev.includes(c[0])&&(z>1||(c[2]&&c[1]>=lim))).map(c=>c[0])),out=[];
  for(const p of cand){if(fogOn&&!fog[p])continue;const sy=pCY[p]*z-cam.py;if(sy-fs<top||sy+fs>H_-28)continue;
    let sx=(pCX[p]*z-cam.px)%WZ;if(sx<0)sx+=WZ;if(sx>W_)continue;
    let tw=seaTw.get(p);if(tw===undefined){tw=octx.measureText(pName[p]).width;seaTw.set(p,tw)}
    const x0=sx-tw/2-10,y0=sy-fs/2-6,w=tw+20,h=fs+12;if(x0<28||x0+w>W_-28)continue;
    let ok=true;for(const q of out){const dx=q[1]-sx,dy=(q[2]-sy)*1.5;if(dx*dx+dy*dy<sp2){ok=false;break}}if(!ok)continue;
    for(const r of ui.r)if(x0<r.right+14&&x0+w>r.left-14&&y0<r.bottom+14&&y0+h>r.top-14){ok=false;break}if(!ok)continue;
    for(const b of boxes)if(x0<b[0]+b[2]&&x0+w>b[0]&&y0<b[1]+b[3]&&y0+h>b[1]){ok=false;break}if(!ok)continue;
    for(let k=0;k<=4&&ok;k++)ok=water(x0+w*k/4,sy)&&(k&1||(water(x0+w*k/4,y0)&&water(x0+w*k/4,y0+h)));if(!ok)continue;
    out.push([p,sx,sy]);boxes.push([x0,y0,w,h])}
  seaPrev=out.map(o=>o[0]);seaPrevZ=z;
  for(const[p,x,y]of out)label(pName[p],x,y,f,'rgba(188,204,217,.66)',0,ls)}
/* screen rects of the floating UI (labels keep clear of them) */
const UIEL=['panel','notes','modes','mini','lead'];
function uiRects(){const r=[];for(const id of UIEL){const e=$(id);if(!e||e.hidden||!e.offsetHeight)continue;r.push(e.getBoundingClientRect())}const t=$('top');return{r,top:t?t.offsetHeight:54}}
function drawOverlay(t){
  const W_=ov.width,H_=ov.height,z=cam.z,WZ=WW*z;octx.clearRect(0,0,W_,H_);
  const each=(wx,fn)=>{let s=(wx*z-cam.px)%WZ;if(s<0)s+=WZ;for(let k=s-WZ;k<W_+WZ;k+=WZ)if(k>-400&&k<W_+400)fn(k)};
  const vis=p=>!fogOn||fog[p]>0;
  if(mode==='trd'){octx.save();octx.lineWidth=Math.max(2,z*.8);octx.setLineDash([z*3,z*2]);octx.lineDashOffset=-(t/40)%(z*5);octx.strokeStyle='#f0c860';octx.lineCap='round';
    for(const path of routes){let prevX=pCX[path[0]]+.5;const pts=[[prevX,pCY[path[0]]+.5]];for(let k=1;k<path.length;k++){let x=pCX[path[k]]+.5;while(x-prevX>WW/2)x-=WW;while(prevX-x>WW/2)x+=WW;pts.push([x,pCY[path[k]]+.5]);prevX=x}
      each(pts[0][0],sx0=>{octx.beginPath();pts.forEach(([x,y],k)=>{const sx=sx0+(x-pts[0][0])*z,sy=y*z-cam.py;k&&(!fogOn||(fog[path[k]]&&fog[path[k-1]]))?octx.lineTo(sx,sy):octx.moveTo(sx,sy)});octx.stroke()})}
    octx.restore()}
  // rivers (only where explored)
  octx.save();octx.lineCap='round';octx.lineJoin='round';
  for(let j=0;j<riverPaths.length;j++){const rp=riverPaths[j],vm=fogOn?rvm[j]:null;if(vm&&!vm.any)continue;if(rp.y1*z-cam.py<-10||rp.y0*z-cam.py>H_+10)continue;const pts=rp.pts,n=pts.length;
    each(pts[0][0],sx0=>{for(let seg=0;seg<4;seg++){const a=Math.floor(seg*(n-1)/4),b=Math.floor((seg+1)*(n-1)/4);if(b<=a)continue;
      octx.beginPath();let pen=false;for(let k=a;k<=b;k++){if(vm&&!vm[k]){pen=false;continue}const sx=sx0+(pts[k][0]-pts[0][0])*z,sy=pts[k][1]*z-cam.py;pen?octx.lineTo(sx,sy):octx.moveTo(sx,sy);pen=true}
      const w=Math.max(1,z*.34)*(.6+seg*.3);octx.strokeStyle='rgba(20,34,48,.55)';octx.lineWidth=w+1.6;octx.stroke();octx.strokeStyle=mode==='ter'||mode==='pol'?'#4d7fa6':'#56789a';octx.lineWidth=w;octx.stroke()}})}
  octx.restore();
  const ps=Math.max(2,Math.round(z*.75));
  // buildings
  if(z>=5)for(let p=0;p<P;p++){if(!pBld[p].length||!vis(p))continue;const sy=pCY[p]*z-cam.py;if(sy<-80||sy>H_+80)continue;
    each(pCX[p],sx=>{pBld[p].forEach((b,k)=>{const a=k*2.2+.6,rr=z*5.5;spr(sx+Math.cos(a)*rr,sy+Math.sin(a)*rr+z*2,b,0,Math.max(2,Math.round(z*.55)))})})}
  // cities
  if(z>=2)for(let p=0;p<P;p++){if((pCap[p]<0&&!pTown[p])||!vis(p))continue;if(pTown[p]&&z<3)continue;const sy=pCY[p]*z-cam.py+z/2;if(sy<-60||sy>H_+60)continue;
    each(pCX[p],sx=>{spr(sx+z/2,sy,pCap[p]>=0?'cap':'town',own[p],ps);if(z>=3)label(pName[p],sx+z/2,sy+ps*5+8,pCap[p]>=0?`700 14px ${fdCache}`:`500 12px ${fuCache}`,pCap[p]>=0?'#ffffff':'#eceef0',pCap[p]>=0?3.5:3,pCap[p]>=0?'.03em':'0px')})}
  // province names
  if(z>=4)for(let p=0;p<P;p++){if(!pLand[p]||pCap[p]>=0||pTown[p]||!vis(p)||(z===4&&pSize[p]<160))continue;const sy=pCY[p]*z-cam.py;if(sy<-20||sy>H_+20)continue;each(pCX[p],sx=>label(pName[p],sx,sy-z*3,`500 11px ${fuCache}`,'rgba(236,238,241,.84)',3))}
  const boxes=[];   // screen boxes of nation names (sea names keep clear of them)
  // nation labels
  if(z<=4)for(let n=0;n<natCap.length;n++){if(!met(n))continue;let sc=0,ss=0,sy=0,tot=0;for(let p=0;p<P;p++)if(own[p]===n&&(!fogOn||fog[p])){const a=pCX[p]/WW*6.283185307;sc+=Math.cos(a)*pSize[p];ss+=Math.sin(a)*pSize[p];sy+=pCY[p]*pSize[p];tot+=pSize[p]}
    if(!tot)continue;const cx=((Math.atan2(ss,sc)/6.283185307*WW)+WW)%WW,cy=sy/tot,fs=Math.max(13,Math.min(38,Math.sqrt(tot)*z*.16)),capY=pCY[natCap[n]];
    let yy=cy*z-cam.py;if(z>=2&&(!fogOn||fog[natCap[n]])&&Math.abs(cy-capY)*z<fs*.9+ps*8)yy=capY*z-cam.py-ps*6-fs*.6;
    const c=NAT[n][2],txt=NAT[n][0].toUpperCase();octx.font=`700 ${fs}px ${fdCache}`;octx.letterSpacing=(fs*.22)+'px';const tw=octx.measureText(txt).width;
    each(cx,sx=>{boxes.push([sx-tw/2,yy-fs/2,tw,fs]);label(txt,sx,yy,`700 ${fs}px ${fdCache}`,`rgba(${c.map(v=>Math.min(255,v*1.25+50)|0)},.92)`,Math.max(3,fs/6),(fs*.22)+'px')})}
  // sea names: at most one per open-water region (see seaLabels)
  if(z<=3)seaLabels(z,W_,H_,boxes);
  // scouts: dotted route (only over explored ground), target flag, walking figure with a torch
  if(scouts.length){const pz=Math.max(2,Math.round(z*.75)),fr=paused?0:((t/260)|0)&1;octx.save();octx.lineCap='butt';
    const seen=(x,y)=>{if(!fogOn)return true;let X=Math.floor(x)%WW;if(X<0)X+=WW;const i=Math.min(WH-1,Math.max(0,Math.floor(y)))*WW+X;return fog[prov[i]]>0&&(!fogD||fogD[i]>=18)};
    for(const s of scouts){const R=s.path,a=R[s.k],b=R[Math.min(s.k+1,R.length-1)];let ax=pCX[a]+.5,bx=pCX[b]+.5;if(bx-ax>WW/2)bx-=WW;else if(ax-bx>WW/2)bx+=WW;
      const x=ax+(bx-ax)*s.t,y=pCY[a]+.5+(pCY[b]-pCY[a])*s.t,pts=[[x,y]];let px=bx;
      for(let k=s.k+1;k<R.length;k++){let xx=pCX[R[k]]+.5;while(xx-px>WW/2)xx-=WW;while(px-xx>WW/2)xx+=WW;pts.push([xx,pCY[R[k]]+.5]);px=xx}
      const sy0=y*z-cam.py;if(sy0<-400||sy0>H_+400)continue;
      const runs=[];let cur=null;   // route sampled every 2 world px; unexplored stretches break the line
      for(let k=1;k<pts.length;k++){const[x0,y0]=pts[k-1],[x1,y1]=pts[k],n=Math.max(1,Math.ceil(Math.hypot(x1-x0,y1-y0)/2));
        for(let j=k===1?0:1;j<=n;j++){const u=j/n,X=x0+(x1-x0)*u,Y=y0+(y1-y0)*u;if(seen(X,Y)){if(!cur)runs.push(cur=[]);cur.push(X,Y)}else cur=null}}
      each(x,sx0=>{if(pts.length>1){octx.beginPath();for(const r of runs)if(r.length>2)for(let k=0;k<r.length;k+=2){const X=sx0+(r[k]-x)*z,Y=r[k+1]*z-cam.py;k?octx.lineTo(X,Y):octx.moveTo(X,Y)}
          const lw=Math.max(2,Math.round(z*.5));octx.setLineDash([lw*1.5,lw*1.5]);octx.lineDashOffset=-(t/60)%(lw*3);octx.strokeStyle='rgba(22,26,31,.72)';octx.lineWidth=lw+2;octx.stroke();octx.strokeStyle='#ffffff';octx.lineWidth=lw;octx.stroke();
          if(!s.auto){const e=pts[pts.length-1];spr(sx0+(e[0]-x)*z+pz,e[1]*z-cam.py-pz*2,'flag',0,pz)}}
        spr(sx0,sy0-pz*2,'sc'+fr,0,pz)})}
    octx.restore()}
}
function frame(t){requestAnimationFrame(frame);if(!P||!colW||!prov)return;scoutTick(t);if(dirtyMap){dirtyMap=false;dirtyOv=true;render()}if(dirtyOv||mode==='trd'||scouts.length){dirtyOv=false;drawOverlay(t)}}

/* ---------- minimap ---------- */
const mcv=$('minicv'),mctx=mcv.getContext('2d'),mimg=mctx.createImageData(256,144),m32=new Uint32Array(mimg.data.buffer);
let miniBase=null;
function drawMini(){const st=WW/256,sy=WH/144;for(let y=0;y<144;y++){const row=Math.min(WH-1,(y*sy+sy/2)|0)*WW;for(let x=0;x<256;x++)m32[y*256+x]=mb[row+((x*st+st/2)|0)]}mctx.putImageData(mimg,0,0);miniBase=mimg}
function drawMiniView(){if(!miniBase||!cv.width)return;mctx.putImageData(miniBase,0,0);const z=cam.z,st=WW/256,sy=WH/144,x0=cam.px/z/st,y0=cam.py/z/sy,w=cv.width/z/st,h=cv.height/z/sy;
  for(const s of scouts){const p=s.path[s.k],mx=Math.round(pCX[p]/st),my=Math.round(pCY[p]/sy);mctx.fillStyle='rgba(20,24,29,.75)';mctx.fillRect(mx-2,my-2,5,5);mctx.fillStyle='#ffffff';mctx.fillRect(mx-1,my-1,3,3)}
  mctx.lineWidth=1;
  for(const dx of [-256,0,256]){const rx=Math.round(x0+dx)+.5,ry=Math.round(y0)+.5,rw=Math.round(w),rh=Math.round(h);mctx.strokeStyle='rgba(20,24,29,.55)';mctx.strokeRect(rx-1,ry-1,rw+2,rh+2);mctx.strokeStyle='#ffffff';mctx.strokeRect(rx,ry,rw,rh)}}

