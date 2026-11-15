/* Review images: four generated sheets, seven transparent block icons per sheet. */
(() => {
  const entries = [["grass", "草方块"], ["oak-log", "橡木原木"], ["cherry-log", "樱花原木"], ["moss", "苔藓"], ["snow", "积雪草方块"], ["birch-log", "錾制砂岩"], ["cactus", "仙人掌"], ["oak-planks", "橡木板"], ["stone-bricks", "石砖"], ["deepslate", "深板岩砖"], ["quartz", "石英"], ["copper", "氧化铜"], ["bricks", "红砖"], ["prismarine", "海晶石"], ["diamond", "钻石矿"], ["emerald", "绿宝石矿"], ["gold", "金矿"], ["iron", "铁矿"], ["lapis", "青金石矿"], ["amethyst", "紫水晶"], ["redstone", "红石矿"], ["nylium", "绯红菌岩"], ["end-stone", "末地石"], ["obsidian", "黑曜石"], ["nether-bricks", "下界砖"], ["sea-lantern", "海晶灯"], ["enchanting", "附魔台"], ["chest", "箱子"]];
  const base = new URL('./assets/', document.currentScript.src);
  const presets = entries.map(([id,name]) => ({id,name,src:new URL(id+'.png',base).href}));
  const choice = value => typeof value === 'object' && value ? value : {kind:'preset',id:({adventure:'chest',old:'snow'}[value] || value || 'grass')};
  const resolve = value => { const item=choice(value);return item.kind==='custom'?{name:'自定义图片',src:item.src}:presets.find(p=>p.id===item.id)||presets[0]; };
  const image = value => `<img class="instance-icon-image" src="${String(resolve(value).src).replace(/&/g,'&amp;').replace(/"/g,'&quot;')}" alt="" draggable="false">`;
  window.rodelInstanceIconAssets = {presets,choice,resolve,image};
})();
