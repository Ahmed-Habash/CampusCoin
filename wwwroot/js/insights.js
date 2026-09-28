(() => {
    const dataEl = document.querySelector('#pulse-data');
    const canvas = document.querySelector('#pulse-chart');
    if (!dataEl || !canvas || !window.Chart) return;

    let payload = {};
    try { payload = JSON.parse(dataEl.textContent || '{}'); } catch { return; }
    if (!payload.hasData) return;

    const labels = payload.labels || [];
    const values = (payload.values || []).map(Number);
    const currency = payload.currency || 'USD';
    const money = new Intl.NumberFormat(undefined, {
        style: 'currency',
        currency,
        maximumFractionDigits: 0
    });

    const root = getComputedStyle(document.documentElement);
    const accent = (root.getPropertyValue('--accent') || '#2563eb').trim() || '#2563eb';
    const muted = (root.getPropertyValue('--muted') || '#64748b').trim() || '#64748b';
    const line = (root.getPropertyValue('--line') || 'rgba(15,23,42,.1)').trim();
    const font = '"Plus Jakarta Sans", system-ui, sans-serif';
    Chart.defaults.font.family = font;

    const fill = accent.startsWith('#')
        ? `${accent}22`
        : 'rgba(37,99,235,.12)';

    new Chart(canvas, {
        type: 'line',
        data: {
            labels,
            datasets: [{
                data: values,
                borderColor: accent,
                backgroundColor: fill,
                fill: true,
                tension: 0.35,
                pointRadius: 3,
                pointHoverRadius: 5,
                pointBackgroundColor: accent,
                pointBorderWidth: 0,
                borderWidth: 2.5
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: { mode: 'index', intersect: false },
            plugins: {
                legend: { display: false },
                tooltip: {
                    callbacks: {
                        label: (ctx) => money.format(Number(ctx.raw || 0))
                    }
                }
            },
            scales: {
                x: {
                    grid: { display: false },
                    ticks: {
                        color: muted,
                        font: { size: 10, family: font },
                        maxRotation: 0,
                        autoSkip: true,
                        maxTicksLimit: 7
                    },
                    border: { display: false }
                },
                y: {
                    beginAtZero: true,
                    grid: { color: line },
                    ticks: {
                        color: muted,
                        font: { size: 10, family: font },
                        callback: (value) => money.format(Number(value))
                    },
                    border: { display: false }
                }
            }
        }
    });
})();

(() => {
    const lens = document.querySelector('.what-if-card') || document.querySelector('.what-if-lens');
    const input = document.querySelector('#what-if-amount');
    if (!lens || !input) return;
    const original = Number(lens.dataset.forecastBalance || 0);
    const goalRemaining = Number(lens.dataset.goalRemaining || 0);
    const balance = document.querySelector('#what-if-balance');
    const message = document.querySelector('#what-if-message');
    const marker = document.querySelector('#what-if-marker');
    const currency = lens.dataset.currency || 'USD';
    const money = new Intl.NumberFormat(undefined, { style: 'currency', currency });
    const render = () => {
        const purchase = Math.max(0, Number(input.value || 0));
        const after = original - purchase;
        if (balance) balance.textContent = money.format(after);
        const range = Math.max(Math.abs(original), purchase, 1);
        if (marker) marker.style.left = `${Math.max(4, Math.min(96, 50 + (after / range) * 42))}%`;
        lens.dataset.state = after < 0 ? 'careful' : purchase > original * .5 && original > 0 ? 'pause' : 'clear';
        if (message) {
            message.textContent = after < 0
                ? `This would put the month about ${money.format(Math.abs(after))} below zero.`
                : goalRemaining > 0 && purchase > 0
                    ? `You would still have ${money.format(after)}; nearest goal needs ${money.format(goalRemaining)}.`
                    : purchase > 0
                        ? `Estimated ${money.format(after)} left for the month.`
                        : 'Your current forecast is ready.';
        }
    };
    input.addEventListener('input', render);
    document.querySelectorAll('[data-what-if]').forEach(button => button.addEventListener('click', () => {
        input.value = button.dataset.whatIf;
        render();
    }));
    render();
})();
