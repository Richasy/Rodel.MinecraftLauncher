/* Pixels never cross to the UI thread. Two downsampled images, one active job. */
importScripts('wallpaper-contrast.js?v=20261016-diagonal-blur2');
const samples = new Map();
let activeFetch;
self.onmessage = async ({ data }) => {
  if (data.cancel) { activeFetch?.abort(); return; }
  const started = performance.now();
  try {
    let image = samples.get(data.image);
    let decoded = false;
    if (!image) {
      activeFetch = new AbortController();
      const response = await fetch(data.image, { signal: activeFetch.signal });
      if (!response.ok) throw new Error('Image unavailable');
      const bitmap = await createImageBitmap(await response.blob());
      try {
        const factor = Math.min(1, 768 / Math.max(bitmap.width, bitmap.height));
        const width = Math.max(1, Math.round(bitmap.width * factor)), height = Math.max(1, Math.round(bitmap.height * factor));
        const canvas = new OffscreenCanvas(width, height), context = canvas.getContext('2d', { willReadFrequently: true });
        context.drawImage(bitmap, 0, 0, width, height);
        image = { width: bitmap.width, height: bitmap.height, sampleWidth: width, sampleHeight: height, canvas, pixels: context.getImageData(0, 0, width, height).data };
      } finally { bitmap.close(); }
      samples.set(data.image, image); decoded = true;
      while (samples.size > 2) samples.delete(samples.keys().next().value);
    } else { samples.delete(data.image); samples.set(data.image, image); }
    const analysisStarted = performance.now();
    let blurredPixels;
    if (data.layout.blur) {
      const scale = Math.max(data.layout.width / image.width, data.layout.height / image.height);
      const radius = Math.max(.5,Math.round(data.layout.blur * image.sampleWidth / (image.width * scale) * 2) / 2);
      if (image.blurRadius !== radius) {
        const canvas = new OffscreenCanvas(image.sampleWidth, image.sampleHeight);
        const context = canvas.getContext('2d',{willReadFrequently:true});
        if (!('filter' in context)) throw new Error('Sample blur unavailable');
        context.filter = 'blur(' + radius + 'px)';
        context.drawImage(image.canvas,0,0);
        image.blurredPixels=context.getImageData(0,0,image.sampleWidth,image.sampleHeight).data;
        image.blurRadius=radius;
      }
      blurredPixels=image.blurredPixels;
    }
    const decisions = WallpaperContrast.analyze({...image,blurredPixels}, data.layout);
    self.postMessage({ revision: data.revision, decisions, decoded, elapsed: performance.now() - started, analysis: performance.now() - analysisStarted, cache: samples.size });
  } catch { self.postMessage({ revision: data.revision, failed: true }); }
  finally { activeFetch = null; }
};
