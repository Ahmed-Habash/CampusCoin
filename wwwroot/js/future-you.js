(() => {
  const bootEl = document.getElementById("fy-bootstrap");
  if (!bootEl || typeof Chart === "undefined") return;

  Chart.defaults.font.family = '"Plus Jakarta Sans", system-ui, sans-serif';
  Chart.defaults.font.size = 11;
  Chart.defaults.color = "#64748b";

  const boot = JSON.parse(bootEl.textContent || "{}");
  const money = (n) => {
    const code = boot.currencyCode || "USD";
    try {
      return new Intl.NumberFormat(undefined, { style: "currency", currency: code, maximumFractionDigits: 2 }).format(n);
    } catch {
      return `${code} ${Number(n).toFixed(2)}`;
    }
  };

  const sliderInputs = [...document.querySelectorAll("#fy-sliders input[data-key]")];
  const extraInput = document.getElementById("fy-extra");
  const tempGoal = document.getElementById("fy-temp-goal");
  const tempGoalName = document.getElementById("fy-temp-goal-name");
  const purchaseInput = document.getElementById("fy-purchase");
  const decisionEl = document.getElementById("fy-decision");
  const coachEl = document.getElementById("fy-coach");
  let chart;
  let timer;
  let goalTarget = Number(boot.goalTarget || 0);

  function readRequest(purchase) {
    const categories = {};
    for (const input of sliderInputs) {
      categories[input.dataset.key] = Number(input.value || 0);
      const out = document.querySelector(`[data-out="${input.dataset.key}"]`);
      if (out) out.textContent = money(Number(input.value || 0));
    }
    if (extraInput) {
      const out = document.querySelector('[data-out="extra"]');
      if (out) out.textContent = money(Number(extraInput.value || 0));
    }
    return {
      categories,
      extraMonthlySavings: Number(extraInput?.value || 0),
      temporaryGoalTarget: Number(tempGoal?.value || boot.goalTarget || 0),
      temporaryGoalName: tempGoalName?.value || boot.goalName,
      purchaseAmount: purchase != null ? Number(purchase) : Number(purchaseInput?.value || 0)
    };
  }

  function applyPath(card, path) {
    if (!card || !path) return;
    const monthly = card.querySelector('[data-field="monthly"]');
    const months = card.querySelector('[data-field="months"]');
    const date = card.querySelector('[data-field="date"]');
    const extra = card.querySelector('[data-field="extra"]');
    if (monthly) monthly.textContent = path.monthlySavingsFormatted;
    if (months) months.textContent = path.monthsToGoalLabel;
    if (date) date.textContent = path.goalDateLabel;
    if (extra) extra.textContent = `Extra vs current: ${path.extraVsCurrentFormatted}`;
  }

  function buildDatasets(result, labels) {
    const target = Number(result.goalTarget ?? goalTarget ?? 0);
    goalTarget = target;
    const goalData = target > 0 ? labels.map(() => target) : [];

    const series = [
      {
        label: "Current You",
        data: result.currentYou?.accumulation || [],
        borderColor: "#64748b",
        backgroundColor: "transparent",
        tension: 0.28,
        borderWidth: 2.25,
        pointRadius: 0,
        pointHoverRadius: 4
      },
      {
        label: "Smarter You",
        data: result.smarterYou?.accumulation || [],
        borderColor: "#0d9488",
        backgroundColor: "transparent",
        tension: 0.28,
        borderWidth: 2.25,
        pointRadius: 0,
        pointHoverRadius: 4
      },
      {
        label: "Your Scenario",
        data: result.yourScenario?.accumulation || [],
        borderColor: "#2563eb",
        backgroundColor: "transparent",
        tension: 0.28,
        borderWidth: 2.75,
        pointRadius: 0,
        pointHoverRadius: 4
      }
    ];

    if (target > 0) {
      series.push({
        label: "Goal",
        data: goalData,
        borderColor: "rgba(15, 23, 42, 0.35)",
        backgroundColor: "transparent",
        borderDash: [6, 5],
        borderWidth: 1.5,
        pointRadius: 0,
        tension: 0,
        order: 10
      });
    }

    return series;
  }

  function updateChart(result) {
    const labels = result.yourScenario?.monthLabels?.length
      ? result.yourScenario.monthLabels
      : result.currentYou?.monthLabels || [];
    const datasets = buildDatasets(result, labels);

    if (!chart) {
      const ctx = document.getElementById("fy-chart");
      if (!ctx) return;
      chart = new Chart(ctx, {
        type: "line",
        data: { labels, datasets },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          interaction: { mode: "index", intersect: false },
          plugins: {
            legend: {
              position: "bottom",
              labels: {
                boxWidth: 10,
                usePointStyle: true,
                pointStyle: "circle",
                padding: 14,
                font: { size: 11, weight: "600" }
              }
            },
            tooltip: {
              callbacks: {
                label: (c) => `${c.dataset.label}: ${money(c.parsed.y)}`
              }
            }
          },
          scales: {
            x: {
              grid: { display: false },
              ticks: { maxTicksLimit: 8, color: "#64748b", font: { size: 11 } }
            },
            y: {
              beginAtZero: true,
              suggestedMax: Math.max(goalTarget * 1.15 || 0, 100),
              grid: { color: "rgba(15,23,42,0.06)" },
              ticks: {
                color: "#64748b",
                font: { size: 11 },
                callback: (v) => money(v)
              }
            }
          },
          animation: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? false : { duration: 280 }
        }
      });
      return;
    }

    chart.data.labels = labels;
    chart.data.datasets = datasets;
    const peak = Math.max(
      goalTarget || 0,
      ...(result.currentYou?.accumulation || [0]),
      ...(result.smarterYou?.accumulation || [0]),
      ...(result.yourScenario?.accumulation || [0])
    );
    chart.options.scales.y.suggestedMax = Math.max(peak * 1.08, goalTarget * 1.15 || 0, 100);
    chart.update();
  }

  async function simulate(opts = {}) {
    const body = readRequest(opts.purchase);
    try {
      const res = await fetch("/FutureYou/simulate", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json",
          "X-Requested-With": "XMLHttpRequest"
        },
        body: JSON.stringify(body)
      });
      if (!res.ok) return;
      const result = await res.json();
      if (!result.ok) return;

      applyPath(document.querySelector('[data-path="current"]'), result.currentYou);
      applyPath(document.querySelector('[data-path="smarter"]'), result.smarterYou);
      applyPath(document.querySelector('[data-path="scenario"]'), result.yourScenario);
      updateChart(result);

      if (coachEl && result.yourScenario) {
        coachEl.textContent = result.yourScenario.coachingMessage || "";
        coachEl.dataset.tone = result.yourScenario.coachingTone || "neutral";
      }
      if (decisionEl && result.decisionImpact) {
        decisionEl.textContent = result.decisionImpact.message;
      }
      const nameEl = document.querySelector("[data-fy-goal-name]");
      const remainEl = document.querySelector("[data-fy-goal-remaining]");
      const tempPill = document.querySelector("[data-fy-temp-pill]");
      if (nameEl && result.goalName) nameEl.textContent = result.goalName;
      if (remainEl && result.goalRemainingFormatted) remainEl.textContent = result.goalRemainingFormatted;
      if (tempPill) tempPill.style.display = result.usingTemporaryGoal ? "" : "none";
      const msgCard = document.querySelector(".fy-message-card p");
      const msgLabel = document.querySelector(".fy-message-card .tiny-label");
      if (result.futureMessage && msgCard) {
        msgCard.textContent = result.futureMessage.body;
        if (msgLabel) msgLabel.textContent = result.futureMessage.fromLabel;
      }
    } catch {
      /* keep last good state */
    }
  }

  function schedule() {
    clearTimeout(timer);
    timer = setTimeout(() => simulate(), 120);
  }

  for (const input of sliderInputs) input.addEventListener("input", schedule);
  extraInput?.addEventListener("input", schedule);
  tempGoal?.addEventListener("input", schedule);
  tempGoal?.addEventListener("change", schedule);
  tempGoalName?.addEventListener("input", schedule);
  tempGoalName?.addEventListener("change", schedule);
  document.getElementById("fy-temp-goal-apply")?.addEventListener("click", () => simulate());

  document.getElementById("fy-purchase-go")?.addEventListener("click", () => {
    const amount = Number(purchaseInput?.value || purchaseInput?.placeholder || 0);
    simulate({ purchase: amount });
  });

  document.getElementById("fy-try-buttons")?.addEventListener("click", (e) => {
    const btn = e.target.closest("[data-try]");
    if (!btn) return;
    const mode = btn.getAttribute("data-try");
    for (const input of sliderInputs) {
      if (mode === "reset") input.value = Math.round(Number(input.dataset.current || 0));
      if (mode === "smarter") input.value = Math.round(Number(input.dataset.smarter || 0));
      if (mode === "food10" && input.dataset.key === "Food") {
        input.value = Math.round(Number(input.dataset.current || 0) * 0.9);
      }
    }
    if (mode === "extra25" && extraInput) {
      extraInput.value = Math.min(Number(extraInput.max || 150), Number(extraInput.value || 0) + 25);
    }
    if (mode === "reset" && extraInput) extraInput.value = 0;
    if (mode === "smarter" && extraInput) extraInput.value = Math.round(Number(boot.suggestedExtra || 25));
    schedule();
  });

  document.getElementById("fy-tips")?.addEventListener("click", (e) => {
    const btn = e.target.closest("[data-try-plan]");
    if (!btn) return;
    const extra = Number(btn.dataset.extra || 0);
    const category = btn.dataset.category || "";
    const cut = Number(btn.dataset.cut || 0);
    if (extraInput && extra > 0) extraInput.value = Math.min(Number(extraInput.max || 200), extra);
    if (category && cut > 0) {
      const input = sliderInputs.find((i) => i.dataset.key === category);
      if (input) {
        const current = Number(input.dataset.current || input.value || 0);
        input.value = Math.round(current * (1 - cut / 100));
      }
    }
    schedule();
    document.getElementById("fy-sliders")?.scrollIntoView({ behavior: "smooth", block: "start" });
  });

  updateChart({
    currentYou: boot.currentYou,
    smarterYou: boot.smarterYou,
    yourScenario: boot.yourScenario,
    goalTarget: boot.goalTarget
  });
  simulate({ purchase: Number(purchaseInput?.value || 8) });
})();
