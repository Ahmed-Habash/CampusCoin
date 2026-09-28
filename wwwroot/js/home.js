(() => {
    const reduced = () => document.documentElement.classList.contains('reduce-motion') || matchMedia('(prefers-reduced-motion: reduce)').matches;
    const reveals = document.querySelectorAll('[data-reveal]');
    const hero = document.querySelector('.cc-hero');
    const visual = document.querySelector('[data-hero-visual]');
    const toolsSection = document.querySelector('[data-tools-section]');

    const markInView = (el) => {
        if (!el) return;
        el.classList.add('is-inview');
    };

    const playToolsFocus = () => {
        if (!toolsSection || reduced()) return;
        toolsSection.classList.remove('is-tools-focus');
        void toolsSection.offsetWidth;
        toolsSection.querySelectorAll('[data-reveal]').forEach(el => el.classList.add('is-visible'));
        toolsSection.classList.add('is-tools-focus');
        clearTimeout(playToolsFocus._t);
        playToolsFocus._t = setTimeout(() => toolsSection.classList.remove('is-tools-focus'), 1200);
    };

    if ('IntersectionObserver' in window && !reduced()) {
        const observer = new IntersectionObserver(entries => entries.forEach(entry => {
            if (!entry.isIntersecting) return;
            entry.target.classList.add('is-visible');
            observer.unobserve(entry.target);
        }), { threshold: .12, rootMargin: '0px 0px -8% 0px' });
        reveals.forEach(item => { if (!item.classList.contains('is-visible')) observer.observe(item); });

        const strip = document.querySelector('[data-reveal-strip]');
        if (strip) {
            const stripObserver = new IntersectionObserver(entries => entries.forEach(entry => {
                if (!entry.isIntersecting) return;
                strip.classList.add('is-lit');
                strip.querySelectorAll('.cc-feature').forEach(card => card.classList.add('is-visible'));
                stripObserver.unobserve(strip);
            }), { threshold: .2, rootMargin: '0px 0px -8% 0px' });
            stripObserver.observe(strip);
        }

        const heroObserver = new IntersectionObserver(entries => entries.forEach(entry => {
            if (!entry.isIntersecting) return;
            markInView(hero);
            markInView(visual);
            heroObserver.unobserve(entry.target);
        }), { threshold: .15, rootMargin: '0px' });
        if (hero) heroObserver.observe(hero);
    } else {
        reveals.forEach(item => item.classList.add('is-visible'));
        document.querySelector('[data-reveal-strip]')?.classList.add('is-lit');
        document.querySelectorAll('.cc-feature').forEach(card => card.classList.add('is-visible'));
        markInView(hero);
        markInView(visual);
    }

    /* Why section cycle removed — static strip */

    const scrollToHash = (hash, { replayTools = false } = {}) => {
        if (!hash || hash === '#') return false;
        const target = document.querySelector(hash);
        if (!target) return false;
        const top = target.getBoundingClientRect().top + scrollY - 12;
        // Jump to Tools instantly — smooth scroll would scrub through the 420vh How-it-works pin
        const skipStory = hash === '#practical-tools';
        scrollTo({ top, behavior: (reduced() || skipStory) ? 'auto' : 'smooth' });
        if (replayTools && skipStory) {
            requestAnimationFrame(() => playToolsFocus());
        }
        return true;
    };

    document.querySelectorAll('.public-nav a[href*="#"]').forEach(link => {
        link.addEventListener('click', (e) => {
            const url = new URL(link.href, location.href);
            if (url.pathname !== location.pathname) return;
            const hash = url.hash;
            if (!hash) return;
            e.preventDefault();
            history.pushState(null, '', hash);
            scrollToHash(hash, { replayTools: hash === '#practical-tools' });
        });
    });

    if (location.hash) {
        requestAnimationFrame(() => scrollToHash(location.hash, { replayTools: location.hash === '#practical-tools' }));
    }

    const track = document.querySelector('[data-story-track]');
    const rail = document.querySelector('[data-story-rail]');
    const storySteps = [...document.querySelectorAll('[data-story-step]')];
    const storyPanels = [...document.querySelectorAll('[data-story-panel]')];
    const storyDots = [...document.querySelectorAll('[data-dot-step]')];
    const storyLabel = document.querySelector('#story-preview-label');
    const storyBar = document.querySelector('[data-story-progress]');
    const names = storySteps.map(s => s.dataset.storyStep);
    let lastIndex = -1;
    let queued = false;
    const desktopPin = () => matchMedia('(min-width: 721px)').matches;

    const activateStory = (name, { syncScroll = false } = {}) => {
        const index = names.indexOf(name);
        if (index < 0) return;
        storySteps.forEach((step, i) => {
            step.classList.toggle('is-active', i === index);
            step.classList.toggle('is-near', Math.abs(i - index) === 1);
            step.setAttribute('aria-pressed', String(i === index));
        });
        storyDots.forEach(dot => dot.classList.toggle('is-active', dot.dataset.dotStep === name));
        if (index !== lastIndex) {
            storyPanels.forEach(panel => {
                const on = panel.dataset.storyPanel === name;
                if (on) {
                    panel.classList.remove('is-active');
                    void panel.offsetWidth;
                    panel.classList.add('is-active');
                } else {
                    panel.classList.remove('is-active');
                }
            });
            if (storyLabel) storyLabel.textContent = `LIVE PREVIEW · ${name === 'ask' ? 'ASK COIN' : name.toUpperCase()}`;
            lastIndex = index;
        }
        if (syncScroll && track && desktopPin()) {
            const total = Math.max(1, track.offsetHeight - innerHeight);
            const targetY = track.getBoundingClientRect().top + scrollY + (index / Math.max(1, names.length - 1)) * total;
            scrollTo({ top: targetY, behavior: reduced() ? 'auto' : 'smooth' });
        }
    };

    const updateStory = () => {
        if (!track || !rail || !storySteps.length) return;

        if (!desktopPin()) {
            rail.style.transform = '';
            if (storyBar) storyBar.style.width = '0%';
            return;
        }

        const rect = track.getBoundingClientRect();
        const total = Math.max(1, track.offsetHeight - innerHeight);
        const scrolled = Math.min(total, Math.max(0, -rect.top));
        const progressValue = scrolled / total;
        if (storyBar) storyBar.style.width = `${(progressValue * 100).toFixed(2)}%`;

        const maxIndex = names.length - 1;
        const exact = progressValue * maxIndex;
        const index = Math.min(maxIndex, Math.max(0, Math.round(exact)));
        const stepHeight = storySteps[0].getBoundingClientRect().height || storySteps[0].offsetHeight || 1;
        rail.style.transform = `translate3d(0, ${-(exact * stepHeight)}px, 0)`;
        activateStory(names[index]);
    };

    const tick = () => {
        updateStory();
        queued = false;
    };

    addEventListener('scroll', () => {
        if (queued) return;
        queued = true;
        requestAnimationFrame(tick);
    }, { passive: true });
    addEventListener('resize', () => {
        if (queued) return;
        queued = true;
        requestAnimationFrame(tick);
    }, { passive: true });

    storySteps.forEach(step => {
        step.addEventListener('click', () => activateStory(step.dataset.storyStep, { syncScroll: true }));
    });
    storyDots.forEach(dot => {
        dot.addEventListener('click', () => activateStory(dot.dataset.dotStep, { syncScroll: true }));
    });

    activateStory(names[0] || 'track');
    updateStory();

    /* Hero visual: idle drift + cursor parallax */
    if (hero && visual && !reduced() && matchMedia('(hover:hover) and (pointer:fine)').matches) {
        let tracking = false;
        let cx = 0, cy = 0, tx = 0, ty = 0;
        let idleT = 0;
        let running = true;
        const apply = () => {
            visual.style.setProperty('--mx', `${(tx * 22).toFixed(2)}px`);
            visual.style.setProperty('--my', `${(ty * 16).toFixed(2)}px`);
            visual.style.setProperty('--rx', `${(-ty * 6.5).toFixed(2)}deg`);
            visual.style.setProperty('--ry', `${(tx * 8).toFixed(2)}deg`);
            visual.style.setProperty('--fx', `${(tx * 34).toFixed(2)}px`);
            visual.style.setProperty('--fy', `${(ty * 26).toFixed(2)}px`);
        };
        const loop = () => {
            if (!running) return;
            if (tracking) {
                tx += (cx - tx) * .12;
                ty += (cy - ty) * .12;
            } else {
                idleT += 0.008;
                // Slow figure-eight style drift
                const ix = Math.sin(idleT) * 0.38;
                const iy = Math.sin(idleT * 0.72 + 1.1) * 0.28;
                tx += (ix - tx) * .035;
                ty += (iy - ty) * .035;
            }
            apply();
            requestAnimationFrame(loop);
        };
        hero.addEventListener('pointermove', (e) => {
            const r = visual.getBoundingClientRect();
            if (!r.width || !r.height) return;
            tracking = true;
            visual.classList.add('is-tracking');
            cx = Math.max(-1, Math.min(1, ((e.clientX - r.left) / r.width - .5) * 2));
            cy = Math.max(-1, Math.min(1, ((e.clientY - r.top) / r.height - .5) * 2));
        }, { passive: true });
        hero.addEventListener('pointerleave', () => {
            tracking = false;
            visual.classList.remove('is-tracking');
            // Keep idle phase continuous from current pose
            idleT = Math.atan2(ty / 0.28, tx / 0.38) || idleT;
        });
        // Pause idle when tab hidden
        document.addEventListener('visibilitychange', () => {
            running = document.visibilityState === 'visible';
            if (running) requestAnimationFrame(loop);
        });
        apply();
        requestAnimationFrame(loop);
    } else if (hero && visual && !reduced()) {
        // Touch / no-hover: gentle CSS-free JS idle
        let idleT = 0;
        const loop = () => {
            idleT += 0.008;
            const tx = Math.sin(idleT) * 0.32;
            const ty = Math.sin(idleT * 0.72 + 1.1) * 0.24;
            visual.style.setProperty('--mx', `${(tx * 18).toFixed(2)}px`);
            visual.style.setProperty('--my', `${(ty * 14).toFixed(2)}px`);
            visual.style.setProperty('--rx', `${(-ty * 5).toFixed(2)}deg`);
            visual.style.setProperty('--ry', `${(tx * 6).toFixed(2)}deg`);
            visual.style.setProperty('--fx', `${(tx * 28).toFixed(2)}px`);
            visual.style.setProperty('--fy', `${(ty * 22).toFixed(2)}px`);
            if (document.visibilityState === 'visible') requestAnimationFrame(loop);
        };
        requestAnimationFrame(loop);
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible') requestAnimationFrame(loop);
        });
    }
})();
