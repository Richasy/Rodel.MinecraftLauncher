/* Design-only theme preference. No native settings or Windows appearance changes. */
const themeLabels = { light: '浅色', dark: '深色', system: '跟随系统', contrast: '高对比度' };
const systemDark = matchMedia('(prefers-color-scheme: dark)');
const systemContrast = matchMedia('(forced-colors: active)');
const reviewQuery = new URLSearchParams(location.search);
let themePreference = Object.hasOwn(themeLabels, reviewQuery.get('theme')) ? reviewQuery.get('theme') : 'light';
const reviewSelection = reviewQuery.get('selection');
if (['current','lifted','lighter'].includes(reviewSelection)) {
  document.documentElement.dataset.selection = reviewSelection;
  document.querySelectorAll('[data-selection-choice]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.selectionChoice === reviewSelection)));
}
function applyPrototypeTheme(choice = themePreference) {
  if (!Object.hasOwn(themeLabels, choice)) return;
  themePreference = choice;
  const resolved = systemContrast.matches ? 'contrast' : choice === 'system' ? (systemDark.matches ? 'dark' : 'light') : choice;
  document.documentElement.dataset.theme = resolved;
  document.documentElement.dataset.themePreference = choice;
  document.querySelectorAll('[data-theme-choice]').forEach(button => button.setAttribute('aria-pressed', button.dataset.themeChoice === choice));
  const select = document.querySelector('select[aria-label="界面主题"]');
  if (select) {
    const label = themeLabels[choice === 'contrast' ? 'system' : choice];
    select.value = label;
    select.parentElement.querySelector('.combo-face > span')?.replaceChildren(document.createTextNode(label));
  }
  document.querySelector('.review-note').textContent = systemContrast.matches ? 'Windows 强制颜色 · HTML 模拟数据' : '壁纸设计待评审 · 模拟数据';
  window.dispatchEvent(new CustomEvent('prototype-theme-changed', { detail: { choice, resolved } }));
}
document.addEventListener('click', event => {
  const selection = event.target.closest('[data-selection-choice]');
  if (selection) {
    document.documentElement.dataset.selection = selection.dataset.selectionChoice;
    document.querySelectorAll('[data-selection-choice]').forEach(button => button.setAttribute('aria-pressed', String(button === selection)));
    applyPrototypeTheme('dark');
  }
  const button = event.target.closest('[data-theme-choice]');
  if (button) { closeCombo(); applyPrototypeTheme(button.dataset.themeChoice); }
});
document.addEventListener('change', event => {
  if (event.target.matches('select[aria-label="界面主题"]')) {
    const choice = Object.keys(themeLabels).find(key => themeLabels[key] === event.target.value);
    applyPrototypeTheme(choice);
  }
});
// Settings is rendered dynamically; reuse the same active preference when it appears.
new MutationObserver(() => {
  const select = document.querySelector('select[aria-label="界面主题"]');
  if (select && select.value !== themeLabels[themePreference === 'contrast' ? 'system' : themePreference]) applyPrototypeTheme();
}).observe(document.getElementById('nav-page'), { childList: true, subtree: true });
systemDark.addEventListener('change', () => applyPrototypeTheme());
systemContrast.addEventListener('change', () => applyPrototypeTheme());
applyPrototypeTheme();
