const orb = document.querySelector('.core-orb');
const rings = document.querySelectorAll('.ring');
let angle = 0;

function animate() {
  angle += 0.0025;
  rings.forEach((ring, index) => {
    const phase = angle * (index + 1) * 12;
    ring.style.transform = `rotateX(${58 + index * 8}deg) rotateZ(${phase + index * 35}deg)`;
  });
  if (orb) orb.style.filter = `brightness(${1 + Math.sin(angle * 40) * 0.08})`;
  requestAnimationFrame(animate);
}

requestAnimationFrame(animate);
