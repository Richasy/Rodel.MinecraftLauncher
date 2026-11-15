/* Review assets only: seven frontal pixel faces per generated batch, four batches. */
(() => {
  const entries = [
    ['steve', '史蒂夫'], ['alex', 'Alex'], ['young-boy', '少年'], ['young-girl', '少女'],
    ['elder-man', '白胡子爷爷'], ['elder-woman', '银发奶奶'], ['red-cap', '红帽男孩'],
    ['glasses', '眼镜女孩'], ['teal-scarf', '青色头巾'], ['crown', '王冠'], ['miner', '矿工'],
    ['explorer', '探险家'], ['witch', '女巫'], ['pirate', '海盗'],
    ['cat', '橘猫'], ['dog', '小狗'], ['wolf', '狼'], ['fox', '狐狸'], ['panda', '熊猫'],
    ['axolotl', '美西螈'], ['creeper', '苦力怕'], ['skeleton', '骷髅'], ['zombie', '僵尸'],
    ['enderman', '末影人'], ['pig', '小猪'], ['villager', '村民'], ['snow-golem', '雪傀儡'],
    ['iron-golem', '铁傀儡']
  ];
  const base = new URL('./assets/', document.currentScript.src);
  const presets = entries.map(([id, name], index) => ({id, name, group: index < 14 ? '人物' : '生物', src: new URL(`${id}.png`, base).href}));
  const resolve = avatar => avatar?.kind === 'custom' ? {name: '自定义图片', src: avatar.src} : presets.find(item => item.id === avatar?.id) || presets[0];
  window.rodelAvatarAssets = {presets, resolve};
})();
