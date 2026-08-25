const canvas = document.querySelector('#jarvis-3d');
let gl = null;
let program = null;
let positionBuffer = null;
let colorBuffer = null;
let count = 0;
let rotation = 0;
let last = 0;

function mat4Perspective(out, fovy, aspect, near, far) {
  const f = 1 / Math.tan(fovy / 2), nf = 1 / (near - far);
  out[0]=f/aspect; out[1]=0; out[2]=0; out[3]=0;
  out[4]=0; out[5]=f; out[6]=0; out[7]=0;
  out[8]=0; out[9]=0; out[10]=(far+near)*nf; out[11]=-1;
  out[12]=0; out[13]=0; out[14]=(2*far*near)*nf; out[15]=0;
  return out;
}
function mat4Model(out, angle, scale=1) {
  const c=Math.cos(angle),s=Math.sin(angle);
  out.set([
    c*scale,0,-s*scale,0,
    0,scale,0,0,
    s*scale,0,c*scale,0,
    0,-0.1,0,1
  ]); return out;
}
function addPoint(points, colors, x,y,z, intensity=1) {
  points.push(x,y,z); colors.push(0.2*intensity,0.9*intensity,1.0*intensity,0.9);
}
function buildAvatar() {
  const pts=[], cols=[];
  const rings=(rx,ry,rz,y0,steps=72,rows=20)=>{
    for(let r=0;r<=rows;r++){
      const v=-Math.PI/2+(r/rows)*Math.PI, cv=Math.cos(v), sv=Math.sin(v);
      for(let i=0;i<steps;i++){
        const a=(i/steps)*Math.PI*2;
        let x=Math.cos(a)*cv*rx, y=y0+sv*ry, z=Math.sin(a)*cv*rz;
        const faceBoost=(z>0?1.25:0.9);
        addPoint(pts,cols,x,y,z,faceBoost);
      }
    }
  };
  rings(0.58,0.78,0.48,0.72,88,24);
  rings(0.34,0.42,0.30,-0.05,54,12);
  for(let i=0;i<90;i++){
    const a=Math.PI*(i/89), x=-0.95+1.9*(i/89), y=-0.5+0.12*Math.sin(a*2), z=0.05*Math.cos(a*2);
    addPoint(pts,cols,x,y,z,0.55);
    addPoint(pts,cols,-x,y,z,0.55);
  }
  for(let r=0;r<4;r++){
    const radius=0.22+r*0.075, yy=-0.72;
    for(let i=0;i<80;i++){
      const a=i/80*Math.PI*2;
      addPoint(pts,cols,Math.cos(a)*radius,yy,Math.sin(a)*radius,1.3-r*0.18);
    }
  }
  for(let i=0;i<120;i++){
    const a=i/120*Math.PI*2;
    const radius=1.02;
    addPoint(pts,cols,Math.cos(a)*radius,0.04,Math.sin(a)*radius,0.45);
  }
  return {pts:new Float32Array(pts),cols:new Float32Array(cols)};
}
function compile(type,source){
  const s=gl.createShader(type); gl.shaderSource(s,source); gl.compileShader(s);
  if(!gl.getShaderParameter(s,gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s)||'shader'); return s;
}
function init(){
  if(!canvas) return;
  gl=canvas.getContext('webgl',{alpha:true,antialias:true});
  if(!gl) { canvas.parentElement.classList.add('no-webgl'); return; }
  const vs=compile(gl.VERTEX_SHADER,`attribute vec3 p; attribute vec4 c; uniform mat4 proj; uniform mat4 model; varying vec4 v; void main(){gl_Position=proj*model*vec4(p,1.0); gl_PointSize=2.2; v=c;}`);
  const fs=compile(gl.FRAGMENT_SHADER,`precision mediump float; varying vec4 v; void main(){float d=distance(gl_PointCoord,vec2(.5)); if(d>.5) discard; float a=(1.0-d*2.0)*v.a; gl_FragColor=vec4(v.rgb,a);}`);
  program=gl.createProgram(); gl.attachShader(program,vs); gl.attachShader(program,fs); gl.linkProgram(program);
  if(!gl.getProgramParameter(program,gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program)||'program');
  const mesh=buildAvatar(); count=mesh.pts.length/3;
  positionBuffer=gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER,positionBuffer); gl.bufferData(gl.ARRAY_BUFFER,mesh.pts,gl.STATIC_DRAW);
  const pb=gl.getAttribLocation(program,'p'); gl.enableVertexAttribArray(pb); gl.vertexAttribPointer(pb,3,gl.FLOAT,false,0,0);
  colorBuffer=gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER,colorBuffer); gl.bufferData(gl.ARRAY_BUFFER,mesh.cols,gl.STATIC_DRAW);
  const cb=gl.getAttribLocation(program,'c'); gl.enableVertexAttribArray(cb); gl.vertexAttribPointer(cb,4,gl.FLOAT,false,0,0);
  resize(); window.addEventListener('resize',resize,{passive:true}); requestAnimationFrame(frame);
}
function resize(){ if(!gl||!canvas)return; const dpr=Math.min(window.devicePixelRatio||1,2); const r=canvas.getBoundingClientRect(); canvas.width=Math.max(1,Math.floor(r.width*dpr)); canvas.height=Math.max(1,Math.floor(r.height*dpr)); gl.viewport(0,0,canvas.width,canvas.height); }
function frame(t){
  if(!gl||!program)return;
  const dt=Math.min(0.05,(t-last)/1000||0.016); last=t; rotation+=dt*0.42;
  gl.clearColor(0,0,0,0); gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT); gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA,gl.ONE); gl.useProgram(program);
  gl.bindBuffer(gl.ARRAY_BUFFER,positionBuffer); const pLoc=gl.getAttribLocation(program,'p'); gl.enableVertexAttribArray(pLoc); gl.vertexAttribPointer(pLoc,3,gl.FLOAT,false,0,0);
  gl.bindBuffer(gl.ARRAY_BUFFER,colorBuffer); const cLoc=gl.getAttribLocation(program,'c'); gl.enableVertexAttribArray(cLoc); gl.vertexAttribPointer(cLoc,4,gl.FLOAT,false,0,0);
  const aspect=canvas.width/Math.max(1,canvas.height); const proj=new Float32Array(16), model=new Float32Array(16); mat4Perspective(proj,Math.PI/3,aspect,.1,10); mat4Model(model,rotation,1.06);
  gl.uniformMatrix4fv(gl.getUniformLocation(program,'proj'),false,proj); gl.uniformMatrix4fv(gl.getUniformLocation(program,'model'),false,model);
  gl.drawArrays(gl.POINTS,0,count);
  requestAnimationFrame(frame);
}
init();
