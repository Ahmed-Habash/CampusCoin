(() => {
    const theme = document.querySelector('[data-preference="theme"]');
    const text = document.querySelector('[data-preference="text"]');
    const motion = document.querySelector('[data-preference="motion"]');
    if (theme && text && motion) {
        const savedTheme = localStorage.getItem('campus-theme-preference') || localStorage.getItem('campus-theme') || 'system';
        theme.value = ['system','light','dark'].includes(savedTheme) ? savedTheme : 'system';
        text.value = localStorage.getItem('campus-large-text') === 'true' ? 'large' : 'normal';
        motion.value = localStorage.getItem('campus-motion') || 'full';
        const withThemeSmooth = (apply) => {
            const root = document.documentElement;
            if (root.classList.contains('reduce-motion') || matchMedia('(prefers-reduced-motion: reduce)').matches) {
                apply();
                return;
            }
            root.classList.add('theme-smooth');
            apply();
            clearTimeout(window.__themeSmoothTimer);
            window.__themeSmoothTimer = setTimeout(() => root.classList.remove('theme-smooth'), 220);
        };
        const applyTheme = value => {
            const root = document.documentElement;
            const resolved = value === 'system' ? (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light') : value;
            withThemeSmooth(() => {
                root.dataset.theme = resolved;
                localStorage.setItem('campus-theme-preference', value);
                localStorage.setItem('campus-theme', resolved);
                window.dispatchEvent(new Event('themechange'));
            });
        };
        theme.addEventListener('change', () => applyTheme(theme.value));
        text.addEventListener('change', () => {
            const large = text.value === 'large';
            document.documentElement.classList.toggle('large-text', large);
            localStorage.setItem('campus-large-text', String(large));
            window.dispatchEvent(new Event('resize'));
        });
        motion.addEventListener('change', () => { const reduced = motion.value === 'reduced'; document.documentElement.classList.toggle('reduce-motion', reduced); localStorage.setItem('campus-motion', motion.value); });
    }

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value
        || document.querySelector('[data-currency-autosave] input[name="__RequestVerificationToken"]')?.value
        || document.querySelector('[data-xp-autosave] input[name="__RequestVerificationToken"]')?.value
        || document.querySelector('[data-futureyou-autosave] input[name="__RequestVerificationToken"]')?.value;

    // Auto-save currency (and admin currency view) without a Save button.
    const codeSelect = document.querySelector('[data-currency-field="code"]');
    const modeSelect = document.querySelector('[data-currency-field="adminMode"]');
    if (codeSelect && token) {
        let timer = 0;
        const statusNodes = () => document.querySelectorAll('.currency-save-status');
        const setStatus = (text, ok = true) => {
            statusNodes().forEach(el => {
                el.textContent = text;
                el.style.color = ok ? 'var(--muted)' : '#ba4b43';
            });
        };

        const save = async () => {
            setStatus('Saving…');
            try {
                const body = new URLSearchParams({
                    currencyCode: codeSelect.value,
                    __RequestVerificationToken: token
                });
                if (modeSelect) body.set('adminCurrencyMode', modeSelect.value);
                const res = await fetch('/Settings/SaveCurrency', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'RequestVerificationToken': token
                    },
                    body,
                    credentials: 'same-origin'
                });
                const data = await res.json().catch(() => ({}));
                if (!res.ok || !data.ok) throw new Error(data.error || 'Could not save currency.');
                setStatus('Saved');
                setTimeout(() => setStatus(''), 1600);
            } catch (err) {
                setStatus(err.message || 'Save failed', false);
            }
        };

        const queueSave = () => {
            clearTimeout(timer);
            timer = setTimeout(save, 180);
        };

        codeSelect.addEventListener('change', queueSave);
        modeSelect?.addEventListener('change', queueSave);
    }

    const bindPrefToggle = ({ input, status, url, param }) => {
        if (!input || !token) return;
        const setStatus = (text, ok = true) => {
            if (!status) return;
            status.textContent = text;
            status.style.color = ok ? 'var(--muted)' : '#ba4b43';
        };
        input.addEventListener('change', async () => {
            setStatus('Saving…');
            try {
                const body = new URLSearchParams({
                    [param]: input.checked ? 'true' : 'false',
                    __RequestVerificationToken: token
                });
                const res = await fetch(url, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'RequestVerificationToken': token
                    },
                    body,
                    credentials: 'same-origin'
                });
                const data = await res.json().catch(() => ({}));
                if (!res.ok || !data.ok) throw new Error(data.error || 'Could not save preference.');
                setStatus(input.checked ? 'On' : 'Off');
                setTimeout(() => setStatus(''), 1400);
            } catch (err) {
                setStatus(err.message || 'Save failed', false);
            }
        });
    };

    bindPrefToggle({
        input: document.querySelector('[data-xp-field="enabled"]'),
        status: document.querySelector('.xp-save-status'),
        url: '/Settings/SaveXpPreference',
        param: 'xpProgressEnabled'
    });

    bindPrefToggle({
        input: document.querySelector('[data-futureyou-field="enabled"]'),
        status: document.querySelector('.futureyou-save-status'),
        url: '/Settings/SaveFutureYouPreference',
        param: 'futureYouMessagesEnabled'
    });
})();
