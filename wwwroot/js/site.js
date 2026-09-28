const themeToggle = document.querySelector('#theme-toggle');
const fontToggle = document.querySelector('#font-toggle');
const syncAppearanceControls = () => {
    if (themeToggle) {
        const dark = document.documentElement.dataset.theme === 'dark';
        themeToggle.setAttribute('aria-pressed', String(dark));
        themeToggle.setAttribute('aria-label', dark ? 'Use light mode' : 'Use dark mode');
        themeToggle.removeAttribute('title');
    }
    if (fontToggle) {
        const large = document.documentElement.classList.contains('large-text');
        fontToggle.setAttribute('aria-pressed', String(large));
        fontToggle.setAttribute('aria-label', large ? 'Use standard text size' : 'Use larger text');
        fontToggle.removeAttribute('title');
    }
};
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
themeToggle?.addEventListener('click', () => {
    const root = document.documentElement;
    withThemeSmooth(() => {
        root.dataset.theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
        try {
            localStorage.setItem('campus-theme', root.dataset.theme);
            localStorage.setItem('campus-theme-preference', root.dataset.theme);
        } catch {}
        syncAppearanceControls();
        window.dispatchEvent(new Event('themechange'));
    });
});
fontToggle?.addEventListener('click', () => {
    const large = document.documentElement.classList.toggle('large-text');
    try { localStorage.setItem('campus-large-text', String(large)); } catch {}
    syncAppearanceControls();
    window.dispatchEvent(new Event('resize'));
});
syncAppearanceControls();
// Ensure first paint without large-text if preference was never set
if (localStorage.getItem('campus-large-text') == null) {
    document.documentElement.classList.remove('large-text');
    syncAppearanceControls();
}

/* Brief skeleton shimmer on data panels */
(() => {
    const hosts = document.querySelectorAll('[data-skeleton-host]');
    if (!hosts.length) return;
    const reduced = document.documentElement.classList.contains('reduce-motion') || matchMedia('(prefers-reduced-motion: reduce)').matches;
    hosts.forEach(host => {
        const sk = host.querySelector('.skeleton-block');
        if (!sk || reduced) return;
        host.classList.add('is-loading');
        sk.hidden = false;
        requestAnimationFrame(() => {
            setTimeout(() => {
                host.classList.remove('is-loading');
                sk.hidden = true;
            }, 320);
        });
    });
})();
document.querySelectorAll('[data-password-target]').forEach(button => {
    button.addEventListener('click', () => {
        const input = document.getElementById(button.dataset.passwordTarget);
        const reveal = input.type === 'password';
        input.type = reveal ? 'text' : 'password';
        button.setAttribute('aria-pressed', String(reveal));
        button.setAttribute('aria-label', `${reveal ? 'Hide' : 'Show'} ${input.id === 'ConfirmPassword' ? 'confirm password' : 'password'}`);
        button.querySelector('span').textContent = reveal ? 'Hide' : 'Show';
    });
});

/* Sidebar: desktop collapse + mobile drawer */
(() => {
    const sidebar = document.querySelector('#sidebar');
    const toggle = document.querySelector('#nav-toggle');
    const collapse = document.querySelector('#sidebar-collapse');
    const scrim = document.querySelector('#nav-scrim');
    if (!sidebar) return;

    const isMobile = () => window.matchMedia('(max-width:1150px)').matches;
    const KEY = 'campus-nav-collapsed';
    const SCROLL_KEY = 'campus-sidebar-scroll';
    const NAV_SCROLL_KEY = 'campus-sidenav-scroll';
    const sideNav = sidebar.querySelector('.side-nav');

    const restoreScroll = () => {
        try {
            const y = Number(sessionStorage.getItem(SCROLL_KEY) || '0');
            if (y > 0) sidebar.scrollTop = y;
            if (sideNav) {
                const ny = Number(sessionStorage.getItem(NAV_SCROLL_KEY) || '0');
                if (ny > 0) sideNav.scrollTop = ny;
            }
        } catch {}
    };
    const persistScroll = () => {
        try {
            sessionStorage.setItem(SCROLL_KEY, String(sidebar.scrollTop || 0));
            if (sideNav) sessionStorage.setItem(NAV_SCROLL_KEY, String(sideNav.scrollTop || 0));
        } catch {}
    };
    sidebar.addEventListener('scroll', () => { persistScroll(); }, { passive: true });
    sideNav?.addEventListener('scroll', () => { persistScroll(); }, { passive: true });
    sidebar.querySelectorAll('a.nav-item, a.sidebar-bottom').forEach(link => {
        link.removeAttribute('title');
        link.addEventListener('click', () => persistScroll());
    });
    restoreScroll();
    requestAnimationFrame(restoreScroll);
    window.addEventListener('pageshow', restoreScroll);

    const syncToggle = () => {
        if (!toggle) return;
        const collapsed = document.body.classList.contains('nav-collapsed');
        const open = sidebar.classList.contains('open');
        const showing = isMobile() ? open : !collapsed;
        toggle.setAttribute('aria-expanded', String(showing));
        toggle.setAttribute('aria-label', showing ? 'Hide navigation' : 'Show navigation');
    };

    const setCollapsed = (collapsed) => {
        document.body.classList.toggle('nav-collapsed', collapsed);
        document.documentElement.classList.toggle('nav-collapsed-pending', collapsed);
        try { localStorage.setItem(KEY, String(collapsed)); } catch {}
        syncToggle();
    };

    const closeMobile = () => {
        sidebar.classList.remove('open');
        document.body.classList.remove('nav-open');
        syncToggle();
    };

    try {
        if (!isMobile() && localStorage.getItem(KEY) === 'true') setCollapsed(true);
        else document.documentElement.classList.remove('nav-collapsed-pending');
    } catch {}

    const onCollapseClick = (e) => {
        e.preventDefault();
        e.stopPropagation();
        if (isMobile()) closeMobile();
        else {
            setCollapsed(true);
            requestAnimationFrame(() => toggle?.focus());
        }
    };
    collapse?.addEventListener('click', onCollapseClick);

    toggle?.addEventListener('click', (e) => {
        e.preventDefault();
        if (isMobile()) {
            const open = sidebar.classList.toggle('open');
            document.body.classList.toggle('nav-open', open);
            syncToggle();
            return;
        }
        setCollapsed(false);
    });

    scrim?.addEventListener('click', closeMobile);
    document.addEventListener('keydown', e => {
        if (e.key !== 'Escape') return;
        if (isMobile()) closeMobile();
        else if (!document.body.classList.contains('nav-collapsed')) setCollapsed(true);
    });
    window.addEventListener('resize', () => {
        if (!isMobile()) {
            sidebar.classList.remove('open');
            document.body.classList.remove('nav-open');
        }
        syncToggle();
    });
    syncToggle();
})();
(() => {
    const onScroll = () => document.body.classList.toggle('scrolled', window.scrollY > 56);
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
})();
(() => {
    const nav = document.querySelector('.public-nav');
    const indicator = nav?.querySelector('.nav-indicator');
    if (!nav || !indicator) return;
    const links = [...nav.querySelectorAll('a')];
    const moveTo = (el) => {
        if (!el) { indicator.style.opacity = '0'; return; }
        indicator.style.opacity = '1';
        indicator.style.width = `${el.offsetWidth}px`;
        indicator.style.left = `${el.offsetLeft}px`;
    };
    links.forEach(link => {
        link.addEventListener('mouseenter', () => moveTo(link));
        link.addEventListener('focus', () => moveTo(link));
    });
    nav.addEventListener('mouseleave', () => { indicator.style.opacity = '0'; });
})();
// Confirm modal — theme + text-size aware
(() => {
    let modalInstance = null;
    let activeForm = null;

    function getModal() {
        if (modalInstance) return modalInstance;
        const el = document.createElement('div');
        el.className = 'cc-confirm-overlay';
        el.hidden = true;
        el.innerHTML = `
            <div class="cc-confirm-card" role="dialog" aria-modal="true" aria-labelledby="cc-confirm-title">
                <div class="cc-confirm-head">
                    <h3 id="cc-confirm-title">Confirm</h3>
                    <button type="button" class="cc-confirm-x" data-cancel aria-label="Close">×</button>
                </div>
                <p id="cc-confirm-text"></p>
                <div class="cc-confirm-actions">
                    <button type="button" class="button secondary small" data-cancel>Cancel</button>
                    <button type="button" class="button small" data-ok>Confirm</button>
                </div>
            </div>`;
        document.body.appendChild(el);
        modalInstance = el;

        const close = () => {
            el.hidden = true;
            el.classList.remove('is-open');
        };

        el.querySelectorAll('[data-cancel]').forEach(btn => btn.addEventListener('click', () => { activeForm = null; close(); }));
        el.addEventListener('click', e => { if (e.target === el) { activeForm = null; close(); } });
        el.querySelector('[data-ok]').addEventListener('click', () => {
            if (!activeForm) { close(); return; }
            const formToSubmit = activeForm;
            activeForm = null;
            close();
            HTMLFormElement.prototype.submit.call(formToSubmit);
        });
        return el;
    }

    function triggerModal(msg, form) {
        const modal = getModal();
        activeForm = form;
        const isDelete = /delete|remove|force|permanent/i.test(msg);
        modal.querySelector('#cc-confirm-title').textContent = isDelete ? 'Please confirm' : 'Please confirm';
        modal.querySelector('#cc-confirm-text').textContent = msg;
        const ok = modal.querySelector('[data-ok]');
        ok.className = isDelete ? 'button danger small' : 'button small';
        ok.textContent = isDelete ? 'Confirm' : 'Continue';
        modal.hidden = false;
        modal.classList.add('is-open');
        ok.focus();
    }

    document.addEventListener('submit', e => {
        const form = e.target;
        if (!(form instanceof HTMLFormElement)) return;
        if (form.matches('[data-admin-ajax]')) return;

        const confirmMsg = form.getAttribute('data-confirm');
        const inlineOnsubmit = form.getAttribute('onsubmit');

        if (confirmMsg) {
            e.preventDefault();
            e.stopPropagation();
            triggerModal(confirmMsg, form);
            return false;
        }

        if (inlineOnsubmit && inlineOnsubmit.includes('confirm(')) {
            e.preventDefault();
            e.stopPropagation();
            const match = inlineOnsubmit.match(/confirm\(['"]([^'"]+)['"]\)/);
            const msg = match ? match[1] : 'Are you sure you want to proceed?';
            triggerModal(msg, form);
            return false;
        }
    }, true);

    document.addEventListener('click', e => {
        const btn = e.target.closest('button, a');
        if (!btn) return;
        const inlineOnclick = btn.getAttribute('onclick');
        if (inlineOnclick && inlineOnclick.includes('confirm(')) {
            e.preventDefault();
            e.stopPropagation();
            const form = btn.closest('form');
            const match = inlineOnclick.match(/confirm\(['"]([^'"]+)['"]\)/);
            const msg = match ? match[1] : 'Are you sure you want to proceed?';
            if (form) triggerModal(msg, form);
        }
    }, true);
})();

// DateOnly model binding expects a full ISO date; month controls submit YYYY-MM.
document.querySelectorAll('form').forEach(form=>form.addEventListener('submit',()=>{form.querySelectorAll('input[type="month"]').forEach(input=>{if(input.value){const hidden=document.createElement('input');hidden.type='hidden';hidden.name=input.name;hidden.value=input.value+'-01';input.removeAttribute('name');form.append(hidden);}});}));

// Decorative books and shapes move gently against the scroll direction.
(() => {
    const items = [...document.querySelectorAll('[data-parallax]')];
    if (!items.length) return;
    let queued = false;
    const render = () => {
        const reduced = document.documentElement.classList.contains('reduce-motion') || matchMedia('(prefers-reduced-motion: reduce)').matches;
        items.forEach(item => item.style.setProperty('--scroll-shift', `${reduced ? 0 : scrollY * Number(item.dataset.parallax || 0)}px`));
        queued = false;
    };
    addEventListener('scroll', () => { if (!queued) { queued = true; requestAnimationFrame(render); } }, { passive: true });
    render();
})();

(() => {
    const launcher = document.querySelector('#assistant-launcher');
    const dock = document.querySelector('#assistant-dock');
    const panel = document.querySelector('#assistant-panel');
    const close = document.querySelector('#assistant-close');
    const form = document.querySelector('#assistant-form');
    const input = document.querySelector('#assistant-message');
    const messages = document.querySelector('#assistant-messages');
    const intro = document.querySelector('#assistant-intro');
    const historyBtn = document.querySelector('#assistant-history-btn');
    const newBtn = document.querySelector('#assistant-new-btn');
    const historyPanel = document.querySelector('#assistant-history');
    const historyList = document.querySelector('#assistant-history-list');
    const historyEmpty = document.querySelector('#assistant-history-empty');
    const historyClear = document.querySelector('#assistant-history-clear');
    const sendBtn = document.querySelector('#assistant-send');
    const stopBtn = document.querySelector('#assistant-stop');
    const historyCount = document.querySelector('#assistant-history-count');
    if (!launcher || !panel || !form || !input || !messages) return;

    const userId = document.body.dataset.userId || 'anon';
    const workspace = document.body.dataset.workspace || 'student';
    const HISTORY_KEY = `campus-askcoin-history:${userId}:${workspace}`;
    const WELCOME = 'Hi! Ask me about your spending, budgets, groups, or how to use Campus Coin. I’ll answer from your account.';
    const THINKING = 'Thinking…';
    const STOPPED = 'You stopped this response.';
    let closeTimer = 0;
    let currentChat = { id: null, messages: [] };
    let started = false;
    let busy = false;
    let activeController = null;
    let pendingEl = null;
    let typeAbort = null;
    let typing = false;

    const iconCopy = '<svg viewBox="0 0 16 16" width="13" height="13" aria-hidden="true"><rect x="5.5" y="5.5" width="7" height="7" rx="1.4" fill="none" stroke="currentColor" stroke-width="1.5"/><path d="M3.5 10.5V3.8A1.3 1.3 0 0 1 4.8 2.5h6.7" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>';
    const iconEdit = '<svg viewBox="0 0 16 16" width="13" height="13" aria-hidden="true"><path d="M9.4 3.4l3.2 3.2M3.5 12.5l.7-3.6L10.8 2.3a1.2 1.2 0 0 1 1.7 0l1.2 1.2a1.2 1.2 0 0 1 0 1.7L7.1 11.8l-3.6.7z" fill="none" stroke="currentColor" stroke-width="1.45" stroke-linecap="round" stroke-linejoin="round"/></svg>';

    const loadHistory = () => {
        try { return JSON.parse(localStorage.getItem(HISTORY_KEY) || '[]'); }
        catch { return []; }
    };
    const updateHistoryBadge = () => {
        if (!historyCount) return;
        const n = loadHistory().length;
        historyCount.textContent = String(n);
        historyCount.hidden = n < 1;
    };
    const saveHistory = (items) => {
        try { localStorage.setItem(HISTORY_KEY, JSON.stringify(items.slice(0, 40))); } catch {}
        updateHistoryBadge();
    };
    const setBusy = (on) => {
        busy = on;
        panel.classList.toggle('is-busy', on);
        if (stopBtn) {
            stopBtn.hidden = !on;
            if (on) stopBtn.removeAttribute('hidden');
            else stopBtn.setAttribute('hidden', '');
        }
        if (sendBtn) {
            sendBtn.hidden = on;
            if (on) sendBtn.setAttribute('hidden', '');
            else sendBtn.removeAttribute('hidden');
        }
        input.disabled = on;
    };
    const setChatting = (on) => {
        started = on;
        panel.classList.toggle('is-chatting', on);
        if (intro) {
            intro.hidden = !!on;
            intro.classList.toggle('is-hidden', !!on);
            intro.setAttribute('aria-hidden', String(!!on));
            intro.style.cssText = on
                ? 'display:none!important;visibility:hidden;max-height:0;margin:0;padding:0;opacity:0;pointer-events:none'
                : '';
        }
    };
    const setOpen = (open, { reset = false } = {}) => {
        clearTimeout(closeTimer);
        launcher.setAttribute('aria-expanded', String(open));
        dock?.classList.toggle('is-near', open);
        if (open) {
            panel.hidden = false;
            requestAnimationFrame(() => {
                panel.classList.add('is-open');
                if (!busy) input.focus();
            });
            return;
        }
        panel.classList.remove('is-open');
        historyPanel && (historyPanel.hidden = true);
        historyBtn?.setAttribute('aria-expanded', 'false');
        const finish = () => {
            panel.hidden = true;
            if (reset) startNewChat(true);
        };
        if (matchMedia('(prefers-reduced-motion: reduce)').matches) {
            finish();
            return;
        }
        closeTimer = setTimeout(finish, 340);
    };
    const resizeComposer = () => {
        input.style.height = 'auto';
        input.style.height = `${Math.min(input.scrollHeight, 120)}px`;
    };
    const getBody = (el) => el?.querySelector('.assistant-message-body') || el;
    const getTurn = (el) => el?.closest('.assistant-turn') || el?.parentElement;
    const attachActions = (item, kind, text, { allowEdit = true } = {}) => {
        if (item.dataset.welcome === 'true' || item.classList.contains('pending') || item.classList.contains('typing')) return;
        const turn = getTurn(item);
        if (!turn) return;
        let actions = turn.querySelector('.assistant-message-actions');
        if (actions) actions.remove();
        actions = document.createElement('div');
        actions.className = 'assistant-message-actions';
        const copyBtn = document.createElement('button');
        copyBtn.type = 'button';
        copyBtn.className = 'assistant-action-copy';
        copyBtn.setAttribute('aria-label', 'Copy message');
        copyBtn.title = 'Copy';
        copyBtn.innerHTML = iconCopy;
        copyBtn.addEventListener('click', async e => {
            e.stopPropagation();
            const value = getBody(item).textContent || text || '';
            try {
                await navigator.clipboard.writeText(value);
                copyBtn.title = 'Copied';
                setTimeout(() => { copyBtn.title = 'Copy'; }, 1200);
            } catch {}
        });
        actions.append(copyBtn);
        if (kind === 'user' && allowEdit && !item.classList.contains('stopped')) {
            const editBtn = document.createElement('button');
            editBtn.type = 'button';
            editBtn.className = 'assistant-action-edit';
            editBtn.setAttribute('aria-label', 'Edit message');
            editBtn.title = 'Edit';
            editBtn.innerHTML = iconEdit;
            editBtn.addEventListener('click', e => {
                e.stopPropagation();
                if (busy) return;
                const value = getBody(item).textContent || text || '';
                input.value = value;
                resizeComposer();
                input.focus();
                const end = input.value.length;
                input.setSelectionRange(end, end);
            });
            actions.append(editBtn);
        }
        turn.append(actions);
    };
    const addMessage = (text, kind, opts = {}) => {
        const turn = document.createElement('div');
        turn.className = `assistant-turn assistant-turn-${kind}`;
        if (opts.welcome) turn.dataset.welcome = 'true';
        const item = document.createElement('div');
        item.className = `assistant-message assistant-message-${kind}`;
        if (opts.welcome) item.dataset.welcome = 'true';
        if (opts.pending) item.classList.add('pending');
        if (opts.stopped) item.classList.add('stopped');
        const body = document.createElement('div');
        body.className = 'assistant-message-body';
        if (opts.pending) {
            body.innerHTML = `<span class="assistant-thinking-label"></span><span class="assistant-thinking-dots" aria-hidden="true"><i></i><i></i><i></i></span>`;
            body.querySelector('.assistant-thinking-label').textContent = text;
        } else {
            body.textContent = text;
        }
        item.append(body);
        turn.append(item);
        if (!opts.pending && !opts.welcome) attachActions(item, kind, text, { allowEdit: !opts.stopped });
        messages.append(turn);
        messages.scrollTop = messages.scrollHeight;
        return item;
    };
    const typeWords = (el, text, signal) => new Promise(resolve => {
        if (!text || matchMedia('(prefers-reduced-motion: reduce)').matches) {
            el.textContent = text || '';
            resolve(el.textContent);
            return;
        }
        const parts = text.match(/\S+\s*|\s+/g) || [text];
        el.textContent = '';
        let i = 0;
        const step = () => {
            if (signal?.aborted) {
                resolve(el.textContent);
                return;
            }
            if (i >= parts.length) {
                resolve(el.textContent);
                return;
            }
            el.textContent += parts[i++];
            messages.scrollTop = messages.scrollHeight;
            const delay = parts[i - 1].trim() ? 34 : 10;
            setTimeout(step, delay);
        };
        step();
    });
    const finishPending = async (el, text, { stopped = false, animate = true } = {}) => {
        if (!el) return text;
        el.classList.remove('pending');
        if (stopped) el.classList.add('stopped');
        const body = getBody(el);
        if (animate && !stopped) {
            el.classList.add('typing');
            typing = true;
            typeAbort = new AbortController();
            const shown = await typeWords(body, text, typeAbort.signal);
            el.classList.remove('typing');
            typing = false;
            typeAbort = null;
            if (!el.classList.contains('stopped')) attachActions(el, 'bot', text, { allowEdit: false });
            messages.scrollTop = messages.scrollHeight;
            return shown;
        }
        body.textContent = text;
        attachActions(el, 'bot', text, { allowEdit: false });
        messages.scrollTop = messages.scrollHeight;
        return text;
    };
    const persistCurrent = () => {
        const turns = currentChat.messages.filter(m =>
            (m.role === 'user' || m.role === 'bot') && m.text && m.text !== WELCOME && m.text !== THINKING
        );
        if (!turns.some(m => m.role === 'user') || turns.length < 2) return;
        const title = (turns.find(m => m.role === 'user')?.text || 'Ask Coin chat').trim().slice(0, 72);
        const items = loadHistory().filter(x => x.id !== currentChat.id);
        items.unshift({
            id: currentChat.id || `chat-${Date.now()}`,
            title,
            updatedAt: Date.now(),
            messages: turns
        });
        currentChat.id = items[0].id;
        saveHistory(items);
        renderHistory();
    };
    const renderHistory = () => {
        if (!historyList || !historyEmpty) return;
        const items = loadHistory();
        historyList.innerHTML = '';
        historyEmpty.hidden = items.length > 0;
        updateHistoryBadge();
        items.forEach(item => {
            const row = document.createElement('div');
            row.className = 'assistant-history-item';
            row.innerHTML = `<div><strong></strong><small></small></div><button type="button" class="history-delete" aria-label="Delete chat">×</button>`;
            row.querySelector('strong').textContent = item.title || 'Ask Coin chat';
            row.querySelector('small').textContent = new Date(item.updatedAt).toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
            row.addEventListener('click', e => {
                if (e.target.closest('.history-delete')) return;
                openChat(item);
            });
            row.querySelector('.history-delete').addEventListener('click', e => {
                e.stopPropagation();
                saveHistory(loadHistory().filter(x => x.id !== item.id));
                if (currentChat.id === item.id) startNewChat(false);
                renderHistory();
            });
            historyList.append(row);
        });
    };
    const openChat = (item) => {
        if (busy) stopResponse(false);
        persistCurrent();
        currentChat = { id: item.id, messages: [...(item.messages || [])] };
        messages.innerHTML = '';
        currentChat.messages.forEach(m => {
            const kind = m.role === 'user' ? 'user' : 'bot';
            addMessage(m.text, kind, { stopped: !!m.stopped });
        });
        setChatting(currentChat.messages.some(m => m.role === 'user'));
        if (historyPanel) historyPanel.hidden = true;
        historyBtn?.setAttribute('aria-expanded', 'false');
        setOpen(true);
    };
    const startNewChat = (save = true) => {
        if (busy) stopResponse(false);
        if (save) persistCurrent();
        currentChat = { id: `chat-${Date.now()}`, messages: [] };
        messages.innerHTML = '';
        addMessage(WELCOME, 'bot', { welcome: true });
        setChatting(false);
        if (historyPanel) historyPanel.hidden = true;
        historyBtn?.setAttribute('aria-expanded', 'false');
        input.value = '';
        resizeComposer();
        if (panel.classList.contains('is-open') && !panel.hidden) input.focus();
    };
    const stopResponse = (announce = true) => {
        if (typeAbort) {
            try { typeAbort.abort(); } catch {}
            typeAbort = null;
        }
        if (activeController) {
            try { activeController.abort(); } catch {}
            activeController = null;
        }
        if (announce && pendingEl) {
            const body = getBody(pendingEl);
            const partial = (body?.textContent || '').trim();
            const stoppedText = pendingEl.classList.contains('typing') && partial && partial !== THINKING
                ? `${partial}\n\n${STOPPED}`
                : STOPPED;
            pendingEl.classList.remove('pending', 'typing');
            pendingEl.classList.add('stopped');
            if (body) body.textContent = stoppedText;
            attachActions(pendingEl, 'bot', stoppedText, { allowEdit: false });
            currentChat.messages.push({ role: 'bot', text: stoppedText, stopped: true });
            persistCurrent();
        } else if (pendingEl) {
            getTurn(pendingEl)?.remove() || pendingEl.remove();
        }
        typing = false;
        pendingEl = null;
        setBusy(false);
    };
    const ask = async (message) => {
        if (!message || busy) return;
        if (!started) setChatting(true);
        if (!currentChat.id) currentChat.id = `chat-${Date.now()}`;
        currentChat.messages.push({ role: 'user', text: message });
        addMessage(message, 'user');
        input.value = '';
        resizeComposer();
        pendingEl = addMessage(THINKING, 'bot', { pending: true });
        setBusy(true);
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        activeController = new AbortController();
        try {
            const response = await fetch('/Assistant/Ask', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': token ?? '' },
                body: new URLSearchParams({ Message: message, __RequestVerificationToken: token ?? '' }),
                signal: activeController.signal
            });
            const data = await response.json();
            if (!response.ok) throw new Error(data.error || 'The assistant could not answer right now.');
            const answer = data.answer || '';
            await finishPending(pendingEl, answer, { animate: true });
            if (pendingEl && !pendingEl.classList.contains('stopped')) {
                currentChat.messages.push({ role: 'bot', text: answer });
            }
        } catch (error) {
            if (error.name === 'AbortError') return;
            const err = error.message || 'The assistant could not answer right now.';
            await finishPending(pendingEl, err, { animate: false });
            currentChat.messages.push({ role: 'bot', text: err });
        } finally {
            if (pendingEl && !typing) {
                pendingEl = null;
                activeController = null;
                setBusy(false);
                persistCurrent();
            } else if (!typing) {
                activeController = null;
                setBusy(false);
            }
        }
    };

    launcher.addEventListener('click', () => {
        const willOpen = panel.hidden || !panel.classList.contains('is-open');
        setOpen(willOpen, { reset: !willOpen });
    });
    document.querySelectorAll('[data-open-assistant]').forEach(button => button.addEventListener('click', () => setOpen(true)));
    close?.addEventListener('click', () => setOpen(false, { reset: true }));
    newBtn?.addEventListener('click', () => startNewChat(true));
    stopBtn?.addEventListener('click', () => stopResponse(true));
    historyBtn?.addEventListener('click', () => {
        if (!historyPanel) return;
        const open = historyPanel.hidden;
        historyPanel.hidden = !open;
        historyBtn.setAttribute('aria-expanded', String(open));
        if (open) renderHistory();
    });
    historyClear?.addEventListener('click', () => {
        if (!loadHistory().length) return;
        if (!confirm('Delete all Ask Coin chat history on this device?')) return;
        saveHistory([]);
        renderHistory();
    });
    document.querySelectorAll('[data-assistant-suggestion]').forEach(button => button.addEventListener('click', () => {
        setOpen(true);
        const value = button.dataset.assistantSuggestion || '';
        if (value) ask(value);
    }));

    input.addEventListener('input', resizeComposer);
    input.addEventListener('keydown', e => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            if (!busy) form.requestSubmit();
        }
    });

    if (dock && !matchMedia('(prefers-reduced-motion: reduce)').matches) {
        let nearTimer = 0;
        const updateNear = (clientX, clientY) => {
            const w = window.innerWidth;
            const h = window.innerHeight;
            const near = clientX > w - 110 && clientY > h - 130;
            dock.classList.toggle('is-near', near || launcher.getAttribute('aria-expanded') === 'true');
        };
        window.addEventListener('pointermove', e => {
            clearTimeout(nearTimer);
            nearTimer = setTimeout(() => updateNear(e.clientX, e.clientY), 16);
        }, { passive: true });
    }

    form.addEventListener('submit', event => {
        event.preventDefault();
        const message = input.value.trim();
        if (!message) return;
        ask(message);
    });

    renderHistory();
    if (!currentChat.id) currentChat.id = `chat-${Date.now()}`;
})();

document.querySelectorAll('a[href*="#"]').forEach(anchor => {
    anchor.addEventListener('click', e => {
        const url = new URL(anchor.href, location.href);
        if (url.pathname === location.pathname && url.hash) {
            const target = document.querySelector(url.hash);
            if (target) {
                e.preventDefault();
                const offset = 76;
                const bodyRect = document.body.getBoundingClientRect().top;
                const elementRect = target.getBoundingClientRect().top;
                const elementPosition = elementRect - bodyRect;
                const offsetPosition = elementPosition - offset;
                window.scrollTo({ top: Math.max(0, offsetPosition), behavior: 'smooth' });
                history.pushState(null, '', url.hash);
            }
        }
    });
});

// Global Orbit Custom Popout Dropdown List System
const initCustomSelects = () => {
    document.querySelectorAll('select').forEach(select => {
        if (select.dataset.customized === 'true' || select.hasAttribute('data-native') || select.style.display === 'none') return;
        select.dataset.customized = 'true';
        select.style.setProperty('display', 'none', 'important');

        const wrapper = document.createElement('div');
        wrapper.className = 'custom-select-wrapper';

        const trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.className = 'custom-select-trigger';
        trigger.setAttribute('aria-expanded', 'false');
        trigger.setAttribute('aria-haspopup', 'listbox');

        const selectedOpt = select.options[select.selectedIndex] || select.options[0];
        const labelSpan = document.createElement('span');
        labelSpan.className = 'custom-select-label';
        labelSpan.textContent = selectedOpt ? selectedOpt.text : 'Select...';

        const arrowSpan = document.createElement('span');
        arrowSpan.className = 'custom-select-arrow';
        arrowSpan.setAttribute('aria-hidden', 'true');
        arrowSpan.textContent = '';
        trigger.append(labelSpan, arrowSpan);

        const dropdown = document.createElement('div');
        dropdown.className = 'custom-select-dropdown';
        dropdown.hidden = true;
        dropdown.setAttribute('role', 'listbox');

        const optionsList = document.createElement('div');
        optionsList.className = 'custom-select-options';
        dropdown.append(optionsList);
        wrapper.append(trigger);
        select.parentNode.insertBefore(wrapper, select);

        const buildOptions = () => {
            optionsList.innerHTML = '';
            Array.from(select.options).forEach(opt => {
                const item = document.createElement('button');
                item.type = 'button';
                item.className = `custom-select-option ${opt.selected ? 'is-selected' : ''}`;
                item.dataset.value = opt.value;
                item.setAttribute('role', 'option');
                if (opt.selected) item.setAttribute('aria-selected', 'true');

                const check = document.createElement('span');
                check.className = 'option-check';
                check.textContent = '✓';
                const text = document.createElement('span');
                text.className = 'option-text';
                text.textContent = opt.text;
                item.append(check, text);

                item.addEventListener('click', e => {
                    e.preventDefault();
                    e.stopPropagation();
                    select.value = opt.value;
                    Array.from(select.options).forEach(o => { o.selected = o.value === opt.value; });
                    labelSpan.textContent = opt.text;
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                    closeDropdown();
                });
                optionsList.append(item);
            });
        };

        const placeDropdown = () => {
            const rect = trigger.getBoundingClientRect();
            const width = Math.max(rect.width, 180);
            const left = Math.max(8, Math.min(rect.left, window.innerWidth - width - 8));
            const spaceBelow = window.innerHeight - rect.bottom;
            const openUp = spaceBelow < 240 && rect.top > 240;
            dropdown.style.setProperty('position', 'fixed', 'important');
            dropdown.style.setProperty('left', `${left}px`, 'important');
            dropdown.style.setProperty('width', `${width}px`, 'important');
            dropdown.style.setProperty('right', 'auto', 'important');
            dropdown.style.setProperty('z-index', '2147483000', 'important');
            dropdown.style.setProperty('display', 'block', 'important');
            if (openUp) {
                dropdown.style.setProperty('top', 'auto', 'important');
                dropdown.style.setProperty('bottom', `${window.innerHeight - rect.top + 8}px`, 'important');
            } else {
                dropdown.style.setProperty('top', `${rect.bottom + 8}px`, 'important');
                dropdown.style.setProperty('bottom', 'auto', 'important');
            }
        };

        const closeDropdown = () => {
            wrapper.classList.remove('is-open');
            trigger.setAttribute('aria-expanded', 'false');
            dropdown.hidden = true;
            dropdown.removeAttribute('data-open');
            ['position','top','left','right','bottom','width','display','z-index'].forEach(prop => {
                dropdown.style.removeProperty(prop);
            });
            if (dropdown.parentElement === document.body) {
                wrapper.appendChild(dropdown);
            }
        };

        const openDropdown = () => {
            document.querySelectorAll('.custom-select-wrapper.is-open').forEach(w => {
                if (w !== wrapper) w._closeCustomSelect?.();
            });
            document.querySelectorAll('.custom-select-dropdown[data-open="true"]').forEach(d => {
                if (d === dropdown) return;
                d.hidden = true;
                d.removeAttribute('data-open');
                d.style.cssText = '';
                const home = document.querySelector(`.custom-select-wrapper[data-select-id="${d.dataset.owner}"]`);
                if (home && d.parentElement === document.body) home.appendChild(d);
                home?.classList.remove('is-open');
                home?.querySelector('.custom-select-trigger')?.setAttribute('aria-expanded', 'false');
            });

            buildOptions();
            wrapper.classList.add('is-open');
            trigger.setAttribute('aria-expanded', 'true');
            dropdown.dataset.owner = wrapper.dataset.selectId;
            dropdown.dataset.open = 'true';
            dropdown.hidden = false;
            document.body.appendChild(dropdown);
            placeDropdown();
        };

        const id = `cs-${Math.random().toString(36).slice(2, 9)}`;
        wrapper.dataset.selectId = id;
        dropdown.dataset.owner = id;

        trigger.addEventListener('click', e => {
            e.preventDefault();
            e.stopPropagation();
            if (wrapper.classList.contains('is-open')) closeDropdown();
            else openDropdown();
        });

        select.addEventListener('change', () => {
            const current = select.options[select.selectedIndex];
            if (current) labelSpan.textContent = current.text;
            buildOptions();
        });

        wrapper._closeCustomSelect = closeDropdown;
        wrapper._placeCustomSelect = placeDropdown;
    });
};

// Global Custom Orbit Date Picker Overlay System
let activeCalendarOverlay = null;
function showOrbitCalendar(targetInput) {
    if (activeCalendarOverlay) activeCalendarOverlay.remove();

    const overlay = document.createElement('div');
    overlay.className = 'custom-calendar-popout';
    overlay.style.cssText = `
        position: absolute;
        z-index: 9999999;
        background: var(--panel);
        border: 1px solid var(--line);
        border-radius: 16px;
        padding: 16px;
        box-shadow: 0 18px 45px rgba(8,17,31,0.18), 0 0 25px rgba(121,242,199,0.2);
        backdrop-filter: blur(16px);
        min-width: 280px;
        animation: popout-list-rise 0.22s cubic-bezier(0.2, 0.8, 0.2, 1);
    `;

    const rect = targetInput.getBoundingClientRect();
    overlay.style.top = `${window.scrollY + rect.bottom + 6}px`;
    overlay.style.left = `${window.scrollX + rect.left}px`;

    let displayDate = new Date();
    if (targetInput.value) {
        const parsed = new Date(targetInput.value);
        if (!isNaN(parsed.getTime())) displayDate = parsed;
    }

    let year = displayDate.getFullYear();
    let month = displayDate.getMonth();

    const render = () => {
        const monthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
        const firstDay = new Date(year, month, 1).getDay();
        const daysInMonth = new Date(year, month + 1, 0).getDate();

        let gridHtml = '';
        for (let i = 0; i < firstDay; i++) {
            gridHtml += `<div></div>`;
        }

        const selectedVal = targetInput.value;

        for (let d = 1; d <= daysInMonth; d++) {
            const dateStr = `${year}-${String(month + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
            const isSelected = selectedVal === dateStr;
            gridHtml += `
                <button type="button" class="cal-day ${isSelected ? 'is-selected' : ''}" data-date="${dateStr}" style="
                    width: 32px; height: 32px; border-radius: 8px; border: none; font-size: 12px; font-weight: 650; cursor: pointer;
                    background: ${isSelected ? 'var(--accent)' : 'transparent'};
                    color: ${isSelected ? '#fff' : 'var(--ink)'};
                    transition: all 0.15s ease;
                ">${d}</button>
            `;
        }

        overlay.innerHTML = `
            <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:12px;">
                <button type="button" id="cal-prev" style="background:none; border:none; color:var(--ink); font-size:16px; cursor:pointer;">‹</button>
                <strong style="font-size:13px; color:var(--ink);">${monthNames[month]} ${year}</strong>
                <button type="button" id="cal-next" style="background:none; border:none; color:var(--ink); font-size:16px; cursor:pointer;">›</button>
            </div>
            <div style="display:grid; grid-template-columns:repeat(7, 1fr); text-align:center; font-size:10px; font-weight:700; color:var(--muted); margin-bottom:8px;">
                <span>Su</span><span>Mo</span><span>Tu</span><span>We</span><span>Th</span><span>Fr</span><span>Sa</span>
            </div>
            <div style="display:grid; grid-template-columns:repeat(7, 1fr); gap:4px; place-items:center;">
                ${gridHtml}
            </div>
            <div style="display:flex; justify-content:space-between; margin-top:12px; padding-top:8px; border-top:1px solid var(--line);">
                <button type="button" id="cal-clear" style="background:none; border:none; color:var(--muted); font-size:11px; cursor:pointer;">Clear</button>
                <button type="button" id="cal-today" style="background:none; border:none; color:var(--accent); font-weight:700; font-size:11px; cursor:pointer;">Today</button>
            </div>
        `;

        overlay.querySelector('#cal-prev').addEventListener('click', e => { e.stopPropagation(); month--; if (month < 0) { month = 11; year--; } render(); });
        overlay.querySelector('#cal-next').addEventListener('click', e => { e.stopPropagation(); month++; if (month > 11) { month = 0; year++; } render(); });
        overlay.querySelector('#cal-today').addEventListener('click', e => {
            e.stopPropagation();
            const today = new Date();
            const dateStr = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
            targetInput.value = dateStr;
            targetInput.dispatchEvent(new Event('change', { bubbles: true }));
            overlay.remove();
            activeCalendarOverlay = null;
        });
        overlay.querySelector('#cal-clear').addEventListener('click', e => {
            e.stopPropagation();
            targetInput.value = '';
            targetInput.dispatchEvent(new Event('change', { bubbles: true }));
            overlay.remove();
            activeCalendarOverlay = null;
        });

        overlay.querySelectorAll('.cal-day').forEach(btn => {
            btn.addEventListener('click', e => {
                e.stopPropagation();
                targetInput.value = btn.dataset.date;
                targetInput.dispatchEvent(new Event('change', { bubbles: true }));
                overlay.remove();
                activeCalendarOverlay = null;
            });
            btn.addEventListener('mouseenter', () => { if (!btn.classList.contains('is-selected')) btn.style.background = 'rgba(121,242,199,0.18)'; });
            btn.addEventListener('mouseleave', () => { if (!btn.classList.contains('is-selected')) btn.style.background = 'transparent'; });
        });
    };

    render();
    document.body.appendChild(overlay);
    activeCalendarOverlay = overlay;

    const closeHandler = e => {
        if (!overlay.contains(e.target) && e.target !== targetInput) {
            overlay.remove();
            activeCalendarOverlay = null;
            document.removeEventListener('click', closeHandler);
        }
    };
    setTimeout(() => document.addEventListener('click', closeHandler), 10);
}

const initCustomCalendars = () => {
    document.querySelectorAll('input[type="date"]').forEach(input => {
        if (input.dataset.calendarized === 'true') return;
        input.dataset.calendarized = 'true';
        input.addEventListener('click', e => {
            e.preventDefault();
            e.stopPropagation();
            showOrbitCalendar(input);
        });
    });
};

const initUiComponents = () => {
    initCustomSelects();
    initCustomCalendars();
};

document.addEventListener('DOMContentLoaded', initUiComponents);
if (document.readyState === 'interactive' || document.readyState === 'complete') initUiComponents();

(() => {
    const isFlashNotice = (el) => {
        if (!el || el.dataset.dismissArmed === 'true') return false;
        if (el.hasAttribute('data-flash-notice') || el.classList.contains('notice-toast')) return true;
        // Match TempData flash banners even if an old layout is still cached.
        if (!el.classList.contains('notice')) return false;
        if (!(el.classList.contains('success') || el.classList.contains('error'))) return false;
        const role = el.getAttribute('role');
        return role === 'status' || role === 'alert';
    };

    const dismissFlashNotices = () => {
        document.querySelectorAll('.notice.success, .notice.error, [data-flash-notice], .notice-toast').forEach(el => {
            if (!isFlashNotice(el)) return;
            // Skip tiny inline status chips that are not flash toasts.
            if (el.closest('td, .pill, .badge') && !el.classList.contains('notice-toast') && !el.hasAttribute('data-flash-notice')) {
                const text = (el.textContent || '').trim().toLowerCase();
                if (text === 'disabled' || text === 'active') return;
            }
            el.dataset.dismissArmed = 'true';
            el.classList.add('notice-toast');
            setTimeout(() => {
                el.classList.add('is-hiding');
                setTimeout(() => el.remove(), 300);
            }, 2500);
        });
    };

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', dismissFlashNotices);
    else dismissFlashNotices();
    // Also catch late-rendered notices after redirects.
    window.addEventListener('load', dismissFlashNotices);
})();

window.addEventListener('scroll', () => {
    document.querySelectorAll('.custom-select-wrapper.is-open').forEach(w => w._placeCustomSelect?.());
}, true);

window.addEventListener('resize', () => {
    document.querySelectorAll('.custom-select-wrapper.is-open').forEach(w => w._placeCustomSelect?.());
});

document.addEventListener('click', e => {
    if (e.target.closest('.custom-select-wrapper') || e.target.closest('.custom-select-dropdown')) return;
    document.querySelectorAll('.custom-select-wrapper.is-open').forEach(w => w._closeCustomSelect?.());
});


