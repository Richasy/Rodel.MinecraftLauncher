/**
 * A deliberately local design-prototype renderer. The product will use the native
 * Sprout surface and Rodel.Minecraft.Skins; this module does not call Core or MCP.
 * Atlas rectangles and face directions match SkinLayout / SkinGeometry in main.
 * Classic / Slim geometry, with 64 / 128 texel atlases for this design prototype.
 * Brush/fill operations are confined to the selected face;
 * native seam transport, selections and arbitrary paint layers are outside this demo.
 */

const BASE_SIZE = 64;
const CAMERA_DISTANCE = 90;
/** Neutral blank-template color shared with the local AI recoloring demonstration. */
export const PLAIN_SKIN_COLOR = "#D6CFBE";
/** Original templates with suggested models, not Mojang's official Steve/Alex textures. */
export const SKIN_TEMPLATES = Object.freeze([
  { id: "classic", name: "经典蓝衫", description: "棕色短发、青色短袖与蓝紫长裤", shirtColors: ["#269DA3", "#207F87", "#43B1B4"] },
  { id: "alex", name: "Alex 风格", description: "橙色头发、绿色上衣与棕色长裤", model: "slim", shirtColors: ["#74A65E", "#557D48", "#96BD79"] },
  { id: "casual", name: "日常白衫", description: "深肤色、黑色短发与白灰上衣", shirtColors: ["#E6E3D9", "#C3C7C3", "#F5F2E9"] },
  { id: "base", name: "基础人形", description: "基础五官、短发与灰色短装，方便重画", shirtColors: ["#9A9F9B", "#7A827E", "#B5BBB5"] },
  { id: "blank", name: "空白素色", description: "不含五官或服装的中性色底图", shirtColors: [PLAIN_SKIN_COLOR] }
].map(template => Object.freeze({ model: "classic", ...template, shirtColors: Object.freeze(template.shirtColors) })));
const TEMPLATE_PALETTES = {
  classic: {
    skin: "#B98D69", skinLight: "#CCA17E", skinShade: "#966E52",
    hair: "#3D2C23", hairLight: "#513A2B", eyes: "#505779", eyeWhite: "#E8E0D2", mouth: "#75503D",
    pants: "#484D8C", pantsLight: "#5C64A2", pantsShade: "#383D70", shoes: "#443D36", sole: "#302C28", beard: true
  },
  alex: {
    skin: "#D3A580", skinLight: "#E4BB94", skinShade: "#B38365",
    hair: "#B86430", hairLight: "#D7813F", eyes: "#426A42", eyeWhite: "#EAE3D3", mouth: "#A56C52",
    pants: "#786247", pantsLight: "#937955", pantsShade: "#5C4B38", shoes: "#4D4234", sole: "#342F27", longHair: true
  },
  casual: {
    skin: "#916447", skinLight: "#A77A59", skinShade: "#704C39",
    hair: "#272422", hairLight: "#38332D", eyes: "#352E28", eyeWhite: "#E3D8C6", mouth: "#5B3D32",
    pants: "#424E54", pantsLight: "#54646A", pantsShade: "#333E43", shoes: "#B9B5A9", sole: "#7F817B"
  },
  base: {
    skin: "#C49A77", skinLight: "#D4AE8B", skinShade: "#A87E60",
    hair: "#5B4939", hairLight: "#705B47", eyes: "#484037", eyeWhite: "#E8E0D2", mouth: "#8B624C",
    pants: "#626B69", pantsLight: "#7B8581", pantsShade: "#4C5654", shoes: "#535853", sole: "#3D433F", shorts: true
  }
};
const PARTS = {
  head: { base: [0, 0], outer: [32, 0], size: [8, 8, 8], min: [-4, 24, -4], max: [4, 32, 4] },
  body: { base: [16, 16], outer: [16, 32], size: [8, 12, 4], min: [-4, 12, -2], max: [4, 24, 2] },
  rightArm: { base: [40, 16], outer: [40, 32], size: [4, 12, 4], min: [-8, 12, -2], max: [-4, 24, 2] },
  leftArm: { base: [32, 48], outer: [48, 48], size: [4, 12, 4], min: [4, 12, -2], max: [8, 24, 2] },
  rightLeg: { base: [0, 16], outer: [0, 32], size: [4, 12, 4], min: [-4, 0, -2], max: [0, 12, 2] },
  leftLeg: { base: [16, 48], outer: [0, 48], size: [4, 12, 4], min: [0, 0, -2], max: [4, 12, 2] }
};
const FACE_NAMES = ["top", "bottom", "right", "front", "left", "back"];
const MIRROR_PART = { rightArm: "leftArm", leftArm: "rightArm", rightLeg: "leftLeg", leftLeg: "rightLeg" };
const MIRROR_FACE = { right: "left", left: "right" };
const clamp = (value, low, high) => Math.max(low, Math.min(high, value));
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const add = (a, b) => a.map((value, index) => value + b[index]);
const subtract = (a, b) => a.map((value, index) => value - b[index]);
const multiply = (a, amount) => a.map(value => value * amount);
const equalColor = (pixels, offset, color) => color.every((value, channel) => pixels[offset + channel] === value);

function documentOptions({ model = "classic", size = BASE_SIZE } = {}) {
  if (model !== "classic" && model !== "slim") throw new RangeError("Skin model must be classic or slim.");
  if (size !== 64 && size !== 128) throw new RangeError("Skin atlas size must be 64 or 128.");
  return { model, size };
}

function partsFor(model) {
  if (model === "classic") return PARTS;
  return {
    ...PARTS,
    rightArm: { ...PARTS.rightArm, size: [3, 12, 4], min: [-7, 12, -2], max: [-4, 24, 2] },
    leftArm: { ...PARTS.leftArm, size: [3, 12, 4], min: [4, 12, -2], max: [7, 24, 2] }
  };
}

function rgba(color) {
  if (Array.isArray(color) || ArrayBuffer.isView(color)) {
    return [color[0], color[1], color[2], color.length > 3 ? color[3] : 255].map(value => clamp(Math.round(value), 0, 255));
  }
  if (color === "transparent") return [0, 0, 0, 0];
  const hex = String(color).replace(/^#/, "");
  if (/^[\da-f]{3}$/i.test(hex)) return [...hex].map(value => parseInt(value + value, 16)).concat(255);
  if (!/^[\da-f]{6}([\da-f]{2})?$/i.test(hex)) throw new TypeError("Use #rgb, #rrggbb, #rrggbbaa or an RGBA array.");
  return [0, 2, 4].map(offset => parseInt(hex.slice(offset, offset + 2), 16)).concat(hex.length === 8 ? parseInt(hex.slice(6), 16) : 255);
}

function makeFrames({ model, size }) {
  const resolution = size / BASE_SIZE;
  const result = [];
  for (const [part, definition] of Object.entries(partsFor(model))) {
    for (const layer of ["base", "outer"]) {
      const [u, v] = definition[layer];
      const [width, height, depth] = definition.size;
      const inflate = layer === "base" ? 0 : part === "head" ? .5 : .25;
      const min = definition.min.map(value => value - inflate);
      const max = definition.max.map(value => value + inflate);
      const [sx, sy, sz] = subtract(max, min);
      const atlas = {
        top: [u + depth, v, width, depth],
        bottom: [u + depth + width, v, width, depth],
        right: [u, v + depth, depth, height],
        front: [u + depth, v + depth, width, height],
        left: [u + depth + width, v + depth, depth, height],
        back: [u + depth + width + depth, v + depth, width, height]
      };
      const geometry = {
        front: [[min[0], max[1], max[2]], [sx, 0, 0], [0, -sy, 0], [0, 0, 1]],
        back: [[max[0], max[1], min[2]], [-sx, 0, 0], [0, -sy, 0], [0, 0, -1]],
        right: [[min[0], max[1], min[2]], [0, 0, sz], [0, -sy, 0], [-1, 0, 0]],
        left: [[max[0], max[1], max[2]], [0, 0, -sz], [0, -sy, 0], [1, 0, 0]],
        top: [[min[0], max[1], min[2]], [sx, 0, 0], [0, 0, sz], [0, 1, 0]],
        bottom: [[min[0], min[1], max[2]], [sx, 0, 0], [0, 0, -sz], [0, -1, 0]]
      };
      for (const face of FACE_NAMES) {
        const [x, y, w, h] = atlas[face];
        const [origin, right, down, normal] = geometry[face];
        result.push({ part, layer, face, x: x * resolution, y: y * resolution, w: w * resolution, h: h * resolution,
          origin, right, down, normal });
      }
    }
  }
  return result;
}

/**
 * Returns independently owned face rectangles in actual atlas texels and matching
 * world-space geometry. At 128 resolution each world unit spans two editable texels.
 */
export function getSkinFrames(options = {}) {
  return makeFrames(documentOptions(options));
}

const frameFor = (frames, hit) => frames.find(frame => frame.part === hit?.part && frame.layer === hit?.layer && frame.face === hit?.face);

/** Uses the same clipped square footprint for painting and the on-canvas cursor. */
function brushFootprints(frames, hit, size, mirror) {
  const frame = frameFor(frames, hit);
  if (!frame || !Number.isInteger(hit.x) || !Number.isInteger(hit.y)
    || hit.x < frame.x || hit.y < frame.y || hit.x >= frame.x + frame.w || hit.y >= frame.y + frame.h) return [];
  const diameter = clamp(Number.isFinite(size) ? Math.round(size) : 1, 1, 32);
  // An even square has its upper-left central pixel under the pointer.
  const start = -Math.floor((diameter - 1) / 2);
  const localX = hit.x - frame.x + start;
  const localY = hit.y - frame.y + start;
  const footprints = [];
  const append = (target, x, y) => {
    if (!target) return;
    const left = clamp(x, 0, target.w), top = clamp(y, 0, target.h);
    const right = clamp(x + diameter, 0, target.w), bottom = clamp(y + diameter, 0, target.h);
    if (right > left && bottom > top) footprints.push({ frame: target, x: left, y: top, w: right - left, h: bottom - top });
  };
  append(frame, localX, localY);
  if (mirror) {
    const mirrored = frameFor(frames, { ...hit, part: MIRROR_PART[hit.part] || hit.part, face: MIRROR_FACE[hit.face] || hit.face });
    if (mirrored) append(mirrored, mirrored.w - localX - diameter, localY);
  }
  return footprints;
}

/** Copies the requested atlas and makes only its model's background faces opaque. */
export function normalizeBaseAlpha(pixels, options = {}) {
  const configuration = documentOptions(options);
  const { size } = configuration;
  if (!(pixels instanceof Uint8ClampedArray) || pixels.length !== size * size * 4) {
    throw new TypeError(`A ${size} × ${size} RGBA Uint8ClampedArray is required.`);
  }
  const result = new Uint8ClampedArray(pixels);
  for (const frame of makeFrames(configuration)) {
    if (frame.layer !== "base") continue;
    for (let y = 0; y < frame.h; y++) {
      for (let x = 0; x < frame.w; x++) {
        result[((frame.y + y) * size + frame.x + x) * 4 + 3] = 255;
      }
    }
  }
  return result;
}

/** Creates a uniform opaque base, leaving outer faces and unused atlas pixels clear. */
function createPlainSkin(configuration) {
  const { size } = configuration;
  const pixels = new Uint8ClampedArray(size * size * 4);
  const color = rgba(PLAIN_SKIN_COLOR);
  for (const frame of makeFrames(configuration)) {
    if (frame.layer !== "base") continue;
    for (let y = 0; y < frame.h; y++) {
      for (let x = 0; x < frame.w; x++) {
        const index = ((frame.y + y) * size + frame.x + x) * 4;
        pixels.set(color, index);
      }
    }
  }
  return pixels;
}

function templateFaceColor(frame, x, y, palette) {
  const { part, face, w } = frame;
  const p = palette;
  if (part === "head") {
    if (face === "top") return x > 1 && x < 6 && y > 0 && y < 7 ? p.hairLight : p.hair;
    if (face === "bottom") return p.skinShade;
    if (face === "back") return y < (p.longHair ? 8 : 6) ? p.hair : p.skinShade;
    if (face === "left" || face === "right") {
      const front = face === "left" ? 0 : w - 1;
      if (y < 3 || (y < (p.longHair ? 7 : 5) && x !== front)) return p.hair;
      return x === front ? p.skin : p.skinShade;
    }
    if (y < 2 || (y === 2 && (x === 0 || x === 7 || (p.longHair && x < 3)))) return p.hair;
    if (y === 3 && (x === 1 || x === 6)) return p.eyeWhite;
    if (y === 3 && (x === 2 || x === 5)) return p.eyes;
    if (y === 4 && (x === 3 || x === 4)) return p.skinShade;
    if (y === 6 && (x === 3 || x === 4)) return p.mouth;
    if (p.beard && y >= 5 && y <= 7 && x >= 2 && x <= 5 && (x === 2 || x === 5 || y === 7)) return p.hairLight;
    return x === 0 || x === 7 ? p.skin : p.skinLight;
  }
  if (part === "body") {
    if (face === "top") return x >= 3 && x <= 4 && y >= 1 && y <= 2 ? p.skinShade : p.shirtLight;
    if (face === "bottom") return p.shirtShade;
    if (face === "front") {
      if (y === 0 && (x === 3 || x === 4)) return p.skin;
      if (x === 0 || x === w - 1 || y === 11) return p.shirtShade;
      if (x === 1 && y >= 2 && y <= 8) return p.shirtLight;
    }
    if (face === "left" || face === "right") return p.shirtShade;
    return p.shirt;
  }
  if (part.endsWith("Arm")) {
    if (face === "top") return p.shirtLight;
    if (face === "bottom") return p.skinShade;
    if (y < 4) return x === 0 || x === w - 1 ? p.shirtShade : p.shirt;
    if (y === 4) return p.shirtShade;
    return x === 0 || face === "back" ? p.skinShade : face === "front" ? p.skinLight : p.skin;
  }
  if (face === "top") return p.pantsShade;
  if (face === "bottom" || y === 11) return p.sole;
  if (y >= 10) return p.shoes;
  if (p.shorts && y >= 6) return x === 0 || face === "back" ? p.skinShade : p.skin;
  if ((p.shorts && y === 5) || x === 0 || x === w - 1) return p.pantsShade;
  if (face === "front" && x === 1 && y >= 2) return p.pantsLight;
  return p.pants;
}

/**
 * Creates an owned RGBA atlas at the requested model and resolution.
 * Base faces are opaque; outer faces and unused pixels remain clear.
 * Higher-resolution templates start with crisp enlarged pixels; subsequent edits
 * address every individual texel, independent of the initial template blocks.
 * Figure templates are drawn here from simple blocks, not copied official assets.
 * Throws RangeError for an unknown ID so callers cannot silently load another skin.
 */
export function createTemplateSkin(id, options = {}) {
  const template = SKIN_TEMPLATES.find(item => item.id === id);
  if (!template) throw new RangeError(`Unknown skin template: ${String(id)}`);
  const configuration = documentOptions(options);
  const { size } = configuration;
  const resolution = size / BASE_SIZE;
  if (id === "blank") return createPlainSkin(configuration);
  const [shirt, shirtShade, shirtLight] = template.shirtColors;
  const palette = { ...TEMPLATE_PALETTES[id], shirt, shirtShade, shirtLight };
  const colors = new Map();
  const pixels = new Uint8ClampedArray(size * size * 4);
  for (const frame of makeFrames(configuration)) {
    if (frame.layer !== "base") continue;
    const logicalFrame = { ...frame, w: frame.w / resolution, h: frame.h / resolution };
    for (let y = 0; y < frame.h; y++) {
      for (let x = 0; x < frame.w; x++) {
        const color = templateFaceColor(logicalFrame, Math.floor(x / resolution), Math.floor(y / resolution), palette);
        if (!colors.has(color)) colors.set(color, rgba(color));
        pixels.set(colors.get(color), ((frame.y + y) * size + frame.x + x) * 4);
      }
    }
  }
  return pixels;
}

/**
 * Creates one independently owned canvas view and pixel document.
 * The caller owns pointer/keyboard handling, document history and synchronization
 * between two views. onPaint(pixels, {changed, tool}) receives an owned pixel copy.
 * Pixel updates are immediate; their redraws share the next animation frame.
 * fillAt accepts an optional atlas-coordinate mask that also bounds flood travel.
 * UV panX/panY and getUvRect use CSS pixels relative to the canvas.
 * setBrushPreview shares paintAt's square footprint and never changes pixels.
 * configure changes model/resolution before setPixels loads a document; its
 * optional template chooses the pristine reset seed without editing caller data.
 * PNG methods return a data URL, so the caller decides whether to download it.
 */
export function createSkinCanvas(canvas, { model = "classic", size = BASE_SIZE, template = "classic", onPaint, onPick, onStatus, maxResolution = Infinity } = {}) {
  const context = canvas.getContext("2d", { alpha: true });
  if (!context) throw new Error("A Canvas 2D context is required.");
  let configuration = documentOptions({ model, size });
  let templateId = template;
  let atlasSize = configuration.size;
  let parts = partsFor(configuration.model);
  let frames = makeFrames(configuration);
  let seed = createTemplateSkin(templateId, configuration);
  let pixels = new Uint8ClampedArray(seed);
  let destroyed = false;
  let width = 1;
  let height = 1;
  let ratio = 1;
  let uvBounds = null;
  let projection = null;
  let image = null;
  let depthBuffer = null;
  let request = 0;
  let brushPreview = null;
  let pose = null;
  const view = {
    mode: "3d", yaw: -.48, pitch: .10, zoom: 1, grid: false,
    part: "all", layer: "base", outerVisible: true, panX: 0, panY: 0
  };

  const matchingPart = frame => view.part === "all" || frame.part === view.part;

  function sizeCanvas() {
    const box = canvas.getBoundingClientRect();
    width = Math.max(1, box.width);
    height = Math.max(1, box.height);
    ratio = Math.min(clamp(window.devicePixelRatio || 1, 1, 2.5), maxResolution / Math.max(width, height));
    const pixelWidth = Math.max(1, Math.round(width * ratio));
    const pixelHeight = Math.max(1, Math.round(height * ratio));
    if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
      canvas.width = pixelWidth;
      canvas.height = pixelHeight;
      image = null;
      depthBuffer = null;
    }
  }

  function renderUv() {
    projection = null;
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    const scale = Math.min((width - 24) / atlasSize, (height - 24) / atlasSize) * view.zoom;
    const cell = Math.max(.5, scale);
    const left = (width - atlasSize * cell) / 2 + view.panX;
    const top = (height - atlasSize * cell) / 2 + view.panY;
    uvBounds = { left, top, cell };
    const dark = document.documentElement.dataset.theme === "dark"
      || document.body.classList.contains("dark")
      || document.documentElement.classList.contains("dark");
    const checkerSize = 4 * atlasSize / BASE_SIZE;
    for (let y = 0; y < atlasSize; y++) {
      for (let x = 0; x < atlasSize; x++) {
        context.fillStyle = (Math.floor(x / checkerSize) + Math.floor(y / checkerSize)) % 2
          ? dark ? "rgba(255,255,255,.075)" : "rgba(76,83,64,.065)"
          : dark ? "rgba(255,255,255,.025)" : "rgba(255,255,255,.35)";
        context.fillRect(left + x * cell, top + y * cell, cell + .15, cell + .15);
      }
    }
    for (const frame of frames) {
      const active = matchingPart(frame) && frame.layer === view.layer;
      context.globalAlpha = active ? 1 : .24;
      for (let y = 0; y < frame.h; y++) {
        for (let x = 0; x < frame.w; x++) {
          const offset = ((frame.y + y) * atlasSize + frame.x + x) * 4;
          if (!pixels[offset + 3]) continue;
          context.fillStyle = `rgba(${pixels[offset]},${pixels[offset + 1]},${pixels[offset + 2]},${pixels[offset + 3] / 255})`;
          context.fillRect(left + (frame.x + x) * cell, top + (frame.y + y) * cell, cell + .15, cell + .15);
        }
      }
      context.globalAlpha = 1;
      context.lineWidth = active ? 1 : .65;
      context.strokeStyle = active ? dark ? "rgba(166,193,137,.72)" : "rgba(68,104,52,.65)"
        : dark ? "rgba(255,255,255,.15)" : "rgba(73,79,61,.18)";
      context.strokeRect(left + frame.x * cell, top + frame.y * cell, frame.w * cell, frame.h * cell);
      if (view.grid && active && cell >= 3) {
        context.beginPath();
        context.lineWidth = .5;
        context.strokeStyle = dark ? "rgba(0,0,0,.35)" : "rgba(25,36,22,.22)";
        for (let x = 1; x < frame.w; x++) {
          context.moveTo(left + (frame.x + x) * cell, top + frame.y * cell);
          context.lineTo(left + (frame.x + x) * cell, top + (frame.y + frame.h) * cell);
        }
        for (let y = 1; y < frame.h; y++) {
          context.moveTo(left + frame.x * cell, top + (frame.y + y) * cell);
          context.lineTo(left + (frame.x + frame.w) * cell, top + (frame.y + y) * cell);
        }
        context.stroke();
      }
    }
    context.globalAlpha = 1;
  }

  function makeProjection() {
    const definition = parts[view.part];
    const center = definition ? multiply(add(definition.min, definition.max), .5) : [0, 16, 0];
    const bounds = definition ? subtract(definition.max, definition.min) : [16, 32, 8];
    const fit = Math.min(width / (Math.max(bounds[0], bounds[2]) * 1.75 + 4), height / (bounds[1] * 1.20 + 4));
    const focal = fit * CAMERA_DISTANCE * view.zoom;
    const cosY = Math.cos(view.yaw), sinY = Math.sin(view.yaw);
    const cosP = Math.cos(view.pitch), sinP = Math.sin(view.pitch);
    const rotate = point => {
      const x = point[0] * cosY + point[2] * sinY;
      const z = -point[0] * sinY + point[2] * cosY;
      return [x, point[1] * cosP - z * sinP, point[1] * sinP + z * cosP];
    };
    const transform = point => rotate(subtract(point, center));
    const project = point => {
      const inverse = 1 / (CAMERA_DISTANCE - point[2]);
      return [(width / 2 + view.panX + point[0] * focal * inverse) * ratio, (height / 2 + view.panY - point[1] * focal * inverse) * ratio, inverse];
    };
    return { focal, transform, rotate, project };
  }

  function render3d() {
    uvBounds = null;
    projection = makeProjection();
    const { transform, rotate, project } = projection;
    if (!image) image = context.createImageData(canvas.width, canvas.height);
    if (!depthBuffer) depthBuffer = new Float32Array(canvas.width * canvas.height);
    image.data.fill(0);
    depthBuffer.fill(Infinity);
    const visibleFrames = frames.filter(frame => matchingPart(frame) && (frame.layer === "base" || view.outerVisible))
      .map(frame => {
        const posedOrigin = pose ? pose.transform(frame.part, frame.origin) : frame.origin;
        const posedRight = pose ? subtract(pose.transform(frame.part, add(frame.origin, frame.right)), posedOrigin) : frame.right;
        const posedDown = pose ? subtract(pose.transform(frame.part, add(frame.origin, frame.down)), posedOrigin) : frame.down;
        const posedNormal = pose ? subtract(pose.transform(frame.part, add(frame.origin, frame.normal)), posedOrigin) : frame.normal;
        const origin = transform(posedOrigin);
        const right = rotate(posedRight);
        const down = rotate(posedDown);
        const normal = rotate(posedNormal);
        return { ...frame, origin, right, down, normal, distance: CAMERA_DISTANCE - add(origin, multiply(add(right, down), .5))[2] };
      })
      .filter(frame => dot(frame.normal, subtract([0, 0, CAMERA_DISTANCE], frame.origin)) > 0)
      .sort((a, b) => b.distance - a.distance);
    for (const frame of visibleFrames) {
      const corners = [
        frame.origin, add(frame.origin, frame.right),
        add(add(frame.origin, frame.right), frame.down), add(frame.origin, frame.down)
      ].map(project);
      const uv = [[0, 0], [frame.w, 0], [frame.w, frame.h], [0, frame.h]];
      const shade = clamp(.73 + .30 * dot(frame.normal, [-.35, .55, .76]), .60, 1.06);
      const vertices = corners.map((point, index) => [...point, uv[index][0] * point[2], uv[index][1] * point[2]]);
      rasterTriangle(vertices[0], vertices[1], vertices[2], frame, shade);
      rasterTriangle(vertices[0], vertices[2], vertices[3], frame, shade);
    }
    context.setTransform(1, 0, 0, 1, 0, 0);
    context.putImageData(image, 0, 0);
  }

  /** Perspective-correct nearest texel rasterization with a true per-pixel depth buffer. */
  function rasterTriangle(a, b, c, frame, shade) {
    const screenWidth = canvas.width;
    const minX = clamp(Math.floor(Math.min(a[0], b[0], c[0])), 0, screenWidth - 1);
    const maxX = clamp(Math.ceil(Math.max(a[0], b[0], c[0])), 0, screenWidth - 1);
    const minY = clamp(Math.floor(Math.min(a[1], b[1], c[1])), 0, canvas.height - 1);
    const maxY = clamp(Math.ceil(Math.max(a[1], b[1], c[1])), 0, canvas.height - 1);
    const denominator = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1]);
    if (Math.abs(denominator) < .0001) return;
    const cellScreenSize = projection.focal / CAMERA_DISTANCE * ratio / (atlasSize / BASE_SIZE);
    const gridWidth = view.grid && cellScreenSize > 5 * ratio ? .55 * ratio / cellScreenSize : 0;
    const output = image.data;
    for (let y = minY; y <= maxY; y++) {
      for (let x = minX; x <= maxX; x++) {
        const wA = ((b[1] - c[1]) * (x + .5 - c[0]) + (c[0] - b[0]) * (y + .5 - c[1])) / denominator;
        const wB = ((c[1] - a[1]) * (x + .5 - c[0]) + (a[0] - c[0]) * (y + .5 - c[1])) / denominator;
        const wC = 1 - wA - wB;
        if (wA < -.00001 || wB < -.00001 || wC < -.00001) continue;
        const inverse = wA * a[2] + wB * b[2] + wC * c[2];
        const depth = 1 / inverse;
        const pixelIndex = y * screenWidth + x;
        if (depth >= depthBuffer[pixelIndex] - .00001) continue;
        const u = clamp((wA * a[3] + wB * b[3] + wC * c[3]) / inverse, 0, frame.w - .00001);
        const v = clamp((wA * a[4] + wB * b[4] + wC * c[4]) / inverse, 0, frame.h - .00001);
        const source = ((frame.y + Math.floor(v)) * atlasSize + frame.x + Math.floor(u)) * 4;
        const alpha = pixels[source + 3] / 255;
        if (alpha === 0) continue;
        const offset = pixelIndex * 4;
        const previousAlpha = output[offset + 3] / 255;
        const totalAlpha = alpha + previousAlpha * (1 - alpha);
        const edge = gridWidth && (u % 1 < gridWidth || v % 1 < gridWidth) ? .73 : 1;
        for (let channel = 0; channel < 3; channel++) {
          output[offset + channel] = (clamp(pixels[source + channel] * shade * edge, 0, 255) * alpha
            + output[offset + channel] * previousAlpha * (1 - alpha)) / totalAlpha;
        }
        output[offset + 3] = totalAlpha * 255;
        depthBuffer[pixelIndex] = depth;
      }
    }
  }

  function render() {
    if (destroyed) return;
    sizeCanvas();
    context.setTransform(1, 0, 0, 1, 0, 0);
    context.clearRect(0, 0, canvas.width, canvas.height);
    if (view.mode === "uv") renderUv();
    else render3d();
    renderBrushPreview();
  }

  function renderBrushPreview() {
    if (!brushPreview) return;
    const footprints = brushFootprints(frames, brushPreview.hit, brushPreview.size, brushPreview.mirror)
      .filter(({ frame }) => matchingPart(frame) && frame.layer === view.layer);
    if (!footprints.length) return;
    context.save();
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.beginPath();
    for (const footprint of footprints) {
      const { frame, x, y, w, h } = footprint;
      if (view.mode === "uv" && uvBounds) {
        const rect = getUvRect(frame.x + x, frame.y + y, w, h);
        context.rect(rect.left, rect.top, rect.width, rect.height);
      } else if (projection) {
        const normal = projection.rotate(frame.normal);
        const origin = projection.transform(frame.origin);
        if (dot(normal, subtract([0, 0, CAMERA_DISTANCE], origin)) <= 0) continue;
        const point = (u, v) => projection.project(projection.transform(add(frame.origin,
          add(multiply(frame.right, u / frame.w), multiply(frame.down, v / frame.h)))));
        const corners = [point(x, y), point(x + w, y), point(x + w, y + h), point(x, y + h)];
        for (let edge = 0; edge < 4; edge++) {
          const a = corners[edge], b = corners[(edge + 1) % 4];
          const steps = Math.max(1, Math.ceil(Math.hypot(b[0] - a[0], b[1] - a[1])));
          let drawing = false;
          for (let step = 0; step <= steps; step++) {
            const t = step / steps;
            const px = a[0] + (b[0] - a[0]) * t, py = a[1] + (b[1] - a[1]) * t;
            const screenX = Math.floor(px), screenY = Math.floor(py);
            // Keep mirrored marks behind other body parts hidden. A small depth
            // allowance retains the base cursor under its own inflated outer shell.
            const visible = screenX >= 0 && screenY >= 0 && screenX < canvas.width && screenY < canvas.height
              && 1 / (a[2] + (b[2] - a[2]) * t) <= depthBuffer[screenY * canvas.width + screenX] + 1.25;
            if (visible) {
              if (drawing) context.lineTo(px / ratio, py / ratio);
              else context.moveTo(px / ratio, py / ratio);
            }
            drawing = visible;
          }
        }
      }
    }
    context.lineJoin = "miter";
    context.strokeStyle = "rgba(0,0,0,.85)";
    context.lineWidth = 3;
    context.stroke();
    context.strokeStyle = "#fff";
    context.lineWidth = 1;
    context.stroke();
    context.restore();
  }

  function setBrushPreview(hit, size = 1, mirror = false) {
    const next = hit ? { hit: { ...hit }, size, mirror } : null;
    if ((!next && !brushPreview) || (next && brushPreview && next.size === brushPreview.size
      && next.mirror === brushPreview.mirror && ["x", "y", "part", "face", "layer"].every(key => next.hit[key] === brushPreview.hit[key]))) return;
    brushPreview = next;
    queueRender();
  }

  /** Returns an atlas rectangle in canvas-relative CSS pixels, or null outside UV mode. */
  function getUvRect(x, y, w = 1, h = 1) {
    if (view.mode !== "uv" || !uvBounds) return null;
    return { left: uvBounds.left + x * uvBounds.cell, top: uvBounds.top + y * uvBounds.cell,
      width: w * uvBounds.cell, height: h * uvBounds.cell };
  }

  function queueRender() {
    if (destroyed || request) return;
    request = requestAnimationFrame(() => {
      request = 0;
      render();
    });
  }

  function hitTest(clientX, clientY) {
    const bounds = canvas.getBoundingClientRect();
    const x = clientX - bounds.left;
    const y = clientY - bounds.top;
    if (x < 0 || y < 0 || x >= bounds.width || y >= bounds.height) return null;
    if (view.mode === "uv" && uvBounds) {
      const u = Math.floor((x - uvBounds.left) / uvBounds.cell);
      const v = Math.floor((y - uvBounds.top) / uvBounds.cell);
      const frame = frames.find(item => matchingPart(item) && item.layer === view.layer
        && u >= item.x && v >= item.y && u < item.x + item.w && v < item.y + item.h);
      return frame ? { x: u, y: v, part: frame.part, face: frame.face, layer: frame.layer } : null;
    }
    if (!projection) return null;
    const direction = [(x - width / 2 - view.panX) / projection.focal, -(y - height / 2 - view.panY) / projection.focal, -1];
    const camera = [0, 0, CAMERA_DISTANCE];
    let closest = Infinity;
    let result = null;
    for (const frame of frames) {
      if (!matchingPart(frame) || frame.layer !== view.layer) continue;
      const normal = projection.rotate(frame.normal);
      const denominator = dot(normal, direction);
      if (denominator >= -.000001) continue;
      const origin = projection.transform(frame.origin);
      const distance = dot(subtract(origin, camera), normal) / denominator;
      if (distance <= 0 || distance >= closest) continue;
      const local = subtract(add(camera, multiply(direction, distance)), origin);
      const right = projection.rotate(frame.right);
      const down = projection.rotate(frame.down);
      const u = dot(local, right) / dot(right, right);
      const v = dot(local, down) / dot(down, down);
      if (u < 0 || v < 0 || u >= 1 || v >= 1) continue;
      closest = distance;
      result = {
        x: frame.x + Math.min(frame.w - 1, Math.floor(u * frame.w)),
        y: frame.y + Math.min(frame.h - 1, Math.floor(v * frame.h)),
        part: frame.part, face: frame.face, layer: frame.layer
      };
    }
    return result;
  }

  function notifyPaint(changed, tool) {
    if (!changed) return;
    queueRender();
    onPaint?.(new Uint8ClampedArray(pixels), { changed, tool });
    onStatus?.({ type: "paint", changed, tool });
  }

  function paintAt(hit, color, size = 1, mirror = false) {
    const footprints = brushFootprints(frames, hit, size, mirror);
    if (!footprints.length) return 0;
    const value = rgba(color);
    const affected = new Set();
    for (const { frame, x, y, w, h } of footprints) {
      for (let row = y; row < y + h; row++) {
        for (let column = x; column < x + w; column++) {
          const index = ((frame.y + row) * atlasSize + frame.x + column) * 4;
          if (!equalColor(pixels, index, value)) {
            pixels.set(value, index);
            affected.add(index);
          }
        }
      }
    }
    notifyPaint(affected.size, "brush");
    return affected.size;
  }

  function fillAt(hit, color, contains = () => true) {
    const frame = frameFor(frames, hit);
    if (!frame || hit.x < frame.x || hit.y < frame.y || hit.x >= frame.x + frame.w || hit.y >= frame.y + frame.h
      || !contains(hit.x, hit.y)) return 0;
    const value = rgba(color);
    const offset = (hit.y * atlasSize + hit.x) * 4;
    const source = Array.from(pixels.slice(offset, offset + 4));
    if (source.every((channel, index) => channel === value[index])) return 0;
    const queue = [[hit.x, hit.y]];
    const visited = new Set();
    let changed = 0;
    while (queue.length) {
      const [x, y] = queue.pop();
      if (x < frame.x || x >= frame.x + frame.w || y < frame.y || y >= frame.y + frame.h) continue;
      if (!contains(x, y)) continue;
      const index = (y * atlasSize + x) * 4;
      if (visited.has(index) || !equalColor(pixels, index, source)) continue;
      visited.add(index);
      pixels.set(value, index);
      changed++;
      queue.push([x - 1, y], [x + 1, y], [x, y - 1], [x, y + 1]);
    }
    notifyPaint(changed, "fill");
    return changed;
  }

  function getColor(hit) {
    if (!hit || hit.x < 0 || hit.y < 0 || hit.x >= atlasSize || hit.y >= atlasSize) return null;
    const offset = (hit.y * atlasSize + hit.x) * 4;
    const color = "#" + Array.from(pixels.slice(offset, offset + (pixels[offset + 3] === 255 ? 3 : 4)))
      .map(value => value.toString(16).padStart(2, "0")).join("");
    onPick?.(color, hit);
    return color;
  }

  function setView(options = {}) {
    if (options.mode !== undefined) view.mode = options.mode === "uv" ? "uv" : "3d";
    if (Number.isFinite(options.yaw)) view.yaw = options.yaw;
    if (Number.isFinite(options.pitch)) view.pitch = clamp(options.pitch, -1.45, 1.45);
    if (Number.isFinite(options.zoom)) view.zoom = clamp(options.zoom, .35, 3.5);
    if (Number.isFinite(options.panX)) view.panX = options.panX;
    if (Number.isFinite(options.panY)) view.panY = options.panY;
    if (options.part !== undefined && (options.part === "all" || parts[options.part])) view.part = options.part;
    if (options.layer === "base" || options.layer === "outer") view.layer = options.layer;
    if (typeof options.grid === "boolean") view.grid = options.grid;
    if (typeof options.outerVisible === "boolean") view.outerVisible = options.outerVisible;
    render();
  }

  function configure(options = {}) {
    const next = documentOptions({ ...configuration, ...options });
    const nextTemplate = options.template ?? templateId;
    if (next.model === configuration.model && next.size === configuration.size && nextTemplate === templateId) return;
    // Construct first so a bad option never destroys the active document.
    const nextSeed = createTemplateSkin(nextTemplate, next);
    const nextFrames = makeFrames(next);
    configuration = next;
    atlasSize = next.size;
    templateId = nextTemplate;
    parts = partsFor(next.model);
    frames = nextFrames;
    seed = nextSeed;
    pixels = new Uint8ClampedArray(seed);
    brushPreview = null;
    uvBounds = null;
    projection = null;
    render();
  }

  function setPixels(next) {
    if (!(next instanceof Uint8ClampedArray) || next.length !== atlasSize * atlasSize * 4) {
      throw new TypeError(`A ${atlasSize} × ${atlasSize} RGBA Uint8ClampedArray is required.`);
    }
    pixels = new Uint8ClampedArray(next);
    queueRender();
  }

  function exportPng() {
    const output = document.createElement("canvas");
    output.width = atlasSize;
    output.height = atlasSize;
    output.getContext("2d").putImageData(new ImageData(new Uint8ClampedArray(pixels), atlasSize, atlasSize), 0, 0);
    return output.toDataURL("image/png");
  }

  const observer = new ResizeObserver(queueRender);
  observer.observe(canvas);
  window.addEventListener("resize", queueRender);
  render();

  return {
    render, setView, getView: () => ({ ...view }),
    setPose(value) { pose = value; render(); },
    configure, getDocumentOptions: () => ({ ...configuration }),
    getPixels: () => new Uint8ClampedArray(pixels),
    setPixels, getSeedPixels: () => new Uint8ClampedArray(seed),
    hitTest, paintAt, fillAt, getColor, exportPng, setBrushPreview, getUvRect,
    capturePng() {
      render();
      return canvas.toDataURL("image/png");
    },
    destroy() {
      destroyed = true;
      observer.disconnect();
      window.removeEventListener("resize", queueRender);
      if (request) cancelAnimationFrame(request);
      image = null;
      depthBuffer = null;
    }
  };
}
