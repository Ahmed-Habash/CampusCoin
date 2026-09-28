(() => {
    const modal = document.getElementById('afford-modal');
    const form = document.getElementById('afford-form');
    const result = document.getElementById('afford-result');
    if (!modal || !form || !result) return;

    const amountInput = document.getElementById('afford-amount');
    const categorySelect = document.getElementById('afford-category');
    const submitBtn = document.getElementById('afford-submit');
    const verdictEl = result.querySelector('[data-verdict]');
    const headlineEl = result.querySelector('.afford-headline');
    const detailEl = result.querySelector('.afford-detail');
    const budgetRow = result.querySelector('[data-budget-row]');
    const fact = (name) => result.querySelector(`[data-fact="${name}"]`);

    const open = () => {
        modal.hidden = false;
        modal.classList.add('is-open');
        modal.setAttribute('aria-hidden', 'false');
        document.body.style.overflow = 'hidden';
        form.hidden = false;
        result.hidden = true;
        window.setTimeout(() => amountInput?.focus(), 50);
    };

    const close = () => {
        modal.classList.remove('is-open');
        modal.hidden = true;
        modal.setAttribute('aria-hidden', 'true');
        document.body.style.overflow = '';
    };

    document.querySelectorAll('[data-open-afford]').forEach((btn) => {
        btn.addEventListener('click', (e) => {
            e.preventDefault();
            open();
        });
    });
    document.querySelectorAll('[data-close-afford]').forEach((btn) => {
        btn.addEventListener('click', close);
    });
    modal.addEventListener('click', (e) => {
        if (e.target === modal) close();
    });

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && !modal.hidden) close();
    });

    result.querySelector('[data-afford-again]')?.addEventListener('click', () => {
        result.hidden = true;
        form.hidden = false;
        amountInput?.focus();
        amountInput?.select();
    });

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const amount = Number(amountInput?.value);
        if (!Number.isFinite(amount) || amount <= 0) {
            amountInput?.setCustomValidity('Enter an amount greater than zero.');
            amountInput?.reportValidity();
            amountInput?.setCustomValidity('');
            return;
        }

        let month = form.dataset.month || '';
        if (/^\d{4}-\d{2}$/.test(month)) month = `${month}-01`;
        const categoryId = categorySelect?.value || '';
        const params = new URLSearchParams({ amount: String(amount) });
        if (month) params.set('month', month);
        if (categoryId) params.set('categoryId', categoryId);

        submitBtn.disabled = true;
        const prevLabel = submitBtn.textContent;
        submitBtn.textContent = 'Checking…';
        try {
            const res = await fetch(`/Dashboard/Afford?${params.toString()}`, {
                headers: { Accept: 'application/json' },
                credentials: 'same-origin'
            });
            const data = await res.json().catch(() => ({}));
            if (!res.ok) throw new Error(data.error || data.title || 'Could not check affordability.');
            const verdict = data.verdict || data.Verdict || 'Yes';
            verdictEl.textContent = verdict === 'Easy' ? 'Easy yes' : verdict === 'No' ? 'Not recommended' : verdict === 'Caution' ? 'Proceed carefully' : 'Yes';
            verdictEl.dataset.verdict = verdict;
            headlineEl.textContent = data.headline || data.Headline || '';
            detailEl.textContent = data.detail || data.Detail || '';
            fact('balance').textContent = data.monthBalanceFormatted || data.MonthBalanceFormatted || '—';
            fact('recurring').textContent = data.upcomingRecurringFormatted || data.UpcomingRecurringFormatted || '—';
            fact('available').textContent = data.availableAfterRecurringFormatted || data.AvailableAfterRecurringFormatted || '—';
            const hasBudget = data.hasBudget ?? data.HasBudget;
            if (hasBudget) {
                budgetRow.hidden = false;
                const pct = data.percentOfRemainingBudget ?? data.PercentOfRemainingBudget;
                const left = data.budgetRemainingFormatted || data.BudgetRemainingFormatted || '—';
                fact('budget').textContent = pct != null ? `${left} · ${pct}% of remaining` : left;
            } else {
                budgetRow.hidden = true;
            }
            form.hidden = true;
            result.hidden = false;
        } catch (err) {
            headlineEl.textContent = 'Something went wrong';
            detailEl.textContent = err.message || 'Try again in a moment.';
            verdictEl.textContent = 'Error';
            verdictEl.dataset.verdict = 'No';
            fact('balance').textContent = '—';
            fact('recurring').textContent = '—';
            fact('available').textContent = '—';
            budgetRow.hidden = true;
            form.hidden = true;
            result.hidden = false;
        } finally {
            submitBtn.disabled = false;
            submitBtn.textContent = prevLabel || 'Can I afford this?';
        }
    });
})();
