(() => {
  const card = document.querySelector('.money-garden-card, .financial-tree-card');
  if (!card) return;
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const motionKey = card.dataset.gardenMotion || card.dataset.treeMotion || 'idle';
  if (reduced) {
    card.dataset.gardenMotion = 'idle';
    card.dataset.treeMotion = 'idle';
  } else if (motionKey === 'grow' || motionKey === 'wilt') {
    card.classList.add(`is-${motionKey}`);
    window.setTimeout(() => {
      card.dataset.gardenMotion = 'idle';
      card.dataset.treeMotion = 'idle';
      card.classList.remove('is-grow', 'is-wilt');
    }, 1100);
  }

  const plant = card.querySelector('.mg-tree, .financial-tree-plant');
  const state = (card.dataset.gardenState || card.dataset.treeState || '').toLowerCase();
  if (!reduced && plant && (state === 'healthy' || state === 'flowering')) {
    let frame = 0;
    const tick = () => {
      frame += 0.02;
      const sway = Math.sin(frame) * 1.15;
      plant.style.transform = `translateX(-50%) rotate(${sway}deg)`;
      requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  }
})();
