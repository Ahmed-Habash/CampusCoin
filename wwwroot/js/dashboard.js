(() => {
const data = JSON.parse(document.getElementById('dashboard-data').textContent);
const palette = ['#2563eb', '#60a5fa', '#38bdf8', '#818cf8', '#34d399', '#f59e0b', '#f43f5e'];
Chart.defaults.font.family = "'Plus Jakarta Sans', system-ui, sans-serif";
Chart.defaults.font.size = 11;
Chart.defaults.color = '#64748b';
const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
const money = (n) => {
    try { return new Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD' }).format(n); }
    catch { return '$' + Number(n).toFixed(2); }
};

const trendEl = document.getElementById('trend-chart');
const trend = trendEl ? new Chart(trendEl, {
    type: 'bar',
    data: {
        labels: data.labels,
        datasets: [
            { label: 'Income', data: data.income, backgroundColor: '#0f172a', borderRadius: 8, maxBarThickness: 22 },
            { label: 'Expenses', data: data.expenses, backgroundColor: '#60a5fa', borderRadius: 8, maxBarThickness: 22 }
        ]
    },
    options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: reduced ? false : { duration: 750, easing: 'easeOutQuart' },
        plugins: {
            legend: { position: 'bottom', align: 'start', labels: { usePointStyle: true, pointStyle: 'circle', boxWidth: 6, padding: 18 } },
            tooltip: { callbacks: { label: (c) => `${c.dataset.label}: ${money(c.parsed.y)}` } }
        },
        scales: {
            x: { grid: { display: false }, border: { display: false } },
            y: { beginAtZero: true, border: { display: false }, grid: { color: 'rgba(15,23,42,.08)' }, ticks: { callback: (v) => money(v) } }
        }
    }
}) : null;

const donutOpts = (cutout) => ({
    responsive: true,
    maintainAspectRatio: false,
    cutout,
    animation: reduced ? false : { animateRotate: true, duration: 700 },
    interaction: { mode: 'nearest', intersect: true },
    plugins: {
        legend: { display: false },
        // Canvas tooltips sit on top of the center total — use the legend instead.
        tooltip: { enabled: false }
    }
});

const categoryEl = document.getElementById('category-chart');
const donut = (data.amounts?.length && categoryEl) ? new Chart(categoryEl, {
    type: 'doughnut',
    data: {
        labels: data.categories,
        datasets: [{
            data: data.amounts,
            backgroundColor: palette,
            borderWidth: 3,
            borderColor: getComputedStyle(document.documentElement).getPropertyValue('--panel').trim() || '#fff',
            hoverOffset: 0,
            borderRadius: 4
        }]
    },
    options: donutOpts('74%')
}) : null;

const heroEl = document.getElementById('hero-category-chart');
const heroDonut = (data.amounts?.length && heroEl) ? new Chart(heroEl, {
    type: 'doughnut',
    data: {
        labels: data.categories,
        datasets: [{
            data: data.amounts,
            backgroundColor: palette,
            borderWidth: 2,
            borderColor: 'rgba(8,15,30,.85)',
            hoverOffset: 2,
            borderRadius: 3
        }]
    },
    options: {
        ...donutOpts('68%'),
        plugins: {
            legend: { display: false },
            tooltip: { enabled: false }
        }
    }
}) : null;

function applyTheme() {
    const dark = document.documentElement.dataset.theme === 'dark';
    const ink = dark ? '#94a3b8' : '#64748b';
    if (trend) {
        trend.data.datasets[0].backgroundColor = dark ? '#e2e8f0' : '#0f172a';
        trend.data.datasets[1].backgroundColor = '#60a5fa';
        trend.options.scales.x.ticks.color = ink;
        trend.options.scales.y.ticks.color = ink;
        trend.options.plugins.legend.labels.color = ink;
        trend.options.scales.y.grid.color = dark ? 'rgba(232,238,248,.08)' : 'rgba(15,23,42,.08)';
        trend.update('none');
    }
    if (donut) {
        donut.data.datasets[0].borderColor = getComputedStyle(document.documentElement).getPropertyValue('--panel').trim() || '#fff';
        donut.update('none');
    }
}
applyTheme();
window.addEventListener('themechange', applyTheme);
})();
