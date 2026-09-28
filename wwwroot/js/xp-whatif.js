(() => {
    // Level-up toast from Overview card
    const levelUp = document.getElementById('xp-levelup');
    if (levelUp && !levelUp.hidden) {
        const dismiss = () => { levelUp.hidden = true; };
        levelUp.querySelector('[data-close-levelup]')?.addEventListener('click', dismiss);
        levelUp.addEventListener('click', (e) => { if (e.target === levelUp) dismiss(); });
        window.setTimeout(dismiss, 4200);
    }

    // What If? modal
    const modal = document.getElementById('whatif-modal');
    const form = document.getElementById('whatif-form');
    const result = document.getElementById('whatif-result');
    if (!modal || !form || !result) return;

    const scenarioEl = document.getElementById('whatif-scenario');
    const categoryWrap = document.getElementById('whatif-category-wrap');
    const daysWrap = document.getElementById('whatif-days-wrap');
    const percentWrap = document.getElementById('whatif-percent-wrap');
    const amountWrap = document.getElementById('whatif-amount-wrap');
    const submitBtn = document.getElementById('whatif-submit');

    const syncFields = () => {
        const s = scenarioEl?.value || 'extra_income';
        if (categoryWrap) categoryWrap.hidden = s === 'extra_income';
        if (daysWrap) daysWrap.hidden = s !== 'stop_category';
        if (percentWrap) percentWrap.hidden = s !== 'reduce_category';
        if (amountWrap) amountWrap.hidden = s !== 'extra_income';
    };
    scenarioEl?.addEventListener('change', syncFields);
    syncFields();

    const open = () => {
        modal.hidden = false;
        modal.classList.add('is-open');
        modal.setAttribute('aria-hidden', 'false');
        form.hidden = false;
        result.hidden = true;
    };
    const close = () => {
        modal.classList.remove('is-open');
        modal.hidden = true;
        modal.setAttribute('aria-hidden', 'true');
    };

    document.querySelectorAll('[data-open-whatif]').forEach((btn) => btn.addEventListener('click', open));
    document.querySelectorAll('[data-close-whatif]').forEach((btn) => btn.addEventListener('click', close));
    modal.addEventListener('click', (e) => { if (e.target === modal) close(); });
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !modal.hidden) close(); });

    result.querySelector('[data-whatif-again]')?.addEventListener('click', () => {
        result.hidden = true;
        form.hidden = false;
    });

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const params = new URLSearchParams();
        params.set('scenario', scenarioEl?.value || 'extra_income');
        params.set('month', form.dataset.month || '');
        const cat = document.getElementById('whatif-category')?.value;
        if (cat) params.set('categoryId', cat);
        params.set('days', document.getElementById('whatif-days')?.value || '14');
        params.set('percent', document.getElementById('whatif-percent')?.value || '50');
        params.set('amount', document.getElementById('whatif-amount')?.value || '0');

        submitBtn.disabled = true;
        submitBtn.textContent = 'Simulating…';
        try {
            const res = await fetch(`/Dashboard/WhatIf?${params}`, { headers: { Accept: 'application/json' }, credentials: 'same-origin' });
            const data = await res.json().catch(() => ({}));
            if (!res.ok) throw new Error(data.error || 'Simulation failed.');
            const ok = data.ok ?? data.Ok;
            result.querySelector('[data-whatif-label]').textContent = data.label || data.Label || '';
            result.querySelector('[data-whatif-explain]').textContent = data.explanation || data.Explanation || '';
            result.querySelector('[data-whatif-before]').textContent = data.beforeBalanceFormatted || data.BeforeBalanceFormatted || '—';
            result.querySelector('[data-whatif-after]').textContent = data.afterBalanceFormatted || data.AfterBalanceFormatted || '—';
            result.querySelector('[data-whatif-delta]').textContent = `Impact ${data.deltaFormatted || data.DeltaFormatted || ''}`;
            const budget = data.budgetImpact || data.BudgetImpact;
            const budgetEl = result.querySelector('[data-whatif-budget]');
            if (budget) { budgetEl.hidden = false; budgetEl.textContent = budget; }
            else budgetEl.hidden = true;
            if (!ok) result.querySelector('[data-whatif-delta]').textContent = 'No change';
            form.hidden = true;
            result.hidden = false;
        } catch (err) {
            result.querySelector('[data-whatif-label]').textContent = 'Something went wrong';
            result.querySelector('[data-whatif-explain]').textContent = err.message || 'Try again.';
            result.querySelector('[data-whatif-before]').textContent = '—';
            result.querySelector('[data-whatif-after]').textContent = '—';
            result.querySelector('[data-whatif-delta]').textContent = 'Error';
            result.querySelector('[data-whatif-budget]').hidden = true;
            form.hidden = true;
            result.hidden = false;
        } finally {
            submitBtn.disabled = false;
            submitBtn.textContent = 'Run scenario';
        }
    });
})();
