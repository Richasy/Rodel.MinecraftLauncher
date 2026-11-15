/* Shared, bounded contrast calculation. Input rectangles are actual character boxes. */
(function (scope) {
  const linear = Array.from({ length: 256 }, (_, i) => { const s = i / 255; return s <= .04045 ? s / 12.92 : ((s + .055) / 1.055) ** 2.4; });
  function luminance(rgb) { return .2126 * linear[Math.round(rgb[0])] + .7152 * linear[Math.round(rgb[1])] + .0722 * linear[Math.round(rgb[2])]; }
  function scene(rgb, light, x) {
    const stops = light ? [[0,.62],[.24,.56],[.42,.24],[.56,0],[1,0]] : [[0,.68],[.32,.58],[.64,.25],[1,.16]];
    const tint = light ? [249,246,235] : [9,22,27];
    let index=1;
    while (index<stops.length-1 && x>stops[index][0]) index++;
    const a=stops[index-1],b=stops[index],t=Math.max(0,Math.min(1,(x-a[0])/(b[0]-a[0])));
    const opacity=a[1]+(b[1]-a[1])*t;
    for(let c=0;c<3;c++) rgb[c]=rgb[c]*(1-opacity)+tint[c]*opacity;
    return rgb;
  }
  function blurWeight(x) {
    const stops=[[0,1],[.14,1],[.28,166/255],[.42,51/255],[.54,0],[1,0]];
    let i=1;while(i<stops.length-1 && x>stops[i][0])i++;
    const a=stops[i-1],b=stops[i],t=Math.max(0,Math.min(1,(x-a[0])/(b[0]-a[0])));
    return a[1]+(b[1]-a[1])*t;
  }
  function analyze(image, layout) {
    const scale = Math.max(layout.width / image.width, layout.height / image.height);
    const diagonalExtent = layout.width * Math.sqrt(3) / 2 + layout.height * .5;
    const left = (layout.width - image.width * scale) * layout.position[0];
    const top = (layout.height - image.height * scale) * layout.position[1];
    const characterCount = layout.groups.reduce((sum, group) => sum + group.rects.length, 0);
    const characterBudget = Math.max(9, Math.floor(65536 / Math.max(1, characterCount)));
    const decisions = layout.groups.map(group => {
      const ink = luminance(group.color), threshold = group.large ? 3 : 4.5;
      let bad = 0, total = 0, previousBad = false, adjacentBad = false, strength = .302;
      for (const rect of group.rects) {
        let localBad = 0, localTotal = 0;
        const columns = Math.max(3, Math.min(12, Math.ceil(rect[2] * image.sampleWidth / (image.width * scale)), Math.floor(characterBudget / 3)));
        const rows = Math.max(3, Math.min(12, Math.ceil(rect[3] * image.sampleHeight / (image.height * scale)), Math.floor(characterBudget / columns)));
        for (let row = 0; row < rows; row++) for (let column = 0; column < columns; column++) {
          const fx = (column + .5) / columns, fy = (row + .5) / rows;
          const x = rect[0] + rect[2] * fx, y = rect[1] + rect[3] * fy;
          if (x < 0 || y < 0 || x >= layout.width || y >= layout.height) continue;
          const sx = Math.min(image.sampleWidth - 1, Math.max(0, Math.floor((x - left) / scale / image.width * image.sampleWidth)));
          const sy = Math.min(image.sampleHeight - 1, Math.max(0, Math.floor((y - top) / scale / image.height * image.sampleHeight)));
          const index = (sy * image.sampleWidth + sx) * 4, a = image.pixels[index + 3] / 255;
          const rgb = [0, 1, 2].map(c => image.pixels[index + c] * a + layout.background[c] * (1 - a));
          const diagonal = (x * Math.sqrt(3) / 2 + (layout.height - y) * .5) / diagonalExtent;
          if (image.blurredPixels) {
            const weight=blurWeight(diagonal),ba=image.blurredPixels[index+3]/255;
            // The blurred branch overlays the original; transparent blur edges reveal the sharp scene below.
            for(let c=0;c<3;c++) rgb[c]=image.blurredPixels[index+c]*ba*weight+rgb[c]*(1-ba*weight);
          }
          scene(rgb, layout.light, diagonal);
          const background = luminance(rgb), ratio = (Math.max(ink, background) + .05) / (Math.min(ink, background) + .05);
          if (ratio < threshold) {
            localBad++;
            // Preserve Player's 30% baseline, strengthen only where that rim is still lost.
            const edge = ink < .3 ? 255 : 0;
            for (const opacity of [.302, .45, .6, .72]) {
              const rim = luminance(rgb.map(value => edge * opacity + value * (1 - opacity)));
              strength = Math.max(strength, opacity);
              if ((Math.max(ink, rim) + .05) / (Math.min(ink, rim) + .05) >= threshold) break;
            }
          }
          localTotal++;
        }
        const poor = localTotal > 0 && localBad / localTotal >= .4;
        if (poor && previousBad) adjacentBad = true;
        previousBad = poor; bad += localBad; total += localTotal;
      }
      const fraction = total ? bad / total : 0;
      return { outline: total > 0 && (fraction >= (group.outlined ? .05 : .12) || adjacentBad), fraction, strength, edge: ink < .3 ? 255 : 0, samples: total };
    });
    return decisions;
  }
  scope.WallpaperContrast = { analyze, luminance };
})(typeof self === 'undefined' ? globalThis : self);


