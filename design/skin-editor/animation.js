import { createSkinCanvas } from './canvas.js';

// Browser-only design reference. Product walking is sampled by SkinAnimation and rendered by D3D11.
function sample(seconds) {
  const swing = Math.cos(seconds / 1.2 * Math.PI * 2);
  const limbs = {
    rightArm: [-swing * .65, 22], leftArm: [swing * .65, 22],
    rightLeg: [swing * .91, 12], leftLeg: [-swing * .91, 12]
  };
  return { transform(part, point) {
    const limb = limbs[part];
    if (!limb) return point;
    const [angle, pivotY] = limb;
    const y = point[1] - pivotY, z = point[2];
    return [point[0], pivotY + y * Math.cos(angle) - z * Math.sin(angle), y * Math.sin(angle) + z * Math.cos(angle)];
  } };
}

export function bindAnimationPreview(source) {
  const dialog = document.querySelector('#animation-dialog'), canvas = document.querySelector('#animation-canvas');
  let renderer = null, request = 0, started = 0, last = 0;
  function frame(now) {
    if (!dialog.open || !renderer) return;
    if (now - last >= 50) { renderer.setPose(sample((now - started) / 1000)); last = now; }
    request = requestAnimationFrame(frame);
  }
  document.querySelector('#preview-document').onclick = () => {
    dialog.showModal(); started = performance.now(); last = 0;
    renderer = createSkinCanvas(canvas, { ...source.getDocumentOptions(), maxResolution: 2048 });
    renderer.setPixels(source.getPixels()); renderer.setPose(sample(0)); renderer.setView({ zoom: .8 });
    request = requestAnimationFrame(frame);
  };
  document.querySelector('#animation-close').onclick = () => dialog.close();
  dialog.addEventListener('close', () => { cancelAnimationFrame(request); renderer?.destroy(); renderer = null; });
}
