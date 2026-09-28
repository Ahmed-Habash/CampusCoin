try {
    const preference = localStorage.getItem('campus-theme-preference') || localStorage.getItem('campus-theme') || 'system';
    document.documentElement.dataset.theme = preference === 'system' ? (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light') : preference;
    // First visit = normal (smaller) text. Only enlarge when user has explicitly opted in.
    const largePref = localStorage.getItem('campus-large-text');
    document.documentElement.classList.toggle('large-text', largePref === 'true');
    document.documentElement.classList.toggle('reduce-motion', localStorage.getItem('campus-motion') === 'reduced');
    if (localStorage.getItem('campus-nav-collapsed') === 'true' && !matchMedia('(max-width:1150px)').matches) {
        document.documentElement.classList.add('nav-collapsed-pending');
    }
} catch {}

